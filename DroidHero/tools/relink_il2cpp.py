#!/usr/bin/env python3
"""Rebuilds the native IL2CPP side from a compiled Assembly-CSharp.dll, without the Editor.

The Bee DAG under Library/Bee/Player*.dag.json holds every command the last real build ran,
verbatim, so this replays those five stages instead of guessing at flags:

  1. UnityLinker strips the assemblies
  2. il2cpp --convert-to-cpp regenerates the C++ and the metadata
  3. clang compiles the C++, llvm-ar archives it, clang++ links libil2cpp.so
  4. llvm-objcopy strips the linked library
  5. the results are copied into the Gradle project, plus global-metadata.dat

Two details cost releases in the past and are handled explicitly:

* The DAG records absolute paths from whichever build wrote it. A project rename leaves the
  arm32 nodes pointing at the old directory, so every path is rewritten to this checkout.
* Step 1 reads PlayerScriptAssemblies, and step 4 reads libil2cpp.dbg.so. Both are stale
  outputs, so the fresh files are copied over them first. Skip that and the pipeline happily
  rebuilds the previous version.

Metadata usage indices are baked into every generated file, so a C# change invalidates all
of the generated C++. Every object is recompiled; only files whose text changed would leave
`libil2cpp.so` reading the wrong type tokens.

    python3 tools/relink_il2cpp.py --dll /tmp/dhout/DroidHero.dll
"""

import argparse
import concurrent.futures
import glob
import json
import os
import shlex
import shutil
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GRADLE = os.path.join(ROOT, "Library", "Bee", "Android", "Prj", "IL2CPP", "Gradle")
UNITY_LIB = os.path.join(GRADLE, "unityLibrary", "src", "main")

# Nodes are addressed by annotation, since the DAG's numeric ids shift between builds.
STAGE = {
    "linker": "UnityLinker",
    "il2cpp": "IL2CPP_CodeGen",
    "objcopy": "NdkObjCopy",
}
ARCH_ANNOTATION = {
    "armeabi-v7a": "C_Android_arm32",
    "arm64-v8a": "C_Android_arm64",
}
ARCH_LINK = {
    "armeabi-v7a": "Link_Android_arm32",
    "arm64-v8a": "Link_Android_arm64",
}
AR_BIN = "/workspace/unity/sdk/ndk/27.2.12479018/toolchains/llvm/prebuilt/linux-x86_64/bin/llvm-ar"

ARCH_AR = {
    "armeabi-v7a": "Lib_Android_arm32",
    "arm64-v8a": "Lib_Android_arm64",
}


def load_dag():
    dags = sorted(glob.glob(os.path.join(ROOT, "Library", "Bee", "Player*.dag.json")))
    if not dags:
        raise SystemExit("no Library/Bee/Player*.dag.json; run one Editor build first")
    # Newest wins: a rename regenerates the DAG with current paths.
    dag = max(dags, key=os.path.getmtime)
    with open(dag) as fh:
        return dag, json.load(fh)


def retarget(action):
    """Points a recorded command line at this checkout.

    The DAG is written by the build that created it, so after a rename the paths still name
    the old project. Only the project root is rewritten; Unity and the NDK stay where they are.
    """
    stale = set()
    for old in ("/workspace/project/BandHero", "/workspace/project/DroidHero"):
        if old != ROOT:
            stale.add(old)
    for old in stale:
        action = action.replace(old, ROOT)
    return action


def nodes_by_annotation(dag):
    out = {}
    for node in dag["Nodes"]:
        annotation = (node.get("Annotation") or "").strip()
        if annotation:
            out.setdefault(annotation.split(" ")[0], []).append(node)
    return out


def run(action, cwd=ROOT, label=""):
    if label:
        print("  " + label, flush=True)
    result = subprocess.run(action, shell=True, cwd=cwd, capture_output=True, text=True)
    if result.returncode != 0:
        sys.stderr.write(result.stdout[-4000:])
        sys.stderr.write(result.stderr[-4000:])
        raise SystemExit("failed: " + action[:160])
    return result.stdout


