#Requires -Version 5.1
<#
.SYNOPSIS
Exercises the per-user installation scripts against their fixed destination.

.DESCRIPTION
Runs the production install.ps1, set-autostart.ps1, and uninstall.ps1 of a built package, plus failure injections
that replace module functions in a child PowerShell process. The checks write to
%LocalAppData%\Programs\SystemResourceMonitor and to the HKCU Run value 'SystemResourceMonitor', so run them in a
disposable Windows 11 user profile. They refuse to start unless the installation, its sibling work directories,
and the Run value are absent, and they remove what they created when they finish.

Settings in %LocalAppData%\SystemResourceMonitor are only read, to prove that they are retained.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackageDirectory,
    [Parameter(Mandatory = $true)][switch]$ConfirmDisposableProfile,
    [string]$ResultPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$powershell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$barGuardName = 'Local\SystemResourceMonitor-1178D3BA-F66E-486A-8896-F6EBAF20FF05'
$usersSid = 'S-1-5-32-545'
$runKeyPath = 'Software\Microsoft\Windows\CurrentVersion\Run'
$runValueName = 'SystemResourceMonitor'

$PackageDirectory = (Resolve-Path -LiteralPath $PackageDirectory).ProviderPath
if ($ResultPath) {
    $ResultPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ResultPath)
}

Import-Module (Join-Path $PackageDirectory 'SystemResourceMonitor.Install.psm1') -Force
if (-not ('InstallationChecks.WorkArea' -as [type])) {
    Add-Type -TypeDefinition @'
using System.Runtime.InteropServices;
namespace InstallationChecks
{
    public static class WorkArea
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SystemParametersInfoW(uint action, uint parameter, out Rect value, uint flags);

        public static string Get()
        {
            Rect rect;
            if (!SystemParametersInfoW(0x0030, 0, out rect, 0)) { throw new System.ComponentModel.Win32Exception(); }
            return rect.Left + "," + rect.Top + "," + rect.Right + "," + rect.Bottom;
        }
    }
}
'@
}

$localAppData = [SystemResourceMonitorInstall.Native]::GetLocalAppDataPath()
$programs = Join-Path $localAppData 'Programs'
$application = Join-Path $programs 'SystemResourceMonitor'
$executable = Join-Path $application 'SystemResourceMonitor.Desktop.exe'
$ownedRunData = '"' + $executable + '"'
$settingsDirectory = Join-Path $localAppData 'SystemResourceMonitor'
$plantedDirectory = Join-Path $programs 'SystemResourceMonitor Planted'
$results = New-Object System.Collections.Generic.List[object]
$work = Join-Path ([IO.Path]::GetTempPath()) ('SRM installation checks ' + [guid]::NewGuid().ToString('N').Substring(0, 8))

function Add-Result {
    param([string]$Name, [string]$Command, $ExitCode, [string]$Outcome, [string]$Detail = '')
    $results.Add([pscustomobject]@{ Check = $Name; Command = $Command; ExitCode = $ExitCode; Result = $Outcome; Detail = $Detail })
    $color = 'Green'
    if ($Outcome -eq 'fail') { $color = 'Red' } elseif ($Outcome -eq 'skipped') { $color = 'Yellow' }
    Write-Host ("[{0}] {1} ({2} -> {3}) {4}" -f $Outcome, $Name, $Command, $ExitCode, $Detail) -ForegroundColor $color
}

function Invoke-Child {
    # Runs a script file in a new Windows PowerShell process and returns its exit code and output.
    param([string]$ScriptPath, [string[]]$Arguments = @())
    # Child stderr arrives as error records; they are expected output, not harness failures.
    $ErrorActionPreference = 'Continue'
    $output = & $powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $ScriptPath @Arguments 2>&1
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = (($output | ForEach-Object { "$_" }) -join "`n") }
}

function Invoke-PackageScript {
    param([string]$Package, [string]$Script, [string]$Arguments = '')
    $runner = Join-Path $work ('run-' + [guid]::NewGuid().ToString('N') + '.ps1')
    $path = (Join-Path $Package $Script).Replace("'", "''")
    Set-Content -LiteralPath $runner -Encoding UTF8 -Value "& '$path' $Arguments`r`nexit `$LASTEXITCODE"
    return Invoke-Child -ScriptPath $runner
}

function Invoke-Injected {
    # Replaces module functions in the child process, then calls the same entry function the script calls.
    param([string]$Package, [string]$Injection, [string]$EntryCall)
    $runner = Join-Path $work ('inject-' + [guid]::NewGuid().ToString('N') + '.ps1')
    $module = (Join-Path $Package 'SystemResourceMonitor.Install.psm1').Replace("'", "''")
    $body = @"
`$ErrorActionPreference = 'Stop'
Import-Module '$module' -Force
& (Get-Module SystemResourceMonitor.Install) {
$Injection
}
exit ($EntryCall)
"@
    Set-Content -LiteralPath $runner -Encoding UTF8 -Value $body
    return Invoke-Child -ScriptPath $runner
}

function Get-RunValue {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runKeyPath, $false)
    if ($null -eq $key) { return $null }
    try {
        if (@($key.GetValueNames()) -notcontains $runValueName) { return $null }
        return [pscustomobject]@{
            Kind = $key.GetValueKind($runValueName).ToString()
            Data = $key.GetValue($runValueName, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        }
    }
    finally { $key.Dispose() }
}

function Set-RunValue {
    param([string]$Data, [Microsoft.Win32.RegistryValueKind]$Kind = [Microsoft.Win32.RegistryValueKind]::String)
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($runKeyPath)
    try { $key.SetValue($runValueName, $Data, $Kind) } finally { $key.Dispose() }
}

function Remove-RunValue {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runKeyPath, $true)
    if ($null -eq $key) { return }
    try { $key.DeleteValue($runValueName, $false) } finally { $key.Dispose() }
}

function Get-RunDescription {
    $value = Get-RunValue
    if ($null -eq $value) { return 'absent' }
    if ($value.Kind -eq 'String' -and $value.Data -ceq $ownedRunData) { return 'owned' }
    return "foreign:$($value.Kind):$($value.Data)"
}

function Get-TreeSnapshot {
    # Relative path and SHA-256 of every file; '<absent>' when the directory does not exist.
    param([string]$Root)
    if (-not [IO.Directory]::Exists($Root)) { return '<absent>' }
    $lines = foreach ($file in [IO.Directory]::GetFiles($Root, '*', [IO.SearchOption]::AllDirectories) | Sort-Object) {
        $file.Substring($Root.Length + 1) + '=' + (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    }
    return ($lines -join "`n")
}

