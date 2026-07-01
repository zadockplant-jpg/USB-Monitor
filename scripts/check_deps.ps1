. "$PSScriptRoot\common.ps1"
$root = Get-RepoRoot
$logRoot = Join-Path (Get-DesktopLogRoot) 'check_logs'
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
$script:LogFile = Join-Path $logRoot ("check_" + (Get-Date -Format 'yyyyMMdd_HHmmss') + ".txt")

Write-LogLine 'USB Point Monitor dependency check started.'
Write-LogLine "Script root: $root"
Write-LogLine "Admin: $(Test-IsAdmin)"
Write-LogLine "PowerShell: $($PSVersionTable.PSVersion)"
Write-LogLine "OS: $([Environment]::OSVersion.VersionString)"
Write-LogLine "Log file: $script:LogFile"

$tshark = Find-TShark
$usbpcap = Find-USBPcapCMD
Write-LogLine "TShark path: $tshark"
Write-LogLine "USBPcapCMD path: $usbpcap"

Write-LogLine 'Wireshark extcap dirs and active USBPcapCMD copies:'
$active = @(Get-ExtcapUSBPcapCopies)
foreach ($dir in Get-WiresharkExtcapDirs) {
    Write-LogLine "  DIR $dir"
    if (Test-Path -LiteralPath $dir) {
        $items = @(Get-ChildItem -LiteralPath $dir -Filter 'USBPcapCMD*' -ErrorAction SilentlyContinue)
        if ($items.Count -eq 0) { Write-LogLine '    no USBPcapCMD* files' }
        foreach ($item in $items) { Write-LogLine "    $($item.Name) size=$($item.Length) modified=$($item.LastWriteTime)" }
    } else {
        Write-LogLine '    missing'
    }
}
Write-LogLine "Active extcap USBPcapCMD.exe copy count: $($active.Count)"
if ($active.Count -gt 1) { Write-LogLine 'WARNING: more than one active extcap copy exists; this can cause Wireshark/tshark -D duplicate plugin errors.' }

if ($usbpcap) {
    Write-LogLine 'USBPcapCMD help/version probe:'
    $h = Invoke-LoggedProcess -FilePath $usbpcap -ArgumentList @('-h') -TimeoutSeconds 15
    Write-LogLine "USBPcapCMD -h exit=$($h.ExitCode) timeout=$($h.TimedOut)"
    foreach ($line in (($h.StdOut + "`n" + $h.StdErr) -split "`r?`n")) { if ($line.Trim()) { Write-LogLine "  $line" } }
} else {
    Write-LogLine 'ERROR: USBPcapCMD.exe not found. Run 1_INSTALL_DEPS_ONCE.cmd.'
}

if ($tshark) {
    Write-LogLine 'TShark version:'
    $v = Invoke-LoggedProcess -FilePath $tshark -ArgumentList @('-v') -TimeoutSeconds 30
    Write-LogLine "tshark -v exit=$($v.ExitCode) timeout=$($v.TimedOut)"
    foreach ($line in (($v.StdOut + "`n" + $v.StdErr) -split "`r?`n") | Select-Object -First 30) { if ($line.Trim()) { Write-LogLine "  $line" } }

    Write-LogLine 'TShark interfaces diagnostic only; monitor does NOT use this for capture:'
    $d = Invoke-LoggedProcess -FilePath $tshark -ArgumentList @('-D') -TimeoutSeconds 30
    Write-LogLine "tshark -D exit=$($d.ExitCode) timeout=$($d.TimedOut)"
    $allLines = @(($d.StdOut + "`n" + $d.StdErr) -split "`r?`n")
    foreach ($line in $allLines) { if ($line.Trim()) { Write-LogLine "  $line" } }
    $usbLines = @($allLines | Where-Object { $_ -match 'USBPcap' })
    $etwLines = @($allLines | Where-Object { $_ -match 'etwdump|Event Tracing' })
    Write-LogLine "USBPcap line count in tshark -D diagnostic: $($usbLines.Count)"
    Write-LogLine "ETW/etwdump line count in tshark -D diagnostic: $($etwLines.Count)"
} else {
    Write-LogLine 'WARNING: tshark.exe not found. Capture can run, but analysis requires Wireshark/TShark.'
}

Write-LogLine 'Present USB/HID/Keyboard PnP devices:'
try {
    Get-PnpDevice -PresentOnly -ErrorAction Stop | Where-Object { $_.Class -in @('USB','HIDClass','Keyboard') -or $_.FriendlyName -match 'USB|HID|Keyboard|Receiver|Dongle' } | Sort-Object Class,FriendlyName | ForEach-Object {
        Write-LogLine ("  Class={0} Status={1} Name={2} InstanceId={3}" -f $_.Class,$_.Status,$_.FriendlyName,$_.InstanceId)
    }
} catch {
    Write-LogLine "Get-PnpDevice failed: $($_.Exception.Message)"
}

Write-LogLine 'Check complete. Log copied to clipboard if clipboard access is available.'
try { Set-Clipboard -Value (Get-Content -LiteralPath $script:LogFile -Raw) } catch { Write-LogLine "Clipboard copy failed: $($_.Exception.Message)" }
exit 0