def stage_linker(nodes, dll):
    """Strips assemblies. Its input is PlayerScriptAssemblies, which must hold the new DLL."""
    player_assemblies = os.path.join(ROOT, "Library", "Bee", "PlayerScriptAssemblies")
    os.makedirs(player_assemblies, exist_ok=True)
    # The assembly name inside the DLL is its file name, and UnityLinker writes its output
    # under that same name. Compiling to DroidHero.dll makes the linker emit DroidHero.dll
    # and leave the stale Assembly-CSharp.dll in place, which then gets linked instead.
    if os.path.basename(dll) != "Assembly-CSharp.dll":
        raise SystemExit("compile to Assembly-CSharp.dll, not " + os.path.basename(dll))

    target = os.path.join(player_assemblies, "Assembly-CSharp.dll")
    shutil.copy2(dll, target)
    print("copied", os.path.basename(dll), "->", target)

    node = nodes[STAGE["linker"]][0]
    run(retarget(node["Action"]), label="UnityLinker")
    return os.path.join(ROOT, "Library", "Bee", "artifacts", "Android", "ManagedStripped")


def stage_il2cpp(nodes):
    node = nodes[STAGE["il2cpp"]][0]
    run(retarget(node["Action"]), label="il2cpp --convert-to-cpp")
    return os.path.join(ROOT, "Library", "Bee", "artifacts", "Android", "il2cppOutput")


def compile_arch(nodes, arch, jobs, cpp_dir):
    """Rebuilds every object for one ABI, then archives, links, and strips the library.

    The output directory holds objects for more than the generated C++: the bundled runtime
    (bdwgc, libil2cpp helpers) compiles into the same directory, and the recorded link names
    those objects explicitly. So the recorded compile nodes are all replayed, and only then
    are the .cpp files that have no node of their own compiled on top.

    Two things make this more than a replay. il2cpp can split an assembly into an extra part
    as the C# grows, and the DAG only names the parts the previous build produced, so those
    new parts are compiled to a derived name and appended to both the archive and the link.
    And every object is rebuilt rather than reused: metadata usage indices are baked into
    each file, so a stale object links cleanly but reads the wrong type tokens.
    """
    recorded = nodes[ARCH_ANNOTATION[arch]]
    template = None
    outputs = {}
    for node in recorded:
        action = node["Action"]
        if "-c " not in action or "il2cppOutput" not in action:
            continue
        if template is None:
            template = action
        source = action.split(' -c ')[-1].split('"')[-2]
        output = action.split(' -o "')[1].split('"')[0]
        outputs[os.path.basename(source)] = output
    if template is None:
        raise SystemExit("no il2cpp compile node recorded for " + arch)

    out_dir = os.path.dirname(template.split(' -o "')[1].split('"')[0])
    print("compiling %d recorded objects for %s" % (len(recorded), arch), flush=True)

    def build(action):
        result = subprocess.run(retarget(action), shell=True, cwd=ROOT,
                                capture_output=True, text=True)
        return action, result.returncode, result.stderr

    def run_all(actions, label):
        failures = []
        with concurrent.futures.ThreadPoolExecutor(max_workers=jobs) as pool:
            for action, code, err in pool.map(build, actions):
                if code != 0:
                    failures.append((action, err))
        if failures:
            for action, err in failures[:3]:
                sys.stderr.write(err[-2500:])
            raise SystemExit("%d object(s) failed to compile for %s" % (len(failures), label))

    run_all([node["Action"] for node in recorded], arch)

    # Parts il2cpp added since the recorded build have no node, so they get a derived name.
    generated = sorted(glob.glob(os.path.join(cpp_dir, "*.cpp")))
    known = set(outputs)
    extra_sources = [p for p in generated if os.path.basename(p) not in known]
    if extra_sources:
        print("compiling %d new object(s) for %s" % (len(extra_sources), arch), flush=True)
        old_source = template.split(' -c ')[-1].split('"')[-2]
        old_output = template.split(' -o "')[1].split('"')[0]
        actions = []
        for source in extra_sources:
            name = os.path.splitext(os.path.basename(source))[0] + ".o"
            actions.append(template.replace(old_source, source)
                                   .replace(old_output, os.path.join(out_dir, name)))
        run_all(actions, arch)

    # The recorded ar covers the runtime objects too, so it is replayed and the new parts
    # appended. Rebuilding it from out_dir alone would drop objects from other directories.
    ar_node = nodes[ARCH_AR[arch]][0]
    archive = os.path.join(ROOT, retarget(ar_node["Annotation"]).split(" ", 1)[1])
    if os.path.exists(archive):
        os.remove(archive)
    ar_args = shlex.split(retarget(ar_node["Action"]))
    archived = {os.path.basename(a) for a in ar_args if a.endswith(".o")}
    extras = [o for o in sorted(glob.glob(os.path.join(out_dir, "*.o")))
              if os.path.basename(o) not in archived]
    run(" ".join(shlex.quote(a) for a in ar_args), label="llvm-ar " + arch)

    # Same reasoning for the link: keep the recorded flags and libraries, add the new parts.
    # The new parts go on the command line only. Putting them in the archive as well makes
    # the linker pull them twice and fail on duplicate symbols.
    link_node = nodes[ARCH_LINK[arch]][0]
    link_args = shlex.split(retarget(link_node["Action"]))
    linked_objects = {os.path.basename(a) for a in link_args if a.endswith(".o")}
    link_args += [o for o in extras if os.path.basename(o) not in linked_objects]
    run(" ".join(shlex.quote(a) for a in link_args), label="link libil2cpp.so " + arch)
    linked = os.path.join(ROOT, retarget(link_node["Annotation"]).split(" ", 1)[1])

    # objcopy reads libil2cpp.dbg.so, not the link output, so the fresh link goes there first.
    objcopy_node = nodes[STAGE["objcopy"]][0]
    objcopy_dirs = sorted(glob.glob(os.path.join(ROOT, "Library", "Bee", "artifacts", "objcopy_*")))
    if not objcopy_dirs:
        raise SystemExit("no Library/Bee/artifacts/objcopy_* directory")
    # One scratch directory per ABI: both architectures would otherwise overwrite the same
    # libil2cpp.dbg.so and the second copy would ship the first ABI's library.
    dbg = os.path.join(objcopy_dirs[0], arch, "libil2cpp.dbg.so")
    os.makedirs(os.path.dirname(dbg), exist_ok=True)
    shutil.copy2(linked, dbg)

    # The recorded action names whichever ABI built last, so rebuild it per ABI. Only the
    # input and output paths are rewritten; argv[0] stays the NDK's llvm-objcopy.
    args = shlex.split(retarget(objcopy_node["Action"]))
    stripped = os.path.join(os.path.dirname(dbg), "stripped", "libil2cpp.so")
    os.makedirs(os.path.dirname(stripped), exist_ok=True)
    for i in range(1, len(args)):
        if args[i].endswith("libil2cpp.dbg.so"):
            args[i] = dbg
        elif args[i].endswith("stripped/libil2cpp.so"):
            args[i] = stripped
    run(" ".join(shlex.quote(a) for a in args), label="llvm-objcopy " + arch)
    return stripped


