# apiref

Reference copies of `ScriptHookVDotNet3.dll` used only to check that
`StreetGolf.cs` compiles against every API version the mod supports. Nothing
here ships with the mod.

They are third party binaries, so they are not committed. Fetch them with:

```
./tools/get-apiref.sh
```

Expected files:

| File | Source |
| --- | --- |
| `shvdn360.dll` | SHVDN v3.6.0 release |
| `shvdnNightly.dll` | SHVDN v3.7.0 nightly |
| `shvdn390.dll` | copied from a local GTA V Enhanced install |

`build.sh` skips any that are missing and tells you which.