function Get-ExpectedSnapshot {
    param([string]$Package)
    $manifest = [IO.File]::ReadAllText((Join-Path $Package 'install-manifest.json')) | ConvertFrom-Json
    $manifestHash = (Get-FileHash -LiteralPath (Join-Path $Package 'install-manifest.json') -Algorithm SHA256).Hash
    $lines = @($manifest.files | ForEach-Object { $_.relativePath.Replace('/', '\') + '=' + $_.sha256 })
    $lines += 'install-manifest.json=' + $manifestHash
    return (($lines | Sort-Object) -join "`n")
}

function Get-Siblings {
    if (-not [IO.Directory]::Exists($programs)) { return @() }
    return @([IO.Directory]::GetDirectories($programs, '.SystemResourceMonitor.*'))
}

function Get-InstalledBarProcesses {
    return @(Get-Process -Name 'SystemResourceMonitor.Desktop' -ErrorAction SilentlyContinue | Where-Object {
            $path = $null
            try { $path = $_.Path } catch { }
            $path -and [string]::Equals($path, $executable, [StringComparison]::OrdinalIgnoreCase)
        })
}

function Test-BarGuardHeld {
    $mutex = $null
    if ([Threading.Mutex]::TryOpenExisting($barGuardName, [ref]$mutex)) {
        $mutex.Dispose()
        return $true
    }
    return $false
}

function Remove-TestTree {
    # Deletes a test-created tree; reparse points are unlinked first so their targets are never followed.
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        [IO.Directory]::Delete($Path, $false)
        return
    }
    if ($item.PSIsContainer) {
        foreach ($child in [IO.Directory]::GetFileSystemEntries($Path)) { Remove-TestTree $child }
        [IO.Directory]::Delete($Path, $false)
    }
    else {
        [IO.File]::SetAttributes($Path, [IO.FileAttributes]::Normal)
        [IO.File]::Delete($Path)
    }
}

function New-Junction {
    param([string]$Path, [string]$Target)
    $null = New-Item -ItemType Junction -Path $Path -Value $Target
}

function Add-UsersWriteRule {
    param([string]$Path)
    $acl = Get-Acl -LiteralPath $Path
    $rule = New-Object Security.AccessControl.FileSystemAccessRule ((New-Object Security.Principal.SecurityIdentifier $usersSid), 'Write', 'Allow')
    $acl.AddAccessRule($rule)
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Remove-UsersWriteRule {
    param([string]$Path)
    $acl = Get-Acl -LiteralPath $Path
    $rule = New-Object Security.AccessControl.FileSystemAccessRule ((New-Object Security.Principal.SecurityIdentifier $usersSid), 'Write', 'Allow')
    $null = $acl.RemoveAccessRule($rule)
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Assert-Outcome {
    # Records one check: expected exit code plus a state predicate.
    param([string]$Name, [string]$Command, $Run, [int]$ExpectedExit, [scriptblock]$State, [string]$StateText)
    $stateOk = & $State
    if ($Run.ExitCode -eq $ExpectedExit -and $stateOk) {
        Add-Result $Name $Command $Run.ExitCode 'pass' $StateText
    }
    else {
        $output = $Run.Output -replace [regex]::Escape($localAppData), '%LOCALAPPDATA%'
        Add-Result $Name $Command $Run.ExitCode 'fail' ("expected exit $ExpectedExit; state ok: $stateOk; output: $output")
    }
}

function Wait-BarWindow {
    param([System.Diagnostics.Process]$Process)
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($Process.HasExited) { return $false }
        if (@([SystemResourceMonitorInstall.Native]::GetVisibleTopLevelWindows([uint32]$Process.Id)).Count -eq 1) {
            Start-Sleep -Milliseconds 1000
            return $true
        }
        Start-Sleep -Milliseconds 200
    }
    return $false
}

if (-not $ConfirmDisposableProfile) {
    throw 'Pass -ConfirmDisposableProfile to acknowledge that the checks write to the fixed per-user installation location.'
}

$programsExisted = [IO.Directory]::Exists($programs)
if ([IO.Directory]::Exists($application) -or @(Get-Siblings).Count -gt 0 -or $null -ne (Get-RunValue) -or (Test-Path -LiteralPath $plantedDirectory)) {
    throw 'The installation, a sibling work directory, the planted test directory, or the Run value already exists. Run the checks in a disposable profile without an installation.'
}

$settingsBefore = Get-TreeSnapshot $settingsDirectory
$packageA = Join-Path $work 'package A'
$packageB = Join-Path $work 'package B'
$decoy = Join-Path $work 'decoy target'
$moved = Join-Path $work 'moved installation'
$null = New-Item -ItemType Directory -Path $work

try {
    # Two candidates from one package: B differs only in its copy of the user guide.
    Copy-Item -LiteralPath $PackageDirectory -Destination $packageA -Recurse
    Copy-Item -LiteralPath $PackageDirectory -Destination $packageB -Recurse
    Add-Content -LiteralPath (Join-Path $packageB 'app\README.md') -Value 'Upgrade candidate for installation checks.'
    $commit = ([IO.File]::ReadAllText((Join-Path $packageA 'install-manifest.json')) | ConvertFrom-Json).sourceCommit
    Remove-Item -LiteralPath (Join-Path $packageB 'install-manifest.json')
    New-SrmCandidateManifest -PayloadDirectory (Join-Path $packageB 'app') -SourceCommit $commit -OutputPath (Join-Path $packageB 'install-manifest.json')
    $expectedA = Get-ExpectedSnapshot $packageA
    $expectedB = Get-ExpectedSnapshot $packageB
    $null = New-Item -ItemType Directory -Path $decoy
    Set-Content -LiteralPath (Join-Path $decoy 'sentinel.txt') -Value 'decoy'
    $decoySnapshot = Get-TreeSnapshot $decoy
    $nothingInstalled = { -not [IO.Directory]::Exists($application) -and @(Get-Siblings).Count -eq 0 -and (Get-RunDescription) -eq 'absent' }

    # Candidate manifest validation through install.ps1.
    $badManifests = [ordered]@{
        'absolute path' = 'C:/Windows/notepad.exe'
        'parent segment' = 'cs/../../escape.txt'
        'duplicate path' = 'ACCESSIBILITY.DLL'
        'drive-relative path' = 'C:escape.txt'
        'alternate data stream' = 'README.md:stream'
    }
    foreach ($case in $badManifests.GetEnumerator()) {
        $bad = Join-Path $work ('bad ' + [guid]::NewGuid().ToString('N').Substring(0, 6))
        $null = New-Item -ItemType Directory -Path $bad
        foreach ($name in @('install.ps1', 'SystemResourceMonitor.Install.psm1')) { Copy-Item -LiteralPath (Join-Path $packageA $name) -Destination $bad }
        $null = New-Item -ItemType Junction -Path (Join-Path $bad 'app') -Value (Join-Path $packageA 'app')
        $json = [IO.File]::ReadAllText((Join-Path $packageA 'install-manifest.json')) | ConvertFrom-Json
        $json.files += [pscustomobject]@{ relativePath = $case.Value; sha256 = ('0' * 64) }
        [IO.File]::WriteAllText((Join-Path $bad 'install-manifest.json'), ($json | ConvertTo-Json -Depth 4))
        $run = Invoke-PackageScript $bad 'install.ps1'
        Assert-Outcome "Reject candidate manifest: $($case.Key)" 'install.ps1 (crafted manifest)' $run 1 $nothingInstalled 'nothing installed'
        [IO.Directory]::Delete((Join-Path $bad 'app'), $false)
        Remove-TestTree $bad
    }

    Add-Content -LiteralPath (Join-Path $packageB 'app\README.md') -Value 'tampered'
    $run = Invoke-PackageScript $packageB 'install.ps1'
    Assert-Outcome 'Reject package payload that does not match its manifest' 'install.ps1 (package B, tampered)' $run 1 $nothingInstalled 'nothing installed'
    Remove-TestTree $packageB
    Copy-Item -LiteralPath $PackageDirectory -Destination $packageB -Recurse
    Add-Content -LiteralPath (Join-Path $packageB 'app\README.md') -Value 'Upgrade candidate for installation checks.'
    Remove-Item -LiteralPath (Join-Path $packageB 'install-manifest.json')
    New-SrmCandidateManifest -PayloadDirectory (Join-Path $packageB 'app') -SourceCommit $commit -OutputPath (Join-Path $packageB 'install-manifest.json')

    # Planted destination reparse points.
    New-Junction $application $decoy
    $run = Invoke-PackageScript $packageA 'install.ps1'
    Assert-Outcome 'Junction at destination: install' 'install.ps1' $run 1 { (Get-TreeSnapshot $decoy) -eq $decoySnapshot -and (Get-Item -LiteralPath $application -Force).Attributes -band [IO.FileAttributes]::ReparsePoint } 'decoy untouched, junction left'
    $run = Invoke-PackageScript $packageA 'set-autostart.ps1' '-Enabled $true'
    Assert-Outcome 'Junction at destination: enable autostart' 'set-autostart.ps1 -Enabled $true' $run 1 { (Get-RunDescription) -eq 'absent' -and (Get-TreeSnapshot $decoy) -eq $decoySnapshot } 'Run value absent'
    $run = Invoke-PackageScript $packageA 'uninstall.ps1'
    Assert-Outcome 'Junction at destination: uninstall' 'uninstall.ps1' $run 1 { (Get-TreeSnapshot $decoy) -eq $decoySnapshot } 'decoy untouched'
    [IO.Directory]::Delete($application, $false)

    $symlinkCreated = $false
    try {
        $null = New-Item -ItemType SymbolicLink -Path $application -Value $decoy
        $symlinkCreated = $true
    }
    catch { }
    if ($symlinkCreated) {
        $run = Invoke-PackageScript $packageA 'install.ps1'
        Assert-Outcome 'Directory symbolic link at destination: install' 'install.ps1' $run 1 { (Get-TreeSnapshot $decoy) -eq $decoySnapshot } 'decoy untouched'
        [IO.Directory]::Delete($application, $false)
    }
    else {
        Add-Result 'Directory symbolic link at destination' 'install.ps1' '' 'skipped' 'creating a symbolic link needs Developer Mode or elevation'
    }

    if (-not $programsExisted) {
        $realPrograms = $programs + '.checks-real'
        $null = New-Item -ItemType Directory -Path $realPrograms
        New-Junction $programs $realPrograms
        $run = Invoke-PackageScript $packageA 'install.ps1'
        Assert-Outcome 'Junction at traversed Programs component: install' 'install.ps1' $run 1 { @([IO.Directory]::GetFileSystemEntries($realPrograms)).Count -eq 0 } 'junction target untouched'
        [IO.Directory]::Delete($programs, $false)
        [IO.Directory]::Delete($realPrograms, $false)
    }
    else {
        Add-Result 'Junction at traversed Programs component' 'install.ps1' '' 'skipped' 'Programs already exists in this profile and is shared with other apps'
    }

    # Staging failures leave no active payload.
    $run = Invoke-Injected $packageA @'
$script:TestCopies = 0
function script:Copy-PayloadFile { param([string]$Source, [string]$Destination)
    $script:TestCopies++
    if ($script:TestCopies -gt 5) { throw 'Injected copy failure.' }
    [IO.File]::Copy($Source, $Destination, $false) }
'@ "Invoke-SrmInstall -PackageDirectory '$($packageA.Replace("'", "''"))'"
    Assert-Outcome 'Injected stage-copy failure' 'Invoke-SrmInstall (copy fails after 5 files)' $run 1 $nothingInstalled 'no active payload, staging removed'

    $run = Invoke-Injected $packageA @'
$script:TestCopies = 0
function script:Copy-PayloadFile { param([string]$Source, [string]$Destination)
    $script:TestCopies++
    [IO.File]::Copy($Source, $Destination, $false)
    if ($script:TestCopies -eq 3) { [IO.File]::AppendAllText($Destination, 'corrupt') } }
'@ "Invoke-SrmInstall -PackageDirectory '$($packageA.Replace("'", "''"))'"
    $leftStages = @(Get-Siblings)
    $onlyCorruptLeft = $leftStages.Count -eq 1 -and @([IO.Directory]::GetFiles($leftStages[0], '*', [IO.SearchOption]::AllDirectories)).Count -eq 2
    Assert-Outcome 'Injected staged hash mismatch' 'Invoke-SrmInstall (third copy corrupted)' $run 1 { -not [IO.Directory]::Exists($application) -and $onlyCorruptLeft } 'no active payload; only the unverified file and staged manifest remain'
    foreach ($stage in $leftStages) { Remove-TestTree $stage }

    # Replacement planted before the recheck that precedes each phase's mutation.
    $plantAtDestination = "New-Item -ItemType Junction -Path '$($application.Replace("'", "''"))' -Value '$($decoy.Replace("'", "''"))' | Out-Null"
    $assertWrapper = @'
$script:TestAsserts = 0
$script:TestOriginalAssert = ${function:Assert-InstallationLocation}
function script:Assert-InstallationLocation { param($Layout)
    $script:TestAsserts++
    if ($script:TestAsserts -eq __CALL__) { __PLANT__ }
    & $script:TestOriginalAssert $Layout }
'@
    # Clean install: calls 1-3 precede staging and verification; call 4 precedes the rename into place.
    $run = Invoke-Injected $packageA ($assertWrapper.Replace('__CALL__', '4').Replace('__PLANT__', $plantAtDestination)) "Invoke-SrmInstall -PackageDirectory '$($packageA.Replace("'", "''"))'"
    Assert-Outcome 'Planted junction before install rename' 'Invoke-SrmInstall (junction planted at recheck 4)' $run 1 { (Get-TreeSnapshot $decoy) -eq $decoySnapshot -and @(Get-Siblings).Count -eq 0 } 'decoy untouched, staging removed'
    [IO.Directory]::Delete($application, $false)

    # Clean install, repeat install, and no launch.
    $run = Invoke-PackageScript $packageA 'install.ps1'
    Start-Sleep -Seconds 2
    Assert-Outcome 'Clean install' 'install.ps1 (package A)' $run 0 { (Get-TreeSnapshot $application) -eq $expectedA -and (Get-RunDescription) -eq 'absent' -and @(Get-InstalledBarProcesses).Count -eq 0 -and @(Get-Siblings).Count -eq 0 } 'files and hashes match package A plus manifest; no Run value; bar not started'
    $manifestTime = [IO.File]::GetLastWriteTimeUtc((Join-Path $application 'install-manifest.json'))
    $run = Invoke-PackageScript $packageA 'install.ps1'
    Assert-Outcome 'Repeat install' 'install.ps1 (package A)' $run 0 { (Get-TreeSnapshot $application) -eq $expectedA -and [IO.File]::GetLastWriteTimeUtc((Join-Path $application 'install-manifest.json')) -eq $manifestTime } 'unchanged'

    # Autostart.
    $run = Invoke-PackageScript $packageA 'set-autostart.ps1' '-Enabled $true'
    Assert-Outcome 'Enable autostart' 'set-autostart.ps1 -Enabled $true' $run 0 { (Get-RunDescription) -eq 'owned' } 'REG_SZ equals the quoted canonical executable path'
    $run = Invoke-PackageScript $packageA 'set-autostart.ps1' '-Enabled $true'
    Assert-Outcome 'Repeat enable' 'set-autostart.ps1 -Enabled $true' $run 0 { (Get-RunDescription) -eq 'owned' } 'unchanged'
    $run = Invoke-PackageScript $packageA 'set-autostart.ps1' '-Enabled $false'
    Assert-Outcome 'Disable autostart' 'set-autostart.ps1 -Enabled $false' $run 0 { (Get-RunDescription) -eq 'absent' -and (Get-TreeSnapshot $application) -eq $expectedA } 'Run value removed; files kept'
    $run = Invoke-PackageScript $packageA 'set-autostart.ps1' '-Enabled $false'
    Assert-Outcome 'Repeat disable' 'set-autostart.ps1 -Enabled $false' $run 0 { (Get-RunDescription) -eq 'absent' } 'unchanged'

    # Foreign Run values, including a planted executable whose path has the installed name as a prefix.
    # A complete copy so that the planted bar can run; its folder name has the installed name as a prefix.
    Copy-Item -LiteralPath (Join-Path $packageA 'app') -Destination $plantedDirectory -Recurse
    $foreignValues = @(
        @{ Name = 'unquoted path'; Data = $executable; Kind = [Microsoft.Win32.RegistryValueKind]::String },
        @{ Name = 'path with argument'; Data = $ownedRunData + ' --x'; Kind = [Microsoft.Win32.RegistryValueKind]::String },
        @{ Name = 'planted prefix executable'; Data = '"' + (Join-Path $plantedDirectory 'SystemResourceMonitor.Desktop.exe') + '"'; Kind = [Microsoft.Win32.RegistryValueKind]::String },
        @{ Name = 'REG_EXPAND_SZ type'; Data = $ownedRunData; Kind = [Microsoft.Win32.RegistryValueKind]::ExpandString }
    )
    foreach ($foreign in $foreignValues) {
        Set-RunValue -Data $foreign.Data -Kind $foreign.Kind
        $before = Get-RunDescription
        $run = Invoke-PackageScript $packageA 'set-autostart.ps1' '-Enabled $true'
        Assert-Outcome "Foreign Run value ($($foreign.Name)): enable" 'set-autostart.ps1 -Enabled $true' $run 1 { (Get-RunDescription) -eq $before } 'value unchanged'
        $run = Invoke-PackageScript $packageA 'set-autostart.ps1' '-Enabled $false'
        Assert-Outcome "Foreign Run value ($($foreign.Name)): disable" 'set-autostart.ps1 -Enabled $false' $run 1 { (Get-RunDescription) -eq $before } 'value unchanged'
        Remove-RunValue
    }

    # Unsafe access and changed payloads stop every operation without changes.
    $run = Invoke-PackageScript $packageA 'set-autostart.ps1' '-Enabled $true'
    Assert-Outcome 'Enable autostart before negative cases' 'set-autostart.ps1 -Enabled $true' $run 0 { (Get-RunDescription) -eq 'owned' } 'owned'
    foreach ($target in @($application, (Join-Path $application 'SystemResourceMonitor.Core.dll'))) {
        $label = if ($target -eq $application) { 'application directory' } else { 'installed file' }
        Add-UsersWriteRule $target
        $unchangedOwned = { (Get-TreeSnapshot $application) -eq $expectedA -and (Get-RunDescription) -eq 'owned' }
        $run = Invoke-PackageScript $packageB 'install.ps1'
        Assert-Outcome "Users write access on $label`: upgrade" 'install.ps1 (package B)' $run 1 $unchangedOwned 'nothing changed'
        $run = Invoke-PackageScript $packageA 'set-autostart.ps1' '-Enabled $false'
        Assert-Outcome "Users write access on $label`: disable" 'set-autostart.ps1 -Enabled $false' $run 1 $unchangedOwned 'nothing changed'
        $run = Invoke-PackageScript $packageA 'uninstall.ps1'
        Assert-Outcome "Users write access on $label`: uninstall" 'uninstall.ps1' $run 1 $unchangedOwned 'nothing changed'
        Remove-UsersWriteRule $target
    }

    if (-not $programsExisted) {
        Add-UsersWriteRule $programs
        $run = Invoke-PackageScript $packageA 'uninstall.ps1'
        Assert-Outcome 'Users write access on Programs: uninstall' 'uninstall.ps1' $run 1 { (Get-TreeSnapshot $application) -eq $expectedA } 'nothing changed'
        Remove-UsersWriteRule $programs
    }
    else {
        Add-Result 'Users write access on Programs' 'uninstall.ps1' '' 'skipped' 'Programs already exists in this profile and is shared with other apps'
    }

    $ownerChanged = $false
    try {
        $acl = Get-Acl -LiteralPath $application
        $acl.SetOwner((New-Object Security.Principal.SecurityIdentifier 'S-1-5-32-544'))
        Set-Acl -LiteralPath $application -AclObject $acl
        $ownerChanged = $true
    }
    catch { }
    if ($ownerChanged) {
        $run = Invoke-PackageScript $packageA 'uninstall.ps1'
        Assert-Outcome 'Application directory owned by Administrators: uninstall' 'uninstall.ps1' $run 1 { (Get-TreeSnapshot $application) -eq $expectedA } 'nothing changed'
        $acl = Get-Acl -LiteralPath $application
        $acl.SetOwner([Security.Principal.WindowsIdentity]::GetCurrent().User)
        Set-Acl -LiteralPath $application -AclObject $acl
    }
    else {
        Add-Result 'Changed application directory owner' 'uninstall.ps1' '' 'skipped' 'a standard user cannot assign another owner'
    }

    $installedReadme = Join-Path $application 'README.md'
    $readmeBytes = [IO.File]::ReadAllBytes($installedReadme)
    Add-Content -LiteralPath $installedReadme -Value 'changed'
    $changedSnapshot = Get-TreeSnapshot $application
    $run = Invoke-PackageScript $packageB 'install.ps1'
    Assert-Outcome 'Changed installed file: upgrade' 'install.ps1 (package B)' $run 1 { (Get-TreeSnapshot $application) -eq $changedSnapshot } 'nothing changed'
    $run = Invoke-PackageScript $packageA 'set-autostart.ps1' '-Enabled $false'
    Assert-Outcome 'Changed installed file: disable' 'set-autostart.ps1 -Enabled $false' $run 1 { (Get-RunDescription) -eq 'owned' } 'Run value kept'
    [IO.File]::WriteAllBytes($installedReadme, $readmeBytes)

    $extra = Join-Path $application 'extra.txt'
    Set-Content -LiteralPath $extra -Value 'extra'
    $extraSnapshot = Get-TreeSnapshot $application
    $run = Invoke-PackageScript $packageB 'install.ps1'
    Assert-Outcome 'Unexpected installed file: upgrade' 'install.ps1 (package B)' $run 1 { (Get-TreeSnapshot $application) -eq $extraSnapshot } 'nothing changed'
    Remove-Item -LiteralPath $extra

    New-Junction (Join-Path $application 'planted') $decoy
    $run = Invoke-PackageScript $packageA 'uninstall.ps1'
    Assert-Outcome 'Junction inside the application directory: uninstall' 'uninstall.ps1' $run 1 { (Get-TreeSnapshot $decoy) -eq $decoySnapshot -and [IO.File]::Exists($executable) -and (Get-RunDescription) -eq 'owned' } 'nothing changed'
    [IO.Directory]::Delete((Join-Path $application 'planted'), $false)

    # Replacement planted before each phase's last recheck.
    $replace = "[IO.Directory]::Move('$($application.Replace("'", "''"))', '$($moved.Replace("'", "''"))'); New-Item -ItemType Junction -Path '$($application.Replace("'", "''"))' -Value '$($moved.Replace("'", "''"))' | Out-Null"
    $restore = {
        [IO.Directory]::Delete($application, $false)
        [IO.Directory]::Move($moved, $application)
    }
    $phases = @(
        @{ Name = 'upgrade swap'; Call = '4'; Entry = "Invoke-SrmInstall -PackageDirectory '$($packageB.Replace("'", "''"))'"; Run = 'owned' },
        @{ Name = 'autostart disable'; Call = '3'; Entry = 'Invoke-SrmSetAutostart -Enabled $false'; Run = 'owned' },
        @{ Name = 'uninstall Run removal'; Call = '3'; Entry = 'Invoke-SrmUninstall'; Run = 'owned' },
        @{ Name = 'uninstall file removal'; Call = '4'; Entry = 'Invoke-SrmUninstall'; Run = 'any' }
    )
    foreach ($phase in $phases) {
        if ($phase.Run -eq 'any') {
            # The Run value is removed before recheck 4; disable autostart so that this phase isolates file removal.
            $null = Invoke-PackageScript $packageA 'set-autostart.ps1' '-Enabled $false'
        }
        $run = Invoke-Injected $packageA ($assertWrapper.Replace('__CALL__', $phase.Call).Replace('__PLANT__', $replace)) $phase.Entry
        $expectedRun = if ($phase.Run -eq 'any') { 'absent' } else { 'owned' }
        Assert-Outcome "Planted replacement before $($phase.Name)" "$($phase.Entry.Split(' ')[0]) (replacement at recheck $($phase.Call))" $run 1 { (Get-TreeSnapshot $moved) -eq $expectedA -and (Get-RunDescription) -eq $expectedRun -and @(Get-Siblings).Count -eq 0 } 'moved installation intact; Run value unchanged'
        & $restore
        if ($phase.Run -eq 'any') {
            $null = Invoke-PackageScript $packageA 'set-autostart.ps1' '-Enabled $true'
        }
    }

    $run = Invoke-PackageScript $packageA 'set-autostart.ps1' '-Enabled $true'
    Assert-Outcome 'Re-enable after negative cases' 'set-autostart.ps1 -Enabled $true' $run 0 { (Get-RunDescription) -eq 'owned' -and (Get-TreeSnapshot $application) -eq $expectedA } 'owned, files intact'

    # Upgrade, rollback, and interrupted swaps.
    $run = Invoke-PackageScript $packageB 'install.ps1'
    Assert-Outcome 'Upgrade A to B' 'install.ps1 (package B)' $run 0 { (Get-TreeSnapshot $application) -eq $expectedB -and (Get-RunDescription) -eq 'owned' -and @(Get-Siblings).Count -eq 0 -and (Get-TreeSnapshot $settingsDirectory) -eq $settingsBefore } 'payload B; autostart and settings kept'

    $run = Invoke-Injected $packageA @'
$script:TestMoves = 0
function script:Move-PayloadDirectory { param([string]$Source, [string]$Destination)
    $script:TestMoves++
    if ($script:TestMoves -eq 2) { throw 'Injected activation failure.' }
    [IO.Directory]::Move($Source, $Destination) }
'@ "Invoke-SrmInstall -PackageDirectory '$($packageA.Replace("'", "''"))'"
    Assert-Outcome 'Injected failure between the two renames' 'Invoke-SrmInstall (second rename fails)' $run 1 { (Get-TreeSnapshot $application) -eq $expectedB -and @(Get-Siblings).Count -eq 0 } 'payload B restored, no work directories'

    $crashAfter = @'
$script:TestMoves = 0
function script:Move-PayloadDirectory { param([string]$Source, [string]$Destination)
    $script:TestMoves++
    [IO.Directory]::Move($Source, $Destination)
    if ($script:TestMoves -eq __MOVE__) { Stop-Process -Id $PID -Force } }
'@
    $run = Invoke-Injected $packageA ($crashAfter.Replace('__MOVE__', '1')) "Invoke-SrmInstall -PackageDirectory '$($packageA.Replace("'", "''"))'"
    $interrupted = -not [IO.Directory]::Exists($application) -and @(Get-Siblings).Count -eq 2
    Add-Result 'Interrupt after the first rename' 'Invoke-SrmInstall (process killed)' $run.ExitCode $(if ($interrupted) { 'pass' } else { 'fail' }) 'no active directory; previous and staging directories remain'
    $run = Invoke-PackageScript $packageA 'install.ps1'
    Assert-Outcome 'Next invocation recovers, then upgrades' 'install.ps1 (package A)' $run 0 { (Get-TreeSnapshot $application) -eq $expectedA -and @(Get-Siblings).Count -eq 0 -and (Get-RunDescription) -eq 'owned' } 'payload A, no work directories'

    $run = Invoke-Injected $packageB ($crashAfter.Replace('__MOVE__', '2')) "Invoke-SrmInstall -PackageDirectory '$($packageB.Replace("'", "''"))'"
    $interrupted = (Get-TreeSnapshot $application) -eq $expectedB -and @(Get-Siblings).Count -eq 1
    Add-Result 'Interrupt after the second rename' 'Invoke-SrmInstall (process killed)' $run.ExitCode $(if ($interrupted) { 'pass' } else { 'fail' }) 'payload B active; previous directory remains'
    $run = Invoke-PackageScript $packageB 'set-autostart.ps1' '-Enabled $true'
    Assert-Outcome 'Next invocation removes the previous installation' 'set-autostart.ps1 -Enabled $true' $run 0 { (Get-TreeSnapshot $application) -eq $expectedB -and @(Get-Siblings).Count -eq 0 } 'payload B, no work directories'

    # Running bar: closed through WM_CLOSE before upgrade and uninstall, with the work area restored.
    if (Test-BarGuardHeld) {
        Add-Result 'Close a running installed bar' 'install.ps1 / uninstall.ps1' '' 'skipped' 'another bar holds the session guard; exit it to run these checks'
    }
    else {
        $baseline = [InstallationChecks.WorkArea]::Get()
        $bar = Start-Process -FilePath $executable -PassThru
        $started = Wait-BarWindow $bar
        $reserved = [InstallationChecks.WorkArea]::Get()
        $run = Invoke-PackageScript $packageA 'install.ps1'
        Start-Sleep -Milliseconds 500
        Assert-Outcome 'Upgrade closes the running bar' 'install.ps1 (package A)' $run 0 { $started -and $reserved -ne $baseline -and $bar.HasExited -and ([InstallationChecks.WorkArea]::Get()) -eq $baseline -and @(Get-InstalledBarProcesses).Count -eq 0 -and (Get-TreeSnapshot $application) -eq $expectedA } 'bar exited, work area restored, not restarted'

        $planted = Start-Process -FilePath (Join-Path $plantedDirectory 'SystemResourceMonitor.Desktop.exe') -PassThru
        $plantedStarted = Wait-BarWindow $planted
        $run = Invoke-PackageScript $packageA 'uninstall.ps1'
        Assert-Outcome 'Planted prefix executable is not closed or removed' 'uninstall.ps1' $run 0 { $plantedStarted -and -not $planted.HasExited -and [IO.File]::Exists((Join-Path $plantedDirectory 'SystemResourceMonitor.Desktop.exe')) -and -not [IO.Directory]::Exists($application) } 'planted bar still running; installation removed'
        $windows = @([SystemResourceMonitorInstall.Native]::GetVisibleTopLevelWindows([uint32]$planted.Id))
        if ($windows.Count -eq 1) { [SystemResourceMonitorInstall.Native]::PostClose($windows[0]) }
        $null = $planted.WaitForExit(5000)
        Start-Sleep -Milliseconds 500

        $null = Invoke-PackageScript $packageA 'install.ps1'
        $null = Invoke-PackageScript $packageA 'set-autostart.ps1' '-Enabled $true'
        $bar = Start-Process -FilePath $executable -PassThru
        $started = Wait-BarWindow $bar
        $run = Invoke-PackageScript $packageA 'uninstall.ps1'
        Start-Sleep -Milliseconds 500
        Assert-Outcome 'Uninstall closes the running bar' 'uninstall.ps1' $run 0 { $started -and $bar.HasExited -and ([InstallationChecks.WorkArea]::Get()) -eq $baseline -and @(Get-InstalledBarProcesses).Count -eq 0 -and -not [IO.Directory]::Exists($application) -and (Get-RunDescription) -eq 'absent' } 'bar exited, work area restored, payload and Run value removed'
        $null = Invoke-PackageScript $packageA 'install.ps1'
        $null = Invoke-PackageScript $packageA 'set-autostart.ps1' '-Enabled $true'
    }

    # Uninstall conflicts and manifest tampering.
    $extra = Join-Path $application 'extra.txt'
    Set-Content -LiteralPath $extra -Value 'extra'
    $run = Invoke-PackageScript $packageA 'uninstall.ps1'
    $remaining = @([IO.Directory]::GetFiles($application, '*', [IO.SearchOption]::AllDirectories) | ForEach-Object { $_.Substring($application.Length + 1) } | Sort-Object)
    Assert-Outcome 'Uninstall with an unexpected file' 'uninstall.ps1' $run 1 { ($remaining -join ',') -eq 'extra.txt,install-manifest.json' -and (Get-RunDescription) -eq 'owned' } 'listed files removed; unexpected file, manifest, and Run value kept'
    Remove-Item -LiteralPath $extra
    $run = Invoke-PackageScript $packageA 'uninstall.ps1'
    Assert-Outcome 'Uninstall after the conflict is resolved' 'uninstall.ps1' $run 0 { -not [IO.Directory]::Exists($application) -and (Get-RunDescription) -eq 'absent' } 'manifest, directory, and Run value removed'

    $null = Invoke-PackageScript $packageA 'install.ps1'
    $null = Invoke-PackageScript $packageA 'set-autostart.ps1' '-Enabled $true'
    Add-Content -LiteralPath $installedReadme -Value 'changed'
    $run = Invoke-PackageScript $packageA 'uninstall.ps1'
    $remaining = @([IO.Directory]::GetFiles($application, '*', [IO.SearchOption]::AllDirectories) | ForEach-Object { $_.Substring($application.Length + 1) } | Sort-Object)
    Assert-Outcome 'Uninstall with a changed file' 'uninstall.ps1' $run 1 { ($remaining -join ',') -eq 'install-manifest.json,README.md' -and (Get-RunDescription) -eq 'owned' } 'changed file, manifest, and Run value kept'
    Remove-Item -LiteralPath $installedReadme
    $run = Invoke-PackageScript $packageA 'uninstall.ps1'
    Assert-Outcome 'Uninstall after the changed file is removed' 'uninstall.ps1' $run 0 { -not [IO.Directory]::Exists($application) -and (Get-RunDescription) -eq 'absent' } 'removed'

    $null = Invoke-PackageScript $packageA 'install.ps1'
    $null = Invoke-PackageScript $packageA 'set-autostart.ps1' '-Enabled $true'
    $manifestPath = Join-Path $application 'install-manifest.json'
    $manifestBytes = [IO.File]::ReadAllBytes($manifestPath)
    foreach ($tamper in @('../escape.txt', 'C:/escape.txt', 'SYSTEMRESOURCEMONITOR.DESKTOP.EXE')) {
        $json = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
        $json.files += [pscustomobject]@{ relativePath = $tamper; sha256 = ('0' * 64) }
        [IO.File]::WriteAllText($manifestPath, ($json | ConvertTo-Json -Depth 4))
        $tampered = Get-TreeSnapshot $application
        $run = Invoke-PackageScript $packageA 'uninstall.ps1'
        Assert-Outcome "Installed manifest with '$tamper': uninstall" 'uninstall.ps1' $run 1 { (Get-TreeSnapshot $application) -eq $tampered -and (Get-RunDescription) -eq 'owned' } 'nothing removed'
        $run = Invoke-PackageScript $packageA 'set-autostart.ps1' '-Enabled $false'
        Assert-Outcome "Installed manifest with '$tamper': disable" 'set-autostart.ps1 -Enabled $false' $run 1 { (Get-RunDescription) -eq 'owned' } 'Run value kept'
        [IO.File]::WriteAllBytes($manifestPath, $manifestBytes)
    }

    $run = Invoke-PackageScript $packageA 'uninstall.ps1'
    Assert-Outcome 'Uninstall' 'uninstall.ps1' $run 0 { -not [IO.Directory]::Exists($application) -and (Get-RunDescription) -eq 'absent' -and @(Get-Siblings).Count -eq 0 -and (Get-TreeSnapshot $settingsDirectory) -eq $settingsBefore } 'payload and Run value removed; settings retained'
    $run = Invoke-PackageScript $packageA 'uninstall.ps1'
    Assert-Outcome 'Repeat uninstall' 'uninstall.ps1' $run 0 $nothingInstalled 'nothing to do'
    $run = Invoke-PackageScript $packageA 'set-autostart.ps1' '-Enabled $true'
    Assert-Outcome 'Enable autostart without an installation' 'set-autostart.ps1 -Enabled $true' $run 1 $nothingInstalled 'Run value not written'
}
catch {
    Add-Result 'Harness' '' '' 'fail' ($_.Exception.Message + ' at line ' + $_.InvocationInfo.ScriptLineNumber)
}
finally {
    foreach ($process in @(Get-InstalledBarProcesses)) { $process.Kill() }
    foreach ($path in @($application) + @(Get-Siblings) + @($plantedDirectory)) {
        if (Test-Path -LiteralPath $path) {
            try { Remove-UsersWriteRule $path } catch { }
            Remove-TestTree $path
        }
    }
    if ([IO.Directory]::Exists($moved)) { Remove-TestTree $moved }
    $leftRun = Get-RunValue
    if ($null -ne $leftRun) { Remove-RunValue }
    if (-not $programsExisted -and [IO.Directory]::Exists($programs) -and @([IO.Directory]::GetFileSystemEntries($programs)).Count -eq 0) {
        [IO.Directory]::Delete($programs, $false)
    }
    Remove-TestTree $work
}

$failed = @($results | Where-Object { $_.Result -eq 'fail' }).Count
$passed = @($results | Where-Object { $_.Result -eq 'pass' }).Count
$skipped = @($results | Where-Object { $_.Result -eq 'skipped' }).Count
Write-Host ("Passed: {0}; failed: {1}; skipped: {2}" -f $passed, $failed, $skipped)
if ($ResultPath) {
    $lines = @('| Check | Command | Exit code | Result | Observation |', '|---|---|---|---|---|')
    $lines += @($results | ForEach-Object { '| {0} | `{1}` | {2} | {3} | {4} |' -f $_.Check, $_.Command, $_.ExitCode, $_.Result, ($_.Detail -replace '\|', '/') })
    [IO.File]::WriteAllLines($ResultPath, $lines)
}
if ($failed -gt 0) { exit 1 }
exit 0