def stage_copy(stripped_by_arch, il2cpp_output):
    """Installs the libraries and the metadata the runtime actually reads names from."""
    for arch, stripped in stripped_by_arch.items():
        dest = os.path.join(UNITY_LIB, "jniLibs", arch, "libil2cpp.so")
        shutil.copy2(stripped, dest)
        print("installed", os.path.relpath(dest, ROOT))

    managed = os.path.join(UNITY_LIB, "assets", "bin", "Data", "Managed")
    for name, sub in (("Metadata", "Metadata"), ("Resources", "Resources")):
        src_dir = os.path.join(il2cpp_output, "data", sub)
        dst_dir = os.path.join(managed, sub)
        os.makedirs(dst_dir, exist_ok=True)
        for src in glob.glob(os.path.join(src_dir, "*")):
            shutil.copy2(src, os.path.join(dst_dir, os.path.basename(src)))
            print("installed", os.path.relpath(os.path.join(dst_dir, os.path.basename(src)), ROOT))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dll", required=True, help="compiled Assembly-CSharp.dll")
    parser.add_argument("--jobs", type=int, default=8)
    parser.add_argument("--skip-linker", action="store_true",
                        help="reuse the already stripped assemblies")
    args = parser.parse_args()

    dag_path, dag = load_dag()
    print("replaying", os.path.relpath(dag_path, ROOT))
    nodes = nodes_by_annotation(dag)

    if not args.skip_linker:
        stage_linker(nodes, args.dll)

    stripped_dll = os.path.join(ROOT, "Library", "Bee", "artifacts", "Android",
                                "ManagedStripped", "Assembly-CSharp.dll")
    if os.path.getmtime(stripped_dll) < os.path.getmtime(args.dll):
        raise SystemExit("UnityLinker did not refresh Assembly-CSharp.dll")

    il2cpp_output = stage_il2cpp(nodes)
    cpp_dir = os.path.join(il2cpp_output, "cpp")

    stripped = {}
    for arch in ("armeabi-v7a", "arm64-v8a"):
        stripped[arch] = compile_arch(nodes, arch, args.jobs, cpp_dir)

    stage_copy(stripped, il2cpp_output)
    print("relink complete")


if __name__ == "__main__":
    main()
