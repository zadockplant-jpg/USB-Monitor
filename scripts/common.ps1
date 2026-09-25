Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Continue'
$script:LogFile = $null

function Get-RepoRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}

function Test-IsAdmin {
    try {
        $id = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object Security.Principal.WindowsPrincipal($id)
        return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    } catch { return $false }
}

function Get-DesktopLogRoot {
    try { $desktop = [Environment]::GetFolderPath('DesktopDirectory') } catch { $desktop = $null }
    if ([string]::IsNullOrWhiteSpace($desktop)) { $desktop = Join-Path $env:USERPROFILE 'Desktop' }
    $root = Join-Path $desktop 'usb_point_monitor_logs'
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    return $root
}

function Write-LogLine {
    param([string]$Message)
    $line = '[{0}] {1}' -f (Get-Date -Format 'HH:mm:ss.fff'), $Message
    Write-Host $line
    if ($script:LogFile) { Add-Content -LiteralPath $script:LogFile -Value $line -Encoding UTF8 }
}

function Find-TShark {
    $candidates = @(
        $env:USBPM_TSHARK,
        'C:\Program Files\Wireshark\tshark.exe',
        'C:\Program Files (x86)\Wireshark\tshark.exe',
        (Join-Path $env:LOCALAPPDATA 'Programs\Wireshark\tshark.exe')
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    foreach ($c in $candidates) { if (Test-Path -LiteralPath $c) { return (Resolve-Path -LiteralPath $c).Path } }
    try {
        $w = (where.exe tshark.exe 2>$null | Select-Object -First 1)
        if ($w -and (Test-Path -LiteralPath $w)) { return $w }
    } catch {}
    return $null
}

function Find-USBPcapCMD {
    $candidates = @(
        $env:USBPM_USBPCAPCMD,
        'C:\Program Files\USBPcap\USBPcapCMD.exe',
        'C:\Program Files (x86)\USBPcap\USBPcapCMD.exe'
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    foreach ($c in $candidates) { if (Test-Path -LiteralPath $c) { return (Resolve-Path -LiteralPath $c).Path } }
    foreach ($d in Get-WiresharkExtcapDirs) {
        $p = Join-Path $d 'USBPcapCMD.exe'
        if (Test-Path -LiteralPath $p) { return (Resolve-Path -LiteralPath $p).Path }
    }
    try {
        $w = (where.exe USBPcapCMD.exe 2>$null | Select-Object -First 1)
        if ($w -and (Test-Path -LiteralPath $w)) { return $w }
    } catch {}
    return $null
}

function Get-WiresharkExtcapDirs {
    $dirs = @(
        'C:\Program Files\Wireshark\extcap',
        'C:\Program Files\Wireshark\extcap\wireshark',
        'C:\Program Files (x86)\Wireshark\extcap',
        'C:\Program Files (x86)\Wireshark\extcap\wireshark',
        (Join-Path $env:APPDATA 'Wireshark\extcap'),
        (Join-Path $env:APPDATA 'Wireshark\extcap\wireshark')
    )
    return $dirs | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
}

function Quote-ProcessArg {
    param([string]$Arg)
    if ($null -eq $Arg) { return '""' }
    if ($Arg -notmatch '[\s"]') { return $Arg }
    return '"' + ($Arg -replace '"','\"') + '"'
}

function Join-ProcessArgs {
    # Not named $Args: PowerShell replaces a parameter with that name by the automatic $args
    # (the unbound arguments, empty here), so every command used to run with no arguments.
    param([string[]]$ArgumentList)
    return (($ArgumentList | ForEach-Object { Quote-ProcessArg $_ }) -join ' ')
}

function Invoke-LoggedProcess {
    param(
        [Parameter(Mandatory=$true)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [int]$TimeoutSeconds = 120
    )
    $result = [ordered]@{ ExitCode = $null; StdOut = ''; StdErr = ''; TimedOut = $false }
    try {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $FilePath
        $psi.Arguments = Join-ProcessArgs $ArgumentList
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $p = New-Object System.Diagnostics.Process
        $p.StartInfo = $psi
        [void]$p.Start()
        if (-not $p.WaitForExit($TimeoutSeconds * 1000)) {
            $result.TimedOut = $true
            try { $p.Kill() } catch {}
        }
        $result.StdOut = $p.StandardOutput.ReadToEnd()
        $result.StdErr = $p.StandardError.ReadToEnd()
        try { $result.ExitCode = $p.ExitCode } catch { $result.ExitCode = -999 }
    } catch {
        $result.ExitCode = -998
        $result.StdErr = $_.Exception.Message
    }
    return [pscustomobject]$result
}

function Get-ExtcapUSBPcapCopies {
    $copies = @()
    foreach ($d in Get-WiresharkExtcapDirs) {
        $p = Join-Path $d 'USBPcapCMD.exe'
        if (Test-Path -LiteralPath $p) {
            $item = Get-Item -LiteralPath $p -ErrorAction SilentlyContinue
            if ($item) { $copies += $item }
        }
    }
    return @($copies)
}

function Repair-USBPcapExtcapDedupe {
    param([switch]$KeepPersonalCopy)
    $master = Find-USBPcapCMD
    if (-not $master) {
        Write-LogLine 'Extcap dedupe skipped: USBPcapCMD.exe not found.'
        return
    }
    # Prefer the real installed master, not an extcap copy, when copying.
    $preferredMaster = @('C:\Program Files\USBPcap\USBPcapCMD.exe','C:\Program Files (x86)\USBPcap\USBPcapCMD.exe') | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if ($preferredMaster) {
        $master = $preferredMaster
    } else {
        # If the only copy we found is itself an extcap copy, preserve a temp copy before moving duplicates.
        try {
            $tmpMaster = Join-Path $env:TEMP ('USBPcapCMD_master_' + (Get-Date -Format 'yyyyMMdd_HHmmss') + '.exe')
            Copy-Item -LiteralPath $master -Destination $tmpMaster -Force
            $master = $tmpMaster
            Write-LogLine "Extcap dedupe: using temporary master copy $master"
        } catch {
            Write-LogLine "Extcap dedupe: could not preserve temp master: $($_.Exception.Message)"
        }
    }

    $personal = Join-Path $env:APPDATA 'Wireshark\extcap'
    New-Item -ItemType Directory -Force -Path $personal | Out-Null
    $keep = Join-Path $personal 'USBPcapCMD.exe'

    Write-LogLine "Extcap dedupe: master USBPcapCMD=$master"
    Write-LogLine "Extcap dedupe: keeping one personal copy at $keep"

    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    foreach ($d in Get-WiresharkExtcapDirs) {
        $p = Join-Path $d 'USBPcapCMD.exe'
        if ((Test-Path -LiteralPath $p) -and ($p -ne $keep)) {
            try {
                $bak = Join-Path $d ("USBPcapCMD.exe.disabled_by_usbpm_$stamp")
                Move-Item -LiteralPath $p -Destination $bak -Force
                Write-LogLine "Moved duplicate extcap copy: $p -> $bak"
            } catch {
                Write-LogLine "Could not move duplicate extcap copy $p : $($_.Exception.Message)"
            }
        }
    }
    try {
        Copy-Item -LiteralPath $master -Destination $keep -Force
        Write-LogLine "Copied fresh personal extcap copy: $keep"
    } catch {
        Write-LogLine "Could not copy personal extcap copy: $($_.Exception.Message)"
    }
    $copies = @(Get-ExtcapUSBPcapCopies)
    Write-LogLine "Extcap USBPcapCMD active copy count after repair: $($copies.Count)"
    foreach ($c in $copies) { Write-LogLine "  active: $($c.FullName) size=$($c.Length)" }
}
