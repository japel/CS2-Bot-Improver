#!/usr/bin/env python3
"""Minimal VPK v2 reader: list or extract files from a Source 2 pak01_dir.vpk.

  vpk-extract.py <pak01_dir.vpk> list [substring]
  vpk-extract.py <pak01_dir.vpk> extract <substring> <outdir>
"""
import os, struct, sys


def read_cstr(f):
    out = bytearray()
    while (b := f.read(1)) not in (b"\0", b""):
        out += b
    return out.decode("utf-8", "replace")


def entries(dir_path):
    with open(dir_path, "rb") as f:
        sig, ver, tree_size = struct.unpack("<III", f.read(12))
        assert sig == 0x55AA1234, "not a VPK"
        if ver == 2:
            f.read(16)
        header = f.tell()
        while ext := read_cstr(f):
            while path := read_cstr(f):
                while name := read_cstr(f):
                    crc, preload, arc, off, length, term = struct.unpack("<IHHIIH", f.read(18))
                    pre = f.read(preload)
                    full = f"{'' if path == ' ' else path + '/'}{name}.{ext}"
                    yield full, arc, off, length, pre, header + tree_size


def read_entry(dir_path, arc, off, length, pre, data_start):
    if arc == 0x7FFF:
        with open(dir_path, "rb") as f:
            f.seek(data_start + off)
            return pre + f.read(length)
    with open(dir_path.replace("_dir.vpk", f"_{arc:03d}.vpk"), "rb") as f:
        f.seek(off)
        return pre + f.read(length)


def main():
    vpk, cmd = sys.argv[1], sys.argv[2]
    pat = sys.argv[3] if len(sys.argv) > 3 else ""
    for full, *rest in entries(vpk):
        if pat not in full:
            continue
        if cmd == "list":
            print(full, rest[2])
        else:
            out = os.path.join(sys.argv[4], full)
            os.makedirs(os.path.dirname(out), exist_ok=True)
            with open(out, "wb") as o:
                o.write(read_entry(vpk, *rest))


if __name__ == "__main__":
    main()
