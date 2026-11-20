#!/usr/bin/env python3
"""Rename the scene's root GameObject without breaking Unity's layout.

Unity 6 aligns every object in a SerializedFile to 16 bytes and lays out the
object table from that alignment. UnityPy pads to 8, so a plain
`env.file.save()` shrinks the file and moves objects onto offsets the player
never writes. That is what shipped in v1.4.0 and it drew a black screen.

This forces the writer to Unity's alignment and refuses to emit anything unless
round-tripping the untouched scene reproduces it byte for byte.

    python3 tools/rename_scene_object.py <pristine level0> <out> OldName NewName
"""
import hashlib
import sys

import UnityPy
from UnityPy.streams import EndianBinaryWriter

_orig_align = EndianBinaryWriter.align_stream


def _align_unity(self, alignment=4):
    return _orig_align(self, 16 if alignment == 8 else alignment)


EndianBinaryWriter.align_stream = _align_unity


def load(data, tag):
    path = f"/tmp/_scene_{tag}"
    open(path, "wb").write(data)
    return UnityPy.load(path)


def main():
    if len(sys.argv) != 5:
        sys.exit(__doc__)

    src, dst, old_name, new_name = sys.argv[1:5]
    pristine = open(src, "rb").read()

    roundtrip = load(pristine, "rt").file.save()
    if roundtrip != pristine:
        sys.exit(
            f"refusing to write: the writer does not reproduce {src} "
            f"({len(pristine)} -> {len(roundtrip)} bytes)"
        )

    env = load(pristine, "edit")
    renamed = 0
    for obj in env.objects:
        if obj.type.name != "GameObject":
            continue
        tree = obj.read_typetree()
        if tree["m_Name"] == old_name:
            tree["m_Name"] = new_name
            obj.save_typetree(tree)
            renamed += 1

    if renamed != 1:
        sys.exit(f"expected one GameObject named {old_name!r}, found {renamed}")

    data = env.file.save()

    misaligned = [
        o.byte_start for o in load(data, "check").file.objects.values()
        if o.byte_start % 16
    ]
    if misaligned:
        sys.exit(f"refusing to write: objects not 16-byte aligned at {misaligned}")

    open(dst, "wb").write(data)
    print(f"{dst}: {len(data)} bytes md5={hashlib.md5(data).hexdigest()}")


if __name__ == "__main__":
    main()
