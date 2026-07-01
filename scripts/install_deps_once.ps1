. "$PSScriptRoot\common.ps1"
$root = Get-RepoRoot
$logRoot = Join-Path (Get-DesktopLogRoot) 'installer_logs'
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
$script:LogFile = Join-Path $logRoot ("install_" + (Get-Date -Format 'yyyyMMdd_HHmmss') + ".txt")

Write-LogLine 'USB Point Monitor dependency installer/repair started.'
Write-LogLine "Script root: $root"
Write-LogLine "Admin: $(Test-IsAdmin)"
Write-LogLine "Log file: $script:LogFile"

if (-not (Test-IsAdmin)) {
    Write-LogLine 'ERROR: This installer must run as Administrator.'
    exit 1
}

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$downloads = Join-Path $env:TEMP 'usb_point_monitor_deps'
New-Item -ItemType Directory -Force -Path $downloads | Out-Null

$tshark = Find-TShark
if ($tshark) {
    Write-LogLine "Wireshark/TShark already found: $tshark"
} else {
    $wiresharkUrl = 'https://www.wireshark.org/download/win64/Wireshark-latest-x64.exe'
    $wiresharkExe = Join-Path $downloads 'Wireshark-latest-x64.exe'
    Write-LogLine "Wireshark/TShark missing. Downloading official latest x64 installer: $wiresharkUrl"
    try {
        Invoke-WebRequest -Uri $wiresharkUrl -OutFile $wiresharkExe -UseBasicParsing
        Write-LogLine "Downloaded Wireshark installer: $wiresharkExe"
        Write-LogLine 'Running Wireshark silent installer with /S ...'
        $p = Start-Process -FilePath $wiresharkExe -ArgumentList '/S' -PassThru -Wait
        Write-LogLine "Wireshark installer exit code: $($p.ExitCode)"
    } catch {
        Write-LogLine "ERROR downloading/installing Wireshark: $($_.Exception.Message)"
    }
}

$usbpcap = Find-USBPcapCMD
if ($usbpcap) {
    Write-LogLine "USBPcapCMD already found: $usbpcap"
} else {
    $usbpcapUrl = 'https://github.com/desowin/usbpcap/releases/download/1.5.4.0/USBPcapSetup-1.5.4.0.exe'
    $usbpcapExe = Join-Path $downloads 'USBPcapSetup-1.5.4.0.exe'
    Write-LogLine "USBPcap missing. Downloading official USBPcap installer: $usbpcapUrl"
    try {
        Invoke-WebRequest -Uri $usbpcapUrl -OutFile $usbpcapExe -UseBasicParsing
        Write-LogLine "Downloaded USBPcap installer: $usbpcapExe"
        Write-LogLine 'Running USBPcap installer silently if supported (/SILENT /NORESTART). If a wizard opens, finish it manually.'
        $p = Start-Process -FilePath $usbpcapExe -ArgumentList '/SILENT','/NORESTART' -PassThru -Wait
        Write-LogLine "USBPcap installer exit code: $($p.ExitCode)"
    } catch {
        Write-LogLine "ERROR downloading/installing USBPcap: $($_.Exception.Message)"
    }
}

Write-LogLine 'Repairing Wireshark USBPcap extcap duplicate placement...'
Repair-USBPcapExtcapDedupe

$tshark = Find-TShark
$usbpcap = Find-USBPcapCMD
Write-LogLine "Final TShark: $tshark"
Write-LogLine "Final USBPcapCMD: $usbpcap"

if ($usbpcap) {
    Write-LogLine 'USBPcapCMD help/version probe:'
    $h = Invoke-LoggedProcess -FilePath $usbpcap -ArgumentList @('-h') -TimeoutSeconds 15
    Write-LogLine "USBPcapCMD -h exit code: $($h.ExitCode) timeout=$($h.TimedOut)"
    foreach ($line in (($h.StdOut + "`n" + $h.StdErr) -split "`r?`n")) { if ($line.Trim()) { Write-LogLine "  $line" } }
}

if ($tshark) {
    Write-LogLine 'TShark version probe only; direct capture no longer depends on tshark -D.'
    $v = Invoke-LoggedProcess -FilePath $tshark -ArgumentList @('-v') -TimeoutSeconds 30
    Write-LogLine "tshark -v exit code: $($v.ExitCode) timeout=$($v.TimedOut)"
    foreach ($line in (($v.StdOut + "`n" + $v.StdErr) -split "`r?`n") | Select-Object -First 20) { if ($line.Trim()) { Write-LogLine "  $line" } }
}

Write-LogLine 'Install/repair complete. If USBPcap was newly installed, reboot Windows before capture.'
try { Set-Clipboard -Value (Get-Content -LiteralPath $script:LogFile -Raw) } catch {}
exit 0
