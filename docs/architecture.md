# Architecture

## Layers

- `src/Plateau.ResoniteLink.Domain`
  Holds normalized models for PLATEAU inputs and Resonite construction plans.
- `src/Plateau.ResoniteLink.Application`
  Holds validation, CityGML-to-plan generation, and orchestration around the Resonite-facing adapter.
- `src/Plateau.ResoniteLink.Cli`
  Holds command-line syntax, process I/O, the JSON artifact adapter, and the current ResoniteLink live adapter.
- `tests/Plateau.ResoniteLink.Tests`
  Verifies CLI syntax, requirement-level application behavior, and the deterministic output contract.

## Boundaries

- Writing into Resonite must happen behind an application-layer abstraction. The CLI only wires the current adapter.
- PLATEAU concepts such as dataset, mesh code, and local versus server-backed sources are normalized in domain models.
- The first implementation keeps one shared construction contract for both JSON artifacts and the live ResoniteLink adapter, split into stable metadata plus a sequential city-object stream.
- ResoniteLink asset I/O stays at the edge. The application layer hands the adapter mesh/material payloads, not transport-specific commands.
- Large imports must stream city objects asynchronously into downstream adapters instead of materializing the entire live payload in memory first.

## Configuration Strategy

- SDK, analyzer, formatting, and package-version policy stays centralized at the repository root.
- Individual projects keep only project-specific differences.
- CI mirrors the local workflow: `restore -> format -> build -> test`.
