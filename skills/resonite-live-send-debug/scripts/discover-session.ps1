param(
    [int]$ListenPort = 12512,
    [int]$TimeoutSeconds = 20,
    [int]$MaxAnnouncements = 5
)

$ErrorActionPreference = 'Stop'

$udp = [System.Net.Sockets.UdpClient]::new([System.Net.Sockets.AddressFamily]::InterNetwork)
$udp.Client.SetSocketOption([System.Net.Sockets.SocketOptionLevel]::Socket, [System.Net.Sockets.SocketOptionName]::ReuseAddress, $true)
$udp.Client.Bind([System.Net.IPEndPoint]::new([System.Net.IPAddress]::Any, $ListenPort))
$udp.Client.ReceiveTimeout = [Math]::Max(1000, $TimeoutSeconds * 1000)

$announcements = New-Object System.Collections.Generic.List[object]
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)

try {
    while ((Get-Date) -lt $deadline -and $announcements.Count -lt $MaxAnnouncements) {
        try {
            $remote = [System.Net.IPEndPoint]::new([System.Net.IPAddress]::Any, 0)
            $bytes = $udp.Receive([ref]$remote)
            $payload = [System.Text.Encoding]::UTF8.GetString($bytes)
            $json = $payload | ConvertFrom-Json

            $announcements.Add([pscustomobject]@{
                SessionName = $json.sessionName
                SessionId   = $json.sessionID
                LinkPort    = $json.linkPort
                RemoteIp    = $remote.Address.ToString()
                ReceivedAt  = Get-Date
            })
        }
        catch {
            if ((Get-Date) -ge $deadline) {
                break
            }
        }
    }
}
finally {
    $udp.Close()
}

if ($announcements.Count -eq 0) {
    throw "No ResoniteLink announcements were captured on UDP ${ListenPort} within ${TimeoutSeconds}s."
}

$announcements
