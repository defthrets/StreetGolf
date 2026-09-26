#!/usr/bin/env python
"""
Checks every native call in StreetGolf.cs passes the number of arguments the
native takes. A wrong count is not a compile error, it is a crash to desktop
the first time the line runs, so this is run after every change.

    python tools/audit_natives.py path/to/natives.json

natives.json is alloc8or's native database (github.com/alloc8or/gta5-nativedb-data).
"""
import io
import json
import re
import sys

BACKSLASH = chr(92)


def load_db(path):
    db = json.load(io.open(path, encoding="utf-8"))
    by_name, by_hash = {}, {}
    for ns in db.values():
        for h, d in ns.items():
            n = len(d.get("params", []))
            by_hash[int(h, 16)] = (d.get("name", "?"), n)
            by_name[d.get("name", "")] = n
            for old in d.get("old_names", []) or []:
                by_name.setdefault(old, n)
    return by_name, by_hash


def split_top(text):
    """Split on commas that are not inside brackets or strings."""
    parts, cur, depth, in_str, i = [], "", 0, False, 0
    while i < len(text):
        c = text[i]
        if in_str:
            cur += c
            if c == BACKSLASH and i + 1 < len(text):
                cur += text[i + 1]
                i += 2
                continue
            if c == '"':
                in_str = False
        elif c == '"':
            in_str = True
            cur += c
        elif c in "([{":
            depth += 1
            cur += c
        elif c in ")]}":
            depth -= 1
            cur += c
        elif c == "," and depth == 0:
            parts.append(cur.strip())
            cur = ""
        else:
            cur += c
        i += 1
    if cur.strip():
        parts.append(cur.strip())
    return parts


def call_body(src, start):
    """The text between the call's opening bracket and its matching close."""
    depth, in_str, i = 0, False, start
    while i < len(src):
        c = src[i]
        if in_str:
            if c == BACKSLASH:
                i += 2
                continue
            if c == '"':
                in_str = False
        elif c == '"':
            in_str = True
        elif c in "([{":
            depth += 1
        elif c in ")]}":
            if depth == 0:
                return src[start:i]
            depth -= 1
        i += 1
    return src[start:]


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    by_name, by_hash = load_db(sys.argv[1])
    src = io.open("StreetGolf.cs", encoding="utf-8-sig").read()

    calls, bad, unknown = 0, [], []
    for m in re.finditer(r"Function\.Call(?:<[^>]+>)?\(", src):
        args = split_top(call_body(src, m.end()))
        if not args:
            continue
        head, rest = args[0], args[1:]
        if len(rest) == 1 and rest[0].startswith("new InputArgument[]"):
            inner = rest[0][rest[0].index("{") + 1: rest[0].rindex("}")]
            rest = split_top(inner)
        line = src.count("\n", 0, m.start()) + 1
        calls += 1

        mh = re.match(r"\(Hash\)0x([0-9A-Fa-f]+)UL?", head)
        if mh:
            name, n = by_hash.get(int(mh.group(1), 16), ("0x" + mh.group(1), None))
        else:
            mn = re.match(r"Hash\.(\w+)", head)
            if not mn:
                unknown.append((line, head))
                continue
            name = mn.group(1)
            n = by_name.get(name)
        if n is None:
            unknown.append((line, name))
            continue
        if len(rest) != n:
            bad.append((line, name, len(rest), n))

    print("native calls checked: %d" % calls)
    print("argument count mismatches: %d" % len(bad))
    for b in bad:
        print("   line %d  %s  passes %d, the native takes %d" % b)
    if unknown:
        print("not found in the database: %d" % len(unknown))
        for u in unknown:
            print("   line %d  %s" % u)
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
