# Contributing

Pull requests are welcome.

This repository is maintained on a best-effort basis, so review and follow-up may take time. Small, focused changes are the easiest to review and merge.

Before opening a PR:

- If you use a coding agent for this repository, make sure it reads [AGENTS.md](AGENTS.md).
- Keep runtime and SDK assumptions on .NET 10 unless the change clearly requires otherwise.
- Update the matching `.ja.md` file when you change an English Markdown document.
- Add or update tests when behavior changes.
- Keep auxiliary git worktrees in the repository root `.worktree/` directory (for example `.../<repo>/.worktree/<name>`), and avoid leaving worktrees in sibling directories or `/tmp`.

GitHub Releases are the canonical changelog. Create release tags as `vX.Y.Z`; each tag publishes a framework-dependent CLI zip asset and generates release notes from merged pull requests.

Release labels are optional, but using them keeps generated notes readable:

- `feature`, `enhancement`: user-facing additions
- `fix`, `bug`, `bugfix`: behavior fixes
- `documentation`, `docs`, `meta`, `chore`: docs and maintenance
- `test`, `tests`, `internal`, `refactor`, `ci`, `build`: internal validation and infrastructure work
- `skip-release-notes`: omit a PR from generated notes when it should stay out of the changelog

Unlabeled PRs fall through to the catch-all `Other changes` section in the generated release notes.

The canonical verification command sequence is:

```bash
dotnet restore Plateau.ResoniteLink.sln --locked-mode --disable-build-servers
dotnet format whitespace . --folder --verify-no-changes
dotnet build Plateau.ResoniteLink.sln --configuration Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet test Plateau.ResoniteLink.sln --configuration Release --no-restore --verbosity normal -m:1 --disable-build-servers -p:UseSharedCompilation=false
```

For quick non-slow iteration between low-conflict changes, use:

```bash
dotnet test Plateau.ResoniteLink.sln --configuration Release --no-restore --verbosity minimal -m:1 --disable-build-servers -p:UseSharedCompilation=false --filter "Category!=Slow"
```

If you need to keep a large repository-improvement plan around temporarily, keep it under `.tmp/plans/` and leave it untracked. Do not treat that area as canonical documentation, do not link it from active docs as current operating guidance, and reflect only adopted conclusions in tracked documentation and code review artifacts.

In the PR description, explain what changed, why, and any remaining limitations or follow-up work.
