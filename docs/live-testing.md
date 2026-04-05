# Live Testing

## Scope

Use this workflow only for machine-level checks against a running ResoniteLink listener.

## Constraints

- The ResoniteLink port is session-specific. Do not hard-code it in source control.
- In this environment, WSL cannot reach the Windows-side listener directly.
- Run the live import from Windows and target `localhost`.

## WSL Notes

- `ws://localhost:<port>/` from a WSL process points at WSL itself, not the Windows host.
- A successful Windows-side live test therefore needs a Windows `dotnet` process, even if editing and validation happen from WSL.
- From WSL, prefer launching the actual send through `cmd.exe` or PowerShell after changing into the repository on the Windows filesystem.

Example from WSL:

```bash
cmd.exe /c "cd /d C:\path\to\repo && dotnet.exe run --project src\Plateau.ResoniteLink.Cli\Plateau.ResoniteLink.Cli.csproj -- build --dataset <dataset> --mesh-code <mesh-code> --source local --input <windows-dataset-root> --resonitelink-port <port>"
```

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

For long-running sends, prefer a small Windows PowerShell wrapper over an inline `cmd.exe /c dotnet ...` command. In this environment, a wrapper that uses `Start-Process -Wait -PassThru` plus redirected stdout/stderr is easier to observe and less likely to get stuck on WSL interop details.

Minimal pattern:

```powershell
$process = Start-Process `
  -FilePath 'C:\Program Files\dotnet\dotnet.exe' `
  -ArgumentList @(
    'C:\path\to\Plateau.ResoniteLink.Cli.dll',
    'build',
    '--dataset', '<dataset>',
    '--mesh-code', '<mesh-code>',
    '--source', 'local',
    '--input', 'C:\path\to\dataset-root',
    '--resonitelink-port', '<port>'
  ) `
  -Wait `
  -PassThru `
  -RedirectStandardOutput 'send.stdout.log' `
  -RedirectStandardError 'send.stderr.log'

$process.ExitCode
```

Practical notes:

- Keep the actual send on Windows and keep the dataset path in Windows form such as `C:\...`.
- For heavy mesh codes, JSON artifact generation can finish long before the live send completes.
- Some long sends do not flush meaningful stdout until process exit, so treat the redirected logs and final exit code as the primary signal.

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
