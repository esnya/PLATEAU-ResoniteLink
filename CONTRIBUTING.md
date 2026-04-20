# Contributing

Pull requests are welcome.

This repository is maintained on a best-effort basis, so review and follow-up may take time. Small, focused changes are the easiest to review and merge.

Before opening a PR:

- If you use a coding agent for this repository, make sure it reads [AGENTS.md](AGENTS.md).
- Keep runtime and SDK assumptions on .NET 10 unless the change clearly requires otherwise.
- Update the matching `.ja.md` file when you change an English Markdown document.
- Add or update tests when behavior changes.
- Keep auxiliary git worktrees in the repository root `.worktree/` directory (for example `.../<repo>/.worktree/<name>`), and avoid leaving worktrees in sibling directories or `/tmp`.
- Do not add grep-based architecture or naming tests to enforce concept ownership. Prefer project-reference boundaries, review checklist enforcement, and behavior-oriented tests.

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
dotnet restore PlateauResoniteLink.sln --locked-mode --disable-build-servers
dotnet format whitespace . --folder --verify-no-changes
dotnet build PlateauResoniteLink.sln --configuration Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet test PlateauResoniteLink.sln --configuration Release --no-restore --verbosity normal -m:1 --disable-build-servers -p:UseSharedCompilation=false
```

Skill-owned tests under `.agents/skills/` are intentionally kept outside `PlateauResoniteLink.sln`.
When you change `.agents/skills/resonite-live-send-debug/tools/session-tool.cs` or its skill-local test contracts, run this additional verification command before push or PR update:

```bash
dotnet restore .agents/skills/resonite-live-send-debug/tools/tests/ResoniteLiveSendDebug.ToolTests.csproj --disable-build-servers
dotnet test .agents/skills/resonite-live-send-debug/tools/tests/ResoniteLiveSendDebug.ToolTests.csproj --no-restore --verbosity normal -m:1 --disable-build-servers -p:UseSharedCompilation=false
```

For quick non-slow iteration between low-conflict changes, use:

```bash
dotnet test PlateauResoniteLink.sln --configuration Release --no-restore --verbosity minimal -m:1 --disable-build-servers -p:UseSharedCompilation=false --filter "Category!=Slow"
```

If you need to keep a large repository-improvement plan around temporarily, keep it under `.tmp/plans/` and leave it untracked. Do not treat that area as canonical documentation, do not link it from active docs as current operating guidance, and reflect only adopted conclusions in tracked documentation and code review artifacts.

In the PR description, explain what changed, why, and any remaining limitations or follow-up work.

Reviewers should also check:

- concept names, directory placement, and namespace placement match ownership without leaving compatibility aliases behind
- project references still enforce the intended dependency direction
- behavior changes are covered by behavior-oriented tests rather than grep-based naming or boundary checks
- agent guidance and reviewer guidance are updated when naming or boundary rules change

When a commit fully resolves a GitHub issue and is safe to auto-close on merge, use a commit message footer such as `Fixes #81` or `Closes #85`. For intermediate cuts, partial migrations, or follow-up-only commits, use `Refs #81` instead.
