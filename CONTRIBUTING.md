# Contributing

Pull requests are welcome.

This repository is maintained on a best-effort basis, so review and follow-up may take time. Small, focused changes are the easiest to review and merge.

Before opening a PR:

- If you use a coding agent for this repository, make sure it reads [AGENTS.md](AGENTS.md).
- Keep runtime and SDK assumptions on .NET 10 unless the change clearly requires otherwise.
- Update the matching `.ja.md` file when you change an English Markdown document.
- Add or update tests when behavior changes.

GitHub Releases are the canonical changelog. Create release tags as `vX.Y.Z`; each tag publishes a framework-dependent CLI zip asset and generates release notes from merged pull requests.

Release labels are optional, but using them keeps generated notes readable:

- `feature`, `enhancement`: user-facing additions
- `fix`, `bug`, `bugfix`: behavior fixes
- `documentation`, `docs`, `meta`, `chore`: docs and maintenance
- `test`, `tests`, `internal`, `refactor`, `ci`, `build`: internal validation and infrastructure work
- `skip-release-notes`: omit a PR from generated notes when it should stay out of the changelog

Unlabeled PRs fall through to the catch-all `Other changes` section in the generated release notes.

For Codex Cloud / ephemeral agents where `dotnet` is missing, run `./scripts/setup-codex-cloud.sh` to bootstrap SDK 10 and execute the standard checks.

If you can, run:

```bash
dotnet format whitespace . --folder --verify-no-changes
dotnet test Plateau.ResoniteLink.sln --configuration Release -m:1 -p:UseSharedCompilation=false
```

In the PR description, explain what changed, why, and any remaining limitations or follow-up work.
