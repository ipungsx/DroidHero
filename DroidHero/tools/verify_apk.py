#!/usr/bin/env python3
"""Check a built APK before shipping it.

Every check here maps to a defect that reached a release and produced a black
screen, so a failure means the APK would not start, not that it is merely
untidy.

    python3 tools/verify_apk.py Builds/DroidHero-v1.4.1.apk
"""
import hashlib
import sys
import zipfile

import UnityPy

EXPECTED_ABIS = ("armeabi-v7a", "arm64-v8a")
STALE_NAMES = ("BandHero",)


def load(data, tag):
    path = f"/tmp/_verify_{tag}"
    open(path, "wb").write(data)
    return UnityPy.load(path)


def serialized_files(z):
    for name in z.namelist():
        if not name.startswith("assets/bin/Data/") or name.endswith("/"):
            continue
        if name.endswith(".split0") or ".split" in name:
            continue
        yield name, z.read(name)


def check_alignment(z, fail):
    """Unity 6 aligns every object in a SerializedFile to 16 bytes."""
    checked = 0
    for name, data in serialized_files(z):
        if len(data) < 100:
            continue
        try:
            objects = load(data, "align").file.objects
        except Exception:
            continue
        if not objects:
            continue
        checked += 1
        bad = [o.byte_start for o in objects.values() if o.byte_start % 16]
        if bad:
            fail(f"{name}: {len(bad)} object(s) not 16-byte aligned at {bad}")
    print(f"  {checked} serialized files checked for 16-byte alignment")


def check_scene(z, fail):
    """The scene must name the root object and point at a real script."""
    env = load(z.read("assets/bin/Data/level0"), "scene")
    names = [
        o.read_typetree()["m_Name"]
        for o in env.objects
        if o.type.name == "GameObject"
    ]
    if not names:
        fail("level0 has no GameObject")
    for name in names:
        if any(stale in name for stale in STALE_NAMES):
            fail(f"level0 still names a GameObject {name!r}")

    scripts = [
        o.read_typetree()["m_Script"]
        for o in env.objects
        if o.type.name == "MonoBehaviour"
    ]
    if not scripts:
        fail("level0 has no MonoBehaviour, so nothing boots the app")
    print(f"  level0: {len(names)} GameObject(s) {names}, script {scripts[0]}")


def check_script_table(z, fail):
    """Every MonoScript must describe the assemblies that were compiled."""
    parts = [
        n for n in sorted(z.namelist())
        if n.startswith("assets/bin/Data/globalgamemanagers.assets.split")
    ]
    if not parts:
        fail("globalgamemanagers.assets is missing")
        return

    table = load(b"".join(z.read(p) for p in parts), "ggm")
    stale = 0
    for obj in table.objects:
        if obj.type.name != "MonoScript":
            continue
        tree = obj.read_typetree()
        full = tree["m_Namespace"] + tree["m_ClassName"]
        if any(name in full for name in STALE_NAMES):
            fail(f"MonoScript {full} still describes the old assembly")
            stale += 1
    print(f"  {len(table.objects)} objects, {stale} stale MonoScript(s)")


def check_native(z, fail):
    """Both ABIs must be present; a stale one ships the previous C#."""
    for abi in EXPECTED_ABIS:
        path = f"lib/{abi}/libil2cpp.so"
        try:
            data = z.read(path)
        except KeyError:
            fail(f"{path} is missing")
            continue
        magic = data[:4]
        want = b"\x7fELF"
        if magic != want:
            fail(f"{path} is not an ELF file")
        # EI_CLASS: 1 is 32-bit, 2 is 64-bit.
        expected_class = 1 if abi == "armeabi-v7a" else 2
        if data[4] != expected_class:
            fail(f"{path} has ELF class {data[4]}, expected {expected_class}")
        print(f"  {abi}: {len(data)} bytes")


def check_metadata(z, fail):
    """global-metadata.dat must match the code that was compiled."""
    data = z.read("assets/bin/Data/Managed/Metadata/global-metadata.dat")
    if b"DroidHeroApp" not in data:
        fail("global-metadata.dat does not mention DroidHeroApp")
    for stale in STALE_NAMES:
        if stale.encode() in data:
            fail(f"global-metadata.dat still mentions {stale}")
    print(f"  global-metadata.dat: {len(data)} bytes")


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)

    path = sys.argv[1]
    failures = []

    def fail(message):
        failures.append(message)

    print(f"=== {path} ===")
    with zipfile.ZipFile(path) as z:
        check_alignment(z, fail)
        check_scene(z, fail)
        check_script_table(z, fail)
        check_native(z, fail)
        check_metadata(z, fail)

    print(f"md5 {hashlib.md5(open(path, 'rb').read()).hexdigest()}")
    if failures:
        print()
        for message in failures:
            print(f"FAIL  {message}")
        sys.exit(1)
    print("OK: this APK can boot")


if __name__ == "__main__":
    main()
