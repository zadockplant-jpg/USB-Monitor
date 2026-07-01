. "$PSScriptRoot\common.ps1"
$logRoot = Join-Path (Get-DesktopLogRoot) 'repair_logs'
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
$script:LogFile = Join-Path $logRoot ("repair_extcap_" + (Get-Date -Format 'yyyyMMdd_HHmmss') + ".txt")
Write-LogLine 'USB Point Monitor extcap dedupe repair started.'
Write-LogLine "Admin: $(Test-IsAdmin)"
Repair-USBPcapExtcapDedupe
Write-LogLine 'Repair complete. Log copied to clipboard if possible.'
try { Set-Clipboard -Value (Get-Content -LiteralPath $script:LogFile -Raw) } catch {}
exit 0
