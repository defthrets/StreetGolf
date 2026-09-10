#!/usr/bin/env bash
# Assemble StreetGolf.cs from src/ and check it compiles against every
# ScriptHookVDotNet version the mod supports.
#
#   ./build.sh            assemble and compile-check
#   ./build.sh --install  also copy into the game's scripts folder
#
# The reference assemblies in apiref/ are not in the repo; run
# tools/get-apiref.sh once to fetch them.

set -u
cd "$(dirname "$0")"

CSC="/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe"
WINDIR="$(pwd -W 2>/dev/null || pwd)"

if [ ! -x "$CSC" ]; then
  echo "csc.exe not found at $CSC"
  echo "(the .NET Framework 4 compiler ships with Windows; adjust CSC if yours differs)"
  exit 1
fi

# ---- assemble -------------------------------------------------------------
# SHVDN's own compiler is happy either way, but a byte order mark stranded in
# the MIDDLE of a file is not valid, so strip them from the parts and put one
# back at the front.
python -c "
import io
parts = ['src/p1.cs','src/p2.cs','src/p3.cs','src/p4.cs']
out = []
for p in parts:
    t = io.open(p, encoding='utf-8-sig').read()
    out.append(t)
io.open('StreetGolf.cs','w',encoding='utf-8-sig',newline='\n').write(''.join(out))
print('assembled StreetGolf.cs from %d parts' % len(parts))
" || exit 1

# ---- compile against each supported API version ---------------------------
mkdir -p build
fail=0
found=0
for pair in "3.6.0:shvdn360.dll" "nightly-3.7:shvdnNightly.dll" "3.9-enhanced:shvdn390.dll"; do
  name="${pair%%:*}"
  dll="${pair##*:}"
  if [ ! -f "apiref/$dll" ]; then
    echo "--- SHVDN $name : skipped, apiref/$dll missing"
    continue
  fi
  found=$((found + 1))
  out="$WINDIR\\build\\probe_${name}.dll"
  log=$(MSYS_NO_PATHCONV=1 "$CSC" -nologo -target:library -langversion:5 -nowin32manifest \
        -out:"$out" -r:"apiref/$dll" -r:System.dll -r:System.Core.dll \
        -r:System.Drawing.dll -r:System.Windows.Forms.dll StreetGolf.cs 2>&1)
  errs=$(printf '%s\n' "$log" | grep -c "error CS")
  echo "=== SHVDN $name : $errs error(s)"
  if [ "$errs" -ne 0 ]; then
    fail=1
    printf '%s\n' "$log" | grep "error CS" \
      | sed -E 's/^StreetGolf\.cs\([0-9]+,[0-9]+\): //' | sort -u | sed 's/^/     /' | head -20
  fi
done

if [ "$found" -eq 0 ]; then
  echo
  echo "No reference assemblies found. Run tools/get-apiref.sh to fetch them."
  exit 1
fi

if [ "$fail" -ne 0 ]; then
  echo
  echo "BUILD FAILED"
  exit 1
fi

echo
echo "all $found version(s) compile clean"

# ---- optional install -----------------------------------------------------
if [ "${1:-}" = "--install" ]; then
  GAME="/c/Program Files (x86)/Steam/steamapps/common/Grand Theft Auto V Enhanced/scripts"
  if [ -d "$GAME" ]; then
    cp StreetGolf.cs "$GAME/StreetGolf.cs" && cp StreetGolf.ini "$GAME/StreetGolf.ini" \
      && echo "installed to $GAME"
  else
    echo "game scripts folder not found at $GAME"
    exit 1
  fi
fi
