## Summary

- what changed
- why it changed

## Verification

- [ ] `dotnet restore PlateauResoniteLink.sln --locked-mode --disable-build-servers`
- [ ] `dotnet format whitespace . --folder --verify-no-changes`
- [ ] `dotnet build PlateauResoniteLink.sln --configuration Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false`
- [ ] `dotnet test PlateauResoniteLink.sln --configuration Release --no-restore --verbosity normal -m:1 --disable-build-servers -p:UseSharedCompilation=false`
- [ ] additional skill-local verification completed when `.agents/skills/` changed

## Review Checklist

- [ ] names and directory placement reflect concept ownership without relying on grep-based tests
- [ ] project references enforce the intended dependency direction
- [ ] new or changed behavior is covered by behavior-oriented tests
- [ ] agent guidance and reviewer guidance were updated when naming or boundary rules changed
- [ ] English and Japanese Markdown mirrors were updated together when needed

## Issues

- list only issues that this PR fully resolves with `Fixes #...`
- use `Refs #...` for intermediate cuts or follow-up work
