# Live Testing

## Scope

Use this workflow only for machine-level checks against a running ResoniteLink listener.

Warning: cleanup during this workflow can destroy live-world results in the current Resonite session. Use a disposable experiment session, or get explicit approval before removing a dataset root from a session the user may want to preserve.

## Dry-Run First

Before any live send, run the same import once with `--dry-run`.

- `--dry-run` exercises dataset resolution, archive/local source checks, construction-source preparation, and city-object streaming without connecting to Resonite.
- `--dry-run` is the required preflight for this repository when the real Resonite session must stay untouched.
- `--dry-run` intentionally rejects `--resonitelink-port`, `--resonitelink-url`, `--resonitelink-connections`, and `--send-metrics`. It validates the import pipeline before transport, not the live socket path.

Example:

```powershell
dotnet run --project src/Plateau.ResoniteLink.Cli -- `
  build `
  --dataset <dataset> `
  --mesh-code <mesh-code> `
  --source local `
  --local-source-path <dataset-root> `
  --dry-run
```

Treat a green dry run plus the repository CI checks as the gate before requesting approval for a real-session send.

## Live Contract Rules

- Treat ResoniteLink component payloads as a strict transport contract, not an open-ended property bag.
- Do not send members that are not defined on the target Resonite component type. Keep local cache metadata such as source fingerprints outside live component members.
- When a component uses nullable enum fields on the Resonite side, write them with the matching ResoniteLink field type instead of assuming a plain enum field is interchangeable.
- Before spending time on a machine-level run, add or update a local test seam that can fail on:
  - read-after-write lag (`AddComponent` succeeds but a following `GetComponent` still returns `null`)
  - unexpected component members
  - field-type mismatches on live-written members

These checks are cheaper and more deterministic than reading Resonite runtime logs after the fact. Use Resonite-side logs only after the local contract and test seams have been exhausted.

## Constraints

- The ResoniteLink port is session-specific. Do not hard-code it in source control.
- In this environment, WSL cannot reach the Windows-side listener directly.
- Run the live import from Windows and target `localhost`.
- This repository's CLI does not discover the listener port by itself. Resolve the active listener port first and then pass it explicitly to the CLI.

## Port Discovery

Do not infer the ResoniteLink port from an arbitrary `Renderite.Host.exe` listener. Use the same source that the Unity SDK uses:

- ResoniteLink announces active sessions over UDP on port `12512`.
- The announcement payload contains `sessionName`, `sessionID`, and `linkPort`.
- The Unity SDK `AutoDiscovery` UI is just a thin wrapper over `ResoniteLink.LinkSessionListener`, which listens on UDP `12512` and reads `linkPort` from each discovered session.

Operator workflow without Unity:

1. Run Resonite and open or join the target world.
2. Open `Session` on the Resonite dash and click `Enable Resonite Link`.
3. Read the in-game text `ResoniteLink running on port: <port number>` on the Session settings page if it is visible.
4. If the UI is not enough or multiple sessions are active, listen for UDP announcements on port `12512` and read `linkPort` from the received JSON.
5. Match the discovered `sessionName` / `sessionID` to the world you intend to target.
6. Pass that port to this repository's CLI with `--resonitelink-port`, or pass a full endpoint with `--resonitelink-url`.

Minimal Windows PowerShell pattern for announcement-based discovery:

```powershell
$udp = [System.Net.Sockets.UdpClient]::new(12512)
$udp.Client.ReceiveTimeout = 20000

try {
  $remote = [System.Net.IPEndPoint]::new([System.Net.IPAddress]::Any, 0)
  $bytes = $udp.Receive([ref]$remote)
  $json = [System.Text.Encoding]::UTF8.GetString($bytes)
  $json | ConvertFrom-Json
}
finally {
  $udp.Close()
}
```

The returned object should contain `sessionName`, `sessionID`, and `linkPort`.

Practical notes for discovery:

- Do not assume a single short wait is enough. In practice, give the listener at least a 20 second receive window before treating discovery as failed.
- If you still do not receive an announcement, keep `Enable Resonite Link` on and retry the same listener check before assuming the port is unavailable.
- When multiple announcements are expected, capture more than one packet and choose the matching `sessionName` / `sessionID` instead of trusting the first packet blindly.

Failure handling:

- If a live send fails with `SocketException (10061)` or another connection-refused error, assume the listener is not active on that port until proven otherwise.
- Re-check `Enable Resonite Link`, then re-confirm the port from the in-game Session UI or a fresh UDP `12512` announcement with the longer receive window above.

## WSL Notes

- `ws://localhost:<port>/` from a WSL process points at WSL itself, not the Windows host.
- A successful Windows-side live test therefore needs a Windows `dotnet` process, even if editing and validation happen from WSL.
- From WSL, prefer launching the actual send through `cmd.exe` or PowerShell after changing into the repository on the Windows filesystem.

Example from WSL:

```bash
cmd.exe /c "cd /d C:\path\to\repo && dotnet.exe run --project src\Plateau.ResoniteLink.Cli\Plateau.ResoniteLink.Cli.csproj -- build --dataset <dataset> --mesh-code <mesh-code> --source local --local-source-path <windows-dataset-root> --resonitelink-port <port>"
```

## Windows Run

Run the actual CLI import from Windows PowerShell or Command Prompt:

```powershell
dotnet run --project src/Plateau.ResoniteLink.Cli -- `
  build `
  --dataset <dataset> `
  --mesh-code <mesh-code> `
  --source local `
  --local-source-path <dataset-root> `
  --resonitelink-port <port>
```

For remote mode, swap the source-specific arguments:

```powershell
dotnet run --project src/Plateau.ResoniteLink.Cli -- `
  build `
  --dataset <dataset> `
  --mesh-code <mesh-code> `
  --source remote `
  --server-url <direct-citygml-zip-or-7z-url> `
  --resonitelink-port <port>
```

`--local-source-path <dataset-root>` follows the Unity SDK `LocalSourcePath` naming and may point at a dataset directory, a ZIP/7z archive, or an ancestor directory that contains one nested dataset root with `udx/`.
`--server-url` must point directly to an official PLATEAU CityGML ZIP/7z archive. The CLI does not perform dataset search. The downloaded archive is cached under `runtime/<os>/resonite/cache/remote/<dataset>/<archive-hash>/`, and the cache is reused across mesh-code changes as long as the archive URL stays the same. The cached data can later be reused through `--source local --local-source-path ...`.

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
    '--local-source-path', 'C:\path\to\dataset-root',
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
- Some long sends do not flush meaningful stdout until process exit, so treat the redirected logs and final exit code as the primary signal.

This path uses official ResoniteLink asset import messages:

- `ImportMesh(ImportMeshRawData)`
- `ImportTexture(ImportTexture2DFile)`

The command sends mesh and texture assets directly through ResoniteLink and then builds live Resonite slots and components that reference the imported asset URLs.

## Done Signal

Treat the run as done only when all of the following are true:

- the CLI exits successfully
- Resonite shows visible geometry for the requested mesh code
- material is visibly applied
- the imported objects have the expected collider behavior

If the visual result is wrong, debug the current import contract and live adapter. Do not reintroduce transport-level probe tooling into this repository.
