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

The canonical verification command is:

```bash
bash scripts/verify-ci.sh
```

That script is the repository-owned verification workflow. Keep other documents at the command level and refer back to this script instead of copying or reordering its internal restore/format/build/test sequence.

For Codex Cloud / ephemeral agents where PATH does not already provide a compatible .NET 10 SDK, run `./scripts/setup-codex-cloud.sh` first. That helper exists only to bootstrap such environments and then hand off to `bash scripts/verify-ci.sh`.

If you need to keep a large repository-improvement plan around temporarily, keep it under `.tmp/plans/` and leave it untracked. Do not treat that area as canonical documentation, do not link it from active docs as current operating guidance, and reflect only adopted conclusions in tracked documentation and code review artifacts.

In the PR description, explain what changed, why, and any remaining limitations or follow-up work.
