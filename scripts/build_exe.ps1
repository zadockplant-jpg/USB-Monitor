. "$PSScriptRoot\common.ps1"
$root = Get-RepoRoot
$logRoot = Join-Path (Get-DesktopLogRoot) 'build_logs'
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
$script:LogFile = Join-Path $logRoot ("build_" + (Get-Date -Format 'yyyyMMdd_HHmmss') + ".txt")
Write-LogLine 'USB Point Monitor build started.'
Write-LogLine "Root: $root"

$src = Join-Path $root 'app\USBPointMonitor.cs'
$manifest = Join-Path $root 'app\USBPointMonitor.exe.manifest'
$dist = Join-Path $root 'dist'
$out = Join-Path $dist 'USBPointMonitor.exe'
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$cscCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$csc = $cscCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $csc) {
    try { $csc = (where.exe csc.exe 2>$null | Select-Object -First 1) } catch {}
}
if (-not $csc) {
    Write-LogLine 'ERROR: csc.exe not found. This needs .NET Framework compiler or Visual Studio Build Tools.'
    exit 1
}
Write-LogLine "Compiler: $csc"
Write-LogLine "Source: $src"
Write-LogLine "Output: $out"

$args = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/r:System.dll',
    '/r:System.Windows.Forms.dll',
    '/r:System.Drawing.dll',
    ("/win32manifest:" + $manifest),
    ("/out:" + $out),
    $src
)
$r = Invoke-LoggedProcess -FilePath $csc -ArgumentList $args -TimeoutSeconds 120
Write-LogLine "csc exit=$($r.ExitCode) timeout=$($r.TimedOut)"
foreach ($line in (($r.StdOut + "`n" + $r.StdErr) -split "`r?`n")) { if ($line.Trim()) { Write-LogLine "  $line" } }
if ($r.ExitCode -eq 0 -and (Test-Path -LiteralPath $out)) {
    Write-LogLine "Build OK: $out"
    try { Set-Clipboard -Value (Get-Content -LiteralPath $script:LogFile -Raw) } catch {}
    exit 0
} else {
    Write-LogLine 'Build failed.'
    try { Set-Clipboard -Value (Get-Content -LiteralPath $script:LogFile -Raw) } catch {}
    exit 1
}
