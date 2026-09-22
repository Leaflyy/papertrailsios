param([ValidateSet('Visual','LAN','Relay','Partner')][string]$Mode = 'Visual')
$ErrorActionPreference = 'Stop'
$paperRoot = Split-Path -Parent $PSScriptRoot
$paperExe = Join-Path $paperRoot 'Builds\Windows\PaperTrails.exe'

function Assert-Log([string]$path, [string]$marker) {
    $content = Get-Content -LiteralPath $path -Raw
    if ($content -notmatch $marker) { throw "Missing success marker in $path" }
    if ($content -match '(?m)^(NullReferenceException|ArgumentException|InvalidOperationException|IndexOutOfRangeException|Exception:)') { throw "Runtime exception in $path" }
}

if ($Mode -eq 'Visual') {
    Add-Type -AssemblyName System.Drawing
    foreach ($size in @(@(1280,800),@(430,900),@(932,430))) {
        $label = "$($size[0])x$($size[1])"
        $log = Join-Path $paperRoot "Builds\visual-$label.log"
        $process = Start-Process -FilePath $paperExe -WorkingDirectory $paperRoot -ArgumentList '-paperSmoke','-force-d3d11','-screen-width',$size[0],'-screen-height',$size[1],'-logFile',$log -PassThru
        if (-not $process.WaitForExit(60000) -and -not $process.HasExited) { Stop-Process -Id $process.Id -ErrorAction SilentlyContinue; throw "Visual run timed out: $label" }
        Assert-Log $log 'PAPERTRAILS_SMOKE_OK'
        foreach ($view in @('match','trail','Menu','Lobby','Collection','Roulette','Results')) {
            $path = Join-Path $paperRoot "Builds\Screenshots\$label\$view.png"
            $bitmap = [System.Drawing.Bitmap]::new($path)
            try {
                $colors = [System.Collections.Generic.HashSet[int]]::new()
                for ($y=0;$y -lt $bitmap.Height;$y+=17) {
                    for ($x=0;$x -lt $bitmap.Width;$x+=17) { [void]$colors.Add($bitmap.GetPixel($x,$y).ToArgb()) }
                }
                if ($colors.Count -lt 10) { throw "Blank or invalid frame: $path" }
                Write-Output "PASS $label $view ($($colors.Count) sampled colors)"
            } finally { $bitmap.Dispose() }
        }
    }
} else {
    $hostLog = Join-Path $paperRoot "Builds\$Mode-host.log"
    $clientLog = Join-Path $paperRoot "Builds\$Mode-client.log"
    $hostFlag = if ($Mode -eq 'Relay') { '-paperRelayHostSmoke' } elseif ($Mode -eq 'Partner') { '-paperPartnerHostSmoke' } else { '-paperHostSmoke' }
    $clientFlag = if ($Mode -eq 'Relay') { '-paperRelayClientSmoke' } elseif ($Mode -eq 'Partner') { '-paperPartnerClientSmoke' } else { '-paperClientSmoke' }
    $codePath = Join-Path $paperRoot 'Builds\relay-join-code.txt'
    if ($Mode -eq 'Relay' -and (Test-Path -LiteralPath $codePath)) { Remove-Item -LiteralPath $codePath }
    $hostProcess = Start-Process -FilePath $paperExe -WorkingDirectory $paperRoot -ArgumentList $hostFlag,'-force-d3d11','-logFile',$hostLog -WindowStyle Hidden -PassThru
    $clientProcess = $null
    try {
        $clientArguments = @($clientFlag)
        if ($Mode -eq 'Relay') {
            $deadline = [DateTime]::UtcNow.AddSeconds(25)
            while (-not (Test-Path -LiteralPath $codePath) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 250 }
            if (-not (Test-Path -LiteralPath $codePath)) { throw 'Host did not generate a Relay join code; inspect Relay-host.log' }
            $clientArguments += (Get-Content -LiteralPath $codePath -Raw).Trim()
        } elseif ($Mode -eq 'Partner') { Start-Sleep -Seconds 8 } else { Start-Sleep -Seconds 2 }
        $clientArguments += @('-force-d3d11','-logFile',$clientLog)
        $clientProcess = Start-Process -FilePath $paperExe -WorkingDirectory $paperRoot -ArgumentList $clientArguments -WindowStyle Hidden -PassThru
        $clientTimeout = if ($Mode -eq 'Partner') { 60000 } else { 40000 }
        $hostTimeout = if ($Mode -eq 'Partner') { 30000 } else { 30000 }
        if (-not $clientProcess.WaitForExit($clientTimeout)) { throw 'Guest test timed out' }
        if (-not $hostProcess.WaitForExit($hostTimeout)) { throw 'Host test timed out' }
        Assert-Log $hostLog 'PAPERTRAILS_NETWORK_OK'
        Assert-Log $clientLog 'PAPERTRAILS_NETWORK_OK'
        Write-Output "PASS $Mode host / join / votes / authoritative match replication"
    } finally {
        if ($clientProcess -and -not $clientProcess.HasExited) { Stop-Process -Id $clientProcess.Id }
        if (-not $hostProcess.HasExited) { Stop-Process -Id $hostProcess.Id }
    }
}
