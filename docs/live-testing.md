# Live Testing

## Scope

Use this workflow only for machine-level checks against a running ResoniteLink listener.

## Constraints

- The ResoniteLink port is session-specific. Do not hard-code it in source control.
- In this environment, WSL cannot reach the Windows-side listener directly.
- Run the live import from Windows and target `localhost`.

## Windows Run

Run the actual CLI import from Windows PowerShell or Command Prompt:

```powershell
dotnet run --project src/Plateau.ResoniteLink.Cli -- `
  build `
  --dataset <dataset> `
  --mesh-code <mesh-code> `
  --source <local-or-server> `
  --resonitelink-port <port>
```

If `--source local` is used, also pass `--input <dataset-root>`.
If `--source server` is used, the CLI downloads an official PLATEAU CityGML ZIP through the default `search.ckan.jp` catalog flow unless `--server-url` overrides it.

This path uses official ResoniteLink asset import messages:

- `ImportMesh(ImportMeshRawData)`
- `ImportTexture(ImportTexture2DFile)`

The command writes a JSON artifact locally and then builds live Resonite slots and components that reference the imported asset URLs.

## Done Signal

Treat the run as done only when all of the following are true:

- the CLI exits successfully
- Resonite shows visible geometry for the requested mesh code
- material is visibly applied
- the imported objects have the expected collider behavior

If the visual result is wrong, debug the current import contract and live adapter. Do not reintroduce transport-level probe tooling into this repository.
