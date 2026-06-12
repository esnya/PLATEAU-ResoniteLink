# AGENTS

Read and apply this file when using a coding agent in this repository.

## Scope
This repository builds a .NET 10 CLI-first import pipeline that maps PLATEAU datasets into Resonite-oriented construction data and live ResoniteLink scene updates.

## Working Rules
- Treat English Markdown files as the canonical source. Whenever an English `.md` file changes, update the matching `.ja.md` file in the same change.
- Treat Japanese Markdown files as translation aids only. When an English and Japanese document disagree, follow the English source and then repair the Japanese mirror.
- Keep runtime and SDK assumptions on .NET 10 unless the user explicitly requests an upgrade.
- Keep shared build, package, formatting, lint, and analyzer policy centralized at the repository root. Do not duplicate those settings inside individual project files unless there is a project-specific exception.
- In Codex or similarly sandboxed WSL environments, do not substitute `dotnet format <sln|csproj>` solution/project mode for the repository verification flow. It can fail while opening the MSBuild workspace with `System.Net.Sockets.SocketException (13): Permission denied /tmp/<guid>`. Use the repository verification command sequence from `CONTRIBUTING.md`, including `dotnet format whitespace . --folder --verify-no-changes`.
- Before every push or pull request update, run the verification command sequence documented in `CONTRIBUTING.md`. `CONTRIBUTING.ja.md` is only its translation mirror.
- Run that verification sequence serially. Do not overlap `dotnet build`, `dotnet test`, or similar commands against the same output tree, because concurrent compiler/testhost access can lock `artifacts/build/windows/obj/.../*.dll` and contaminate the result.

### Repository Truth Surfaces
- Treat code as the primary current truth for behavior, boundaries, ownership, naming, defaults, and dependency direction.
- Make correctness static first. Prefer types, APIs, project dependencies, ownership boundaries, and build-time checks that make invalid states, dependencies, or calls fail to compile.
- Use tests only for current dynamic behavior that code alone cannot lock down, and for catching regressions or bugs in that behavior.
- Prefer reducing dynamic behavior over expanding test volume. When less behavior is dynamic, fewer tests should be necessary.
- Use documentation only for current intent, architecture rationale, workflow constraints, reference notes, and operational context that tests cannot express well.
- Keep past decisions, migration history, removed behavior, and historical rationale in git history, pull requests, issues, and releases. Do not preserve history by keeping obsolete current-state docs, tests, or compatibility surfaces.
- Do not use tests to police static ownership, naming, or architecture. Express those rules in code, or in centralized analyzer, style, or build policy when code alone cannot express them.

- Keep PLATEAU terminology and import semantics aligned with `PLATEAU-SDK-for-UNITY`.
- Treat ResoniteLink and live target integration as external contracts. Correctness is the emitted payload, readback, or observable target state matching the current contract, not a particular internal implementation shape.
- Prefer deterministic outputs, explicit command inputs, and reproducible local/CI behavior.
- When behavior changes, first decide whether the dynamic behavior can be reduced or expressed directly in code. Add or update automated tests only for the remaining dynamic behavior that needs contract locking or regression detection.
- For regression fixes, define the expected observable contract and the counterexamples or observation points that could disprove the proposed cause before treating the fix as complete.
- Under limited information, do not lock onto a single hypothesis. First create observations, tests, or comparisons that can fail that hypothesis, and make only changes that stay valid across plausible interpretations.
- When multiple execution paths emit the same concept, enforce a shared contract and compare equivalent outputs instead of adding path-specific compensation.
- If an output cannot be inspected through the normal UI or external target surface, add a recording, dump, or readback artifact that can inspect the actual emitted payload and use it in the completion criteria.
- Do not treat green tests, CI, or review approval alone as proof that a regression is fixed. Confirm that the observation artifacts relevant to the failure mode satisfy the expected contract.

- Keep auxiliary git worktrees under `<repo>/.worktree/`, and avoid sibling directories or `/tmp` worktrees; this keeps ephemeral worktrees consistently ignored and separated from the main checkout.

## Live Send Workflow
- For Coding Agent live tests, follow [.agents/skills/resonite-live-send-debug/SKILL.md](.agents/skills/resonite-live-send-debug/SKILL.md).
