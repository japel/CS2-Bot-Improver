#!/usr/bin/env python3
"""Find code that references a string in a Linux x86-64 .so (RIP-relative lea).

  xref.py <lib.so> <string>        prints VA of each referencing instruction

Assumes .rodata.str1.1 is mapped at VA == file offset and reads .text's VA/offset from the ELF
section headers.
"""
import re, struct, sys


def sections(data):
    shoff, = struct.unpack_from("<Q", data, 0x28)
    shentsize, shnum, shstrndx = struct.unpack_from("<HHH", data, 0x3A)
    hdrs = [struct.unpack_from("<IIQQQQIIQQ", data, shoff + i * shentsize) for i in range(shnum)]
    strtab_off = hdrs[shstrndx][4]
    out = {}
    for h in hdrs:
        name_end = data.index(b"\0", strtab_off + h[0])
        out[data[strtab_off + h[0]:name_end].decode()] = {"va": h[3], "off": h[4], "size": h[5]}
    return out


def main():
    lib, needle = sys.argv[1], sys.argv[2].encode()
    data = open(lib, "rb").read()
    sec = sections(data)
    ro = sec[".rodata.str1.1"]   # merged C strings live here, not in .rodata
    # the lea points at the start of the containing C string (needle may be a substring)
    starts = {data.rindex(b"\0", 0, m.start()) + 1 for m in re.finditer(re.escape(needle), data)
              if ro["off"] <= m.start() < ro["off"] + ro["size"]}
    targets = sorted(st - ro["off"] + ro["va"] for st in starts)
    if not targets:
        sys.exit(f"string not found in .rodata: {needle!r}")
    text = sec[".text"]
    code = data[text["off"]:text["off"] + text["size"]]
    for t in targets:
        print(f"string @ VA {t:#x}")
        for m in re.finditer(rb"[\x48\x4c]\x8d[\x05\x0d\x15\x1d\x25\x2d\x35\x3d]", code):
            disp, = struct.unpack_from("<i", code, m.start() + 3)
            va = text["va"] + m.start()
            if va + 7 + disp == t:
                print(f"  lea @ VA {va:#x}  (file off {text['off'] + m.start():#x})")


if __name__ == "__main__":
    main()
