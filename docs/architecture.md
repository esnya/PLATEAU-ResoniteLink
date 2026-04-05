# Architecture

## Layers

- `src/Plateau.ResoniteLink.Domain`
  Holds normalized models for PLATEAU inputs and Resonite-facing mesh/material payloads.
- `src/Plateau.ResoniteLink.Application`
  Holds validation, CityGML-to-payload generation, and orchestration around the Resonite-facing adapter.
- `src/Plateau.ResoniteLink.Cli`
  Holds command-line syntax, process I/O, and the current ResoniteLink live adapter.
- `tests/Plateau.ResoniteLink.Tests`
  Verifies CLI syntax, requirement-level application behavior, and deterministic live payload generation.

## Boundaries

- Writing into Resonite must happen behind an application-layer abstraction. The CLI only wires the current adapter.
- PLATEAU concepts such as dataset, mesh code, and local versus remote sources are normalized in domain models, while user-facing option names stay aligned with the Unity SDK where practical.
- ResoniteLink asset I/O stays at the edge. The application layer hands the adapter mesh/material payloads, not transport-specific commands.
- Large imports must stream city objects asynchronously into downstream adapters instead of materializing the entire live payload in memory first.

## Configuration Strategy

- SDK, analyzer, formatting, and package-version policy stays centralized at the repository root.
- Individual projects keep only project-specific differences.
- CI mirrors the local workflow: `restore -> format -> build -> test`.
