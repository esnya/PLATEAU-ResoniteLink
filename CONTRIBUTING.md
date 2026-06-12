# Contributing

Pull requests are welcome.

This repository is maintained on a best-effort basis, so review and follow-up may take time. Small, focused changes are the easiest to review and merge.

Before opening a PR:

- If you use a coding agent for this repository, make sure it reads [AGENTS.md](AGENTS.md).
- Keep runtime and SDK assumptions on .NET 10 unless the change clearly requires otherwise.
- Update the matching `.ja.md` file when you change an English Markdown document.
- When behavior changes, first express correctness in static code where feasible: types, APIs, project dependencies, ownership boundaries, and build-time checks should make invalid code fail to compile. Add or update tests only for remaining dynamic behavior that needs contract locking or regression detection.
- Keep auxiliary git worktrees in the repository root `.worktree/` directory (for example `.../<repo>/.worktree/<name>`), and avoid leaving worktrees in sibling directories or `/tmp`.
- Do not add tests that make static ownership, naming, or architecture the canonical contract. Prefer code first, then centralized analyzer, style, or build policy for mechanically checkable constraints that code alone cannot express.

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

In the PR description, explain what changed, why, and any remaining limitations or follow-up work.

Reviewers should also check:

- the PR states the current contract or correctness criterion it changes
- static correctness is expressed in code or build-time checks where feasible, not only in tests, docs, or review guidance
- mechanically checkable static rules that code alone cannot express are captured by root analyzer, style, or build policy rather than tests
- remaining dynamic behavior has focused tests when code alone cannot lock the contract or catch regressions
- external output contracts have observation, dump, or readback evidence when the normal UI or target surface cannot show the actual emitted payload
- agent guidance and reviewer guidance are updated when current correctness criteria or workflow constraints change

When a commit fully resolves a GitHub issue and is safe to auto-close on merge, use a commit message footer such as `Fixes #81` or `Closes #85`. For intermediate cuts, partial migrations, or follow-up-only commits, use `Refs #81` instead.
