#!/usr/bin/env bash
# Fetch the ScriptHookVDotNet reference assemblies that build.sh compiles
# against. They are third party binaries, so they are not kept in the repo.
#
#   ./tools/get-apiref.sh
#
# 3.9 Enhanced is not published as a downloadable asset here, so it is copied
# from a local GTA V Enhanced install if one is present.

set -u
cd "$(dirname "$0")/.."
mkdir -p apiref

grab() {   # url  member  dest
  local url="$1" dest="$3"
  if [ -f "apiref/$dest" ]; then echo "have  $dest"; return; fi
  echo "fetch $dest"
  curl -sL -m 180 -o apiref/_tmp.zip "$url" || { echo "  download failed"; return 1; }
  python -c "
import zipfile, sys
z = zipfile.ZipFile('apiref/_tmp.zip')
for n in z.namelist():
    if n.lower().endswith('scripthookvdotnet3.dll'):
        open('apiref/$dest','wb').write(z.read(n)); print('  extracted', n); break
else:
    print('  ScriptHookVDotNet3.dll not found in archive'); sys.exit(1)
" || return 1
  rm -f apiref/_tmp.zip
}

grab "https://github.com/scripthookvdotnet/scripthookvdotnet/releases/download/v3.6.0/ScriptHookVDotNet.zip" \
     ScriptHookVDotNet3.dll shvdn360.dll

# Any recent nightly will do; pin one so the check is reproducible.
grab "https://github.com/scripthookvdotnet/scripthookvdotnet-nightly/releases/download/v3.7.0-nightly.189/ScriptHookVDotNet-v3.7.0-nightly.189.zip" \
     ScriptHookVDotNet3.dll shvdnNightly.dll

ENH="/c/Program Files (x86)/Steam/steamapps/common/Grand Theft Auto V Enhanced/ScriptHookVDotNet3.dll"
if [ -f "apiref/shvdn390.dll" ]; then
  echo "have  shvdn390.dll"
elif [ -f "$ENH" ]; then
  cp "$ENH" apiref/shvdn390.dll && echo "copied shvdn390.dll from the local Enhanced install"
else
  echo "skip  shvdn390.dll - no local GTA V Enhanced install found"
  echo "      copy ScriptHookVDotNet3.dll from an Enhanced install into apiref/shvdn390.dll"
fi

echo
ls -la apiref/
