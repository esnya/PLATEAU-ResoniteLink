# Contributing

Pull requests are welcome.

This repository is maintained on a best-effort basis, so review and follow-up may take time. Small, focused changes are the easiest to review and merge.

Before opening a PR:

- If you use a coding agent for this repository, make sure it reads [AGENTS.md](AGENTS.md).
- Keep runtime and SDK assumptions on .NET 10 unless the change clearly requires otherwise.
- Update the matching `.ja.md` file when you change an English Markdown document.
- Add or update tests when behavior changes.

If you can, run:

```bash
dotnet format whitespace . --folder --verify-no-changes
dotnet test Plateau.ResoniteLink.sln --configuration Release -m:1 -p:UseSharedCompilation=false
```

In the PR description, explain what changed, why, and any remaining limitations or follow-up work.
