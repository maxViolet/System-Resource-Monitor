#Requires -Version 5.1
# Shared implementation of the per-user installation scripts (ADR 0014).
# The destination, executable name, manifest name, and Run value name are fixed; no caller can override them.

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$script:ApplicationDirectoryName = 'SystemResourceMonitor'
$script:ProgramsDirectoryName = 'Programs'
$script:ExecutableName = 'SystemResourceMonitor.Desktop.exe'
$script:BarProcessName = 'SystemResourceMonitor.Desktop'
$script:ManifestName = 'install-manifest.json'
$script:PackagePayloadDirectoryName = 'app'
$script:RunKeyPath = 'Software\Microsoft\Windows\CurrentVersion\Run'
$script:RunValueName = 'SystemResourceMonitor'
$script:StagePrefix = '.SystemResourceMonitor.stage-'
$script:PreviousPrefix = '.SystemResourceMonitor.previous-'
$script:InstallerMutexName = 'Local\SystemResourceMonitor.Installer-6E0D1C5B-8F7A-4E8B-9C1D-2B7A4F3E5D60'
$script:CloseTimeoutMilliseconds = 5000
$script:MaximumManifestBytes = 1MB
$script:SystemSid = 'S-1-5-18'
$script:AdministratorsSid = 'S-1-5-32-544'
$script:CreatorOwnerSid = 'S-1-3-0'
$script:OwnerRightsSid = 'S-1-3-4'

# WriteData, AppendData, WriteExtendedAttributes, DeleteSubdirectoriesAndFiles, WriteAttributes, Delete,
# ChangePermissions, TakeOwnership, GENERIC_ALL, and GENERIC_WRITE.
$script:WriteRightsMask = 0x500D0156

if (-not ('SystemResourceMonitorInstall.Native' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SystemResourceMonitorInstall
{
    public static class Native
    {
        private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, IntPtr token, out IntPtr path);

        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr memory);

        private static readonly Guid LocalAppDataFolderId = new Guid("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");

        public static string GetLocalAppDataPath()
        {
            Guid folderId = LocalAppDataFolderId;
            IntPtr path;
            int result = SHGetKnownFolderPath(ref folderId, 0, IntPtr.Zero, out path);
            try
            {
                if (result != 0)
                {
                    Marshal.ThrowExceptionForHR(result);
                }

                return Marshal.PtrToStringUni(path);
            }
            finally
            {
                CoTaskMemFree(path);
            }
        }

        public static IntPtr[] GetVisibleTopLevelWindows(uint processId)
        {
            var windows = new List<IntPtr>();
            EnumWindows((window, parameter) =>
            {
                uint owner;
                GetWindowThreadProcessId(window, out owner);
                if (owner == processId && IsWindowVisible(window))
                {
                    windows.Add(window);
                }

                return true;
            }, IntPtr.Zero);
            return windows.ToArray();
        }

        public static void PostClose(IntPtr window)
        {
            const uint WM_CLOSE = 0x0010;
            if (!PostMessageW(window, WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
        }
    }
}
'@
}

function Stop-Installation {
    param([Parameter(Mandatory = $true)][string]$Message)
    throw (New-Object System.InvalidOperationException $Message)
}

function Write-Step {
    param([string]$Message)
    Write-Host $Message
}

function Get-CurrentUserSid {
    return [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
}

function Assert-NotElevated {
    $principal = New-Object Security.Principal.WindowsPrincipal ([Security.Principal.WindowsIdentity]::GetCurrent())
    if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Stop-Installation 'Run this script from a PowerShell window that is not elevated. The installation belongs to the current user and never needs administrator rights.'
    }
}

function Test-ReparsePoint {
    param([string]$Path)
    $attributes = [IO.File]::GetAttributes($Path)
    return ($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
}

function Resolve-CanonicalDirectory {
    # Walks every component from the drive root, takes each name as stored on disk, and rejects reparse points.
    param([Parameter(Mandatory = $true)][string]$Path)

    $full = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetPathRoot($full)
    if ($root -notmatch '^[A-Za-z]:\\$') {
        Stop-Installation "The path '$full' is not on a local drive."
    }

    $drive = New-Object IO.DriveInfo $root
    if ($drive.DriveType -ne [IO.DriveType]::Fixed) {
        Stop-Installation "The path '$full' is not on a fixed local drive."
    }

    $current = $root.ToUpperInvariant()
    $relative = $full.Substring($root.Length).TrimEnd('\')
    if ($relative.Length -eq 0) {
        return $current
    }

    foreach ($name in $relative.Split('\')) {
        if ($name.IndexOfAny([char[]]'*?') -ge 0) {
            Stop-Installation "The path '$full' contains a wildcard character."
        }

        $found = @([IO.Directory]::GetDirectories($current, $name))
        if ($found.Count -ne 1) {
            Stop-Installation "The directory '$name' under '$current' does not exist or is ambiguous."
        }

        $next = $found[0]
        if (Test-ReparsePoint $next) {
            Stop-Installation "The path component '$next' is a junction, symbolic link, or other reparse point."
        }

        $current = $next
    }

    return $current
}

function Get-InstallLayout {
    $knownFolder = [SystemResourceMonitorInstall.Native]::GetLocalAppDataPath()
    $localAppData = Resolve-CanonicalDirectory $knownFolder
    $programs = Join-Path $localAppData $script:ProgramsDirectoryName
    if ([IO.Directory]::Exists($programs)) {
        # Windows paths are case-insensitive; the Run value uses the name stored on disk.
        $programs = Resolve-CanonicalDirectory $programs
    }

    $application = Join-Path $programs $script:ApplicationDirectoryName
    $executable = Join-Path $application $script:ExecutableName
    return [pscustomobject]@{
        LocalAppData = $localAppData
        ProgramsDirectory = $programs
        ApplicationDirectory = $application
        ExecutablePath = $executable
        ManifestPath = Join-Path $application $script:ManifestName
        RunValueData = '"' + $executable + '"'
        UserSid = Get-CurrentUserSid
    }
}

function Get-AccessProblems {
    # Returns descriptions of an untrusted owner or of write access granted to an untrusted principal.
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][bool]$IsDirectory,
        [Parameter(Mandatory = $true)][string]$UserSid,
        [Parameter(Mandatory = $true)][bool]$RequireUserOwner
    )

    $sections = [Security.AccessControl.AccessControlSections]'Owner, Access'
    if ($IsDirectory) {
        $security = (New-Object IO.DirectoryInfo $Path).GetAccessControl($sections)
    }
    else {
        $security = (New-Object IO.FileInfo $Path).GetAccessControl($sections)
    }

    $problems = New-Object System.Collections.Generic.List[string]
    $trusted = @($UserSid, $script:SystemSid, $script:AdministratorsSid)
    $owner = $security.GetOwner([Security.Principal.SecurityIdentifier]).Value
    if ($RequireUserOwner) {
        if ($owner -ne $UserSid) {
            $problems.Add("'$Path' is not owned by the current user")
        }
    }
    elseif ($trusted -notcontains $owner) {
        $problems.Add("'$Path' has an untrusted owner")
    }

    foreach ($rule in $security.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow) {
            continue
        }

        $sid = $rule.IdentityReference.Value
        if ($trusted -contains $sid -or $sid -eq $script:OwnerRightsSid) {
            continue
        }

        $inheritOnly = ($rule.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly) -ne 0
        if ($sid -eq $script:CreatorOwnerSid -and $inheritOnly) {
            # Windows replaces CREATOR OWNER with the creator, the current user, on new children.
            continue
        }

        if (([int]$rule.FileSystemRights -band $script:WriteRightsMask) -ne 0) {
            $problems.Add("'$Path' grants write access to $sid")
        }
    }

    return , $problems
}

function Assert-SecureDirectory {
    param([string]$Path, [string]$UserSid, [bool]$RequireUserOwner)
    if (Test-ReparsePoint $Path) {
        Stop-Installation "'$Path' is a junction, symbolic link, or other reparse point."
    }

    $problems = Get-AccessProblems -Path $Path -IsDirectory $true -UserSid $UserSid -RequireUserOwner $RequireUserOwner
    if ($problems.Count -gt 0) {
        Stop-Installation ("Unsafe permissions: " + ($problems -join '; ') + '.')
    }
}

function Get-SecureTreeInventory {
    # Walks a directory tree without following reparse points. Every entry must be free of reparse points,
    # owned by a trusted principal, and writable only by trusted principals. Returns relative file paths with '/'.
    param([Parameter(Mandatory = $true)][string]$Root, [Parameter(Mandatory = $true)][string]$UserSid)

    Assert-SecureDirectory -Path $Root -UserSid $UserSid -RequireUserOwner $true
    $files = New-Object System.Collections.Generic.List[string]
    $pending = New-Object System.Collections.Generic.Stack[string]
    $pending.Push($Root)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($entry in [IO.Directory]::GetFileSystemEntries($directory)) {
            $attributes = [IO.File]::GetAttributes($entry)
            if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                Stop-Installation "'$entry' is a junction, symbolic link, or other reparse point."
            }

            $isDirectory = ($attributes -band [IO.FileAttributes]::Directory) -ne 0
            $problems = Get-AccessProblems -Path $entry -IsDirectory $isDirectory -UserSid $UserSid -RequireUserOwner $false
            if ($problems.Count -gt 0) {
                Stop-Installation ("Unsafe permissions: " + ($problems -join '; ') + '.')
            }

            if ($isDirectory) {
                $pending.Push($entry)
            }
            else {
                $files.Add($entry.Substring($Root.Length + 1).Replace('\', '/'))
            }
        }
    }

    return , $files
}

function Assert-InstallationLocation {
    # Revalidates the fixed location before each phase: the known-folder path, the Programs directory,
    # and, when present, the application directory subtree. Returns the application inventory or $null.
    param([Parameter(Mandatory = $true)]$Layout)

    $current = Get-InstallLayout
    if ($current.ApplicationDirectory -cne $Layout.ApplicationDirectory) {
        Stop-Installation 'The per-user installation location changed during the operation.'
    }

    if (-not [IO.Directory]::Exists($Layout.ProgramsDirectory)) {
        if ([IO.File]::Exists($Layout.ProgramsDirectory)) {
            Stop-Installation "'$($Layout.ProgramsDirectory)' is a file, not a directory."
        }

        return $null
    }

    $null = Resolve-CanonicalDirectory $Layout.ProgramsDirectory
    Assert-SecureDirectory -Path $Layout.ProgramsDirectory -UserSid $Layout.UserSid -RequireUserOwner $false
    if (-not [IO.Directory]::Exists($Layout.ApplicationDirectory)) {
        if ([IO.File]::Exists($Layout.ApplicationDirectory)) {
            Stop-Installation "'$($Layout.ApplicationDirectory)' is a file, not a directory."
        }

        return $null
    }

    if ((Resolve-CanonicalDirectory $Layout.ApplicationDirectory) -cne $Layout.ApplicationDirectory) {
        Stop-Installation "'$($Layout.ApplicationDirectory)' does not have its canonical name."
    }

    return , (Get-SecureTreeInventory -Root $Layout.ApplicationDirectory -UserSid $Layout.UserSid)
}

function Test-ManifestRelativePath {
    param([string]$RelativePath)
    if ([string]::IsNullOrEmpty($RelativePath) -or $RelativePath.Length -gt 200) {
        return $false
    }

    foreach ($segment in $RelativePath.Split('/')) {
        if ($segment.Length -eq 0 -or $segment -eq '.' -or $segment -eq '..') {
            return $false
        }

        if ($segment -notmatch '^[^<>:"/\\|?*\x00-\x1F]+$' -or $segment.EndsWith('.') -or $segment.EndsWith(' ') -or $segment.StartsWith(' ')) {
            return $false
        }

        if ($segment -match '^(?i:CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\..*)?$') {
            return $false
        }
    }

    return $RelativePath -ne $script:ManifestName
}

function Get-ManifestFilePath {
    # Maps a validated relative path to an absolute path and rejects any result outside the root.
    param([string]$Root, [string]$RelativePath)
    $full = [IO.Path]::GetFullPath((Join-Path $Root ($RelativePath.Replace('/', '\'))))
    if (-not $full.StartsWith($Root + '\', [StringComparison]::OrdinalIgnoreCase)) {
        Stop-Installation "The manifest path '$RelativePath' escapes the application directory."
    }

    return $full
}

function Read-InstallManifest {
    # Parses and validates install-manifest.json. Any absolute, traversing, duplicate, escaping, or malformed entry rejects it.
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Root)

    if (-not [IO.File]::Exists($Path)) {
        Stop-Installation "The manifest '$Path' does not exist."
    }

    if ((New-Object IO.FileInfo $Path).Length -gt $script:MaximumManifestBytes) {
        Stop-Installation "The manifest '$Path' is too large."
    }

    try {
        $document = [IO.File]::ReadAllText($Path, [Text.Encoding]::UTF8) | ConvertFrom-Json
    }
    catch {
        Stop-Installation "The manifest '$Path' is not valid JSON."
    }

    if ($null -eq $document -or $document -isnot [pscustomobject]) {
        Stop-Installation "The manifest '$Path' is not a JSON object."
    }

    $names = @($document.PSObject.Properties | ForEach-Object { $_.Name })
    if ($names.Count -ne 2 -or $names -notcontains 'sourceCommit' -or $names -notcontains 'files') {
        Stop-Installation "The manifest '$Path' must contain exactly 'sourceCommit' and 'files'."
    }

    if ($document.sourceCommit -isnot [string] -or $document.sourceCommit -cnotmatch '^[0-9a-f]{40}$') {
        Stop-Installation "The manifest '$Path' has an invalid source commit."
    }

    if ($document.files -isnot [array] -or $document.files.Count -eq 0) {
        Stop-Installation "The manifest '$Path' has no file list."
    }

    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $files = New-Object System.Collections.Generic.List[object]
    foreach ($entry in $document.files) {
        if ($entry -isnot [pscustomobject]) {
            Stop-Installation "The manifest '$Path' contains an entry that is not an object."
        }

        $entryNames = @($entry.PSObject.Properties | ForEach-Object { $_.Name })
        if ($entryNames.Count -ne 2 -or $entryNames -notcontains 'relativePath' -or $entryNames -notcontains 'sha256') {
            Stop-Installation "The manifest '$Path' entries must contain exactly 'relativePath' and 'sha256'."
        }

        $relativePath = $entry.relativePath
        if ($relativePath -isnot [string] -or -not (Test-ManifestRelativePath $relativePath)) {
            Stop-Installation "The manifest '$Path' contains an invalid path '$relativePath'."
        }

        if ($entry.sha256 -isnot [string] -or $entry.sha256 -cnotmatch '^[0-9A-F]{64}$') {
            Stop-Installation "The manifest '$Path' has an invalid SHA-256 value for '$relativePath'."
        }

        if (-not $seen.Add($relativePath)) {
            Stop-Installation "The manifest '$Path' lists '$relativePath' more than once."
        }

        $null = Get-ManifestFilePath -Root $Root -RelativePath $relativePath
        $files.Add([pscustomobject]@{ RelativePath = $relativePath; Sha256 = $entry.sha256 })
    }

    if (-not $seen.Contains($script:ExecutableName)) {
        Stop-Installation "The manifest '$Path' does not list $($script:ExecutableName)."
    }

    return [pscustomobject]@{ SourceCommit = $document.sourceCommit; Files = $files }
}

function Write-InstallManifest {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)]$Manifest)
    $entries = @($Manifest.Files | Sort-Object -Property RelativePath -CaseSensitive | ForEach-Object {
            [ordered]@{ relativePath = $_.RelativePath; sha256 = $_.Sha256 }
        })
    $json = ConvertTo-Json -InputObject ([ordered]@{ sourceCommit = $Manifest.SourceCommit; files = $entries }) -Depth 4
    $stream = New-Object IO.FileStream ($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $bytes = (New-Object Text.UTF8Encoding $false).GetBytes($json + "`r`n")
        $stream.Write($bytes, 0, $bytes.Length)
    }
    finally {
        $stream.Dispose()
    }
}

function Get-FileSha256 {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Compare-InstalledPayload {
    # Classifies manifest-listed files as unchanged, changed, or missing, and lists unexpected files.
    param([string]$Root, $Manifest, $Inventory)

    $listed = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $unchanged = New-Object System.Collections.Generic.List[string]
    $changed = New-Object System.Collections.Generic.List[string]
    $missing = New-Object System.Collections.Generic.List[string]
    $unexpected = New-Object System.Collections.Generic.List[string]
    foreach ($file in $Manifest.Files) {
        $null = $listed.Add($file.RelativePath)
        $path = Get-ManifestFilePath -Root $Root -RelativePath $file.RelativePath
        if (-not [IO.File]::Exists($path)) {
            $missing.Add($file.RelativePath)
        }
        elseif ((Get-FileSha256 $path) -ceq $file.Sha256) {
            $unchanged.Add($file.RelativePath)
        }
        else {
            $changed.Add($file.RelativePath)
        }
    }

    foreach ($relativePath in $Inventory) {
        if (-not $listed.Contains($relativePath) -and $relativePath -ne $script:ManifestName) {
            $unexpected.Add($relativePath)
        }
    }

    return [pscustomobject]@{
        Unchanged = $unchanged
        Changed = $changed
        Missing = $missing
        Unexpected = $unexpected
        IsExact = ($changed.Count -eq 0 -and $missing.Count -eq 0 -and $unexpected.Count -eq 0)
    }
}

function Format-PayloadConflicts {
    param($Comparison)
    $lines = @()
    $lines += @($Comparison.Changed | ForEach-Object { "  changed: $_" })
    $lines += @($Comparison.Missing | ForEach-Object { "  missing: $_" })
    $lines += @($Comparison.Unexpected | ForEach-Object { "  unexpected: $_" })
    return ($lines -join [Environment]::NewLine)
}

function Get-VerifiedDirectoryPayload {
    # Validates a directory's tree, manifest, and payload. Throws when the tree is unsafe or the manifest is invalid.
    param([string]$Root, [string]$UserSid)
    $inventory = Get-SecureTreeInventory -Root $Root -UserSid $UserSid
    $manifest = Read-InstallManifest -Path (Join-Path $Root $script:ManifestName) -Root $Root
    $comparison = Compare-InstalledPayload -Root $Root -Manifest $manifest -Inventory $inventory
    return [pscustomobject]@{ Manifest = $manifest; Comparison = $comparison }
}

function Test-ManifestEquivalent {
    param($Left, $Right)
    if ($Left.SourceCommit -cne $Right.SourceCommit -or $Left.Files.Count -ne $Right.Files.Count) {
        return $false
    }

    $rightHashes = @{}
    foreach ($file in $Right.Files) {
        $rightHashes[$file.RelativePath.ToUpperInvariant()] = $file.Sha256
    }

    foreach ($file in $Left.Files) {
        $key = $file.RelativePath.ToUpperInvariant()
        if (-not $rightHashes.ContainsKey($key) -or $rightHashes[$key] -cne $file.Sha256) {
            return $false
        }
    }

    return $true
}

function Remove-ManifestScopedFiles {
    # Removes unchanged manifest-listed files, then the manifest when nothing is left behind, then empty
    # directories implied by the manifest and the root. Never deletes recursively. Returns the leftovers.
    param([string]$Root, $Manifest, $Inventory)

    $leftovers = New-Object System.Collections.Generic.List[string]
    foreach ($file in $Manifest.Files) {
        $path = Get-ManifestFilePath -Root $Root -RelativePath $file.RelativePath
        if (-not [IO.File]::Exists($path)) {
            continue
        }

        Assert-NoReparseBelow -Root $Root -Path $path
        if ((Get-FileSha256 $path) -cne $file.Sha256) {
            $leftovers.Add("changed: $($file.RelativePath)")
            continue
        }

        try {
            Remove-PayloadFile $path
        }
        catch {
            $leftovers.Add("could not remove: $($file.RelativePath)")
        }
    }

    $listed = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $Manifest.Files) {
        $null = $listed.Add($file.RelativePath)
    }

    foreach ($relativePath in $Inventory) {
        if (-not $listed.Contains($relativePath) -and $relativePath -ne $script:ManifestName) {
            $leftovers.Add("unexpected: $relativePath")
        }
    }

    $manifestPath = Join-Path $Root $script:ManifestName
    if ($leftovers.Count -eq 0 -and [IO.File]::Exists($manifestPath)) {
        Assert-NoReparseBelow -Root $Root -Path $manifestPath
        Remove-PayloadFile $manifestPath
    }

    $directories = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $Manifest.Files) {
        $parent = [IO.Path]::GetDirectoryName((Get-ManifestFilePath -Root $Root -RelativePath $file.RelativePath))
        while ($parent.Length -gt $Root.Length) {
            $null = $directories.Add($parent)
            $parent = [IO.Path]::GetDirectoryName($parent)
        }
    }

    foreach ($directory in @($directories | Sort-Object -Property Length -Descending)) {
        Remove-EmptyDirectory -Root $Root -Path $directory
    }

    Remove-EmptyDirectory -Root $Root -Path $Root
    return , $leftovers
}

function Assert-NoReparseBelow {
    param([string]$Root, [string]$Path)
    $current = $Path
    while ($current.Length -ge $Root.Length) {
        if (Test-ReparsePoint $current) {
            Stop-Installation "'$current' became a junction, symbolic link, or other reparse point."
        }

        if ($current.Length -eq $Root.Length) {
            break
        }

        $current = [IO.Path]::GetDirectoryName($current)
    }
}

function Remove-EmptyDirectory {
    param([string]$Root, [string]$Path)
    if (-not [IO.Directory]::Exists($Path)) {
        return
    }

    Assert-NoReparseBelow -Root $Root -Path $Path
    if (@([IO.Directory]::GetFileSystemEntries($Path)).Count -eq 0) {
        [IO.Directory]::Delete($Path, $false)
    }
}

function Remove-PayloadFile {
    param([string]$Path)
    [IO.File]::Delete($Path)
}

function Copy-PayloadFile {
    param([string]$Source, [string]$Destination)
    [IO.File]::Copy($Source, $Destination, $false)
}

function Move-PayloadDirectory {
    param([string]$Source, [string]$Destination)
    [IO.Directory]::Move($Source, $Destination)
}

function Get-SiblingDirectories {
    param($Layout, [string]$Prefix)
    if (-not [IO.Directory]::Exists($Layout.ProgramsDirectory)) {
        return , @()
    }

    return , @([IO.Directory]::GetDirectories($Layout.ProgramsDirectory, $Prefix + '*') |
            Where-Object { [IO.Path]::GetFileName($_).StartsWith($Prefix, [StringComparison]::Ordinal) })
}

function New-SiblingPath {
    param($Layout, [string]$Prefix)
    $path = Join-Path $Layout.ProgramsDirectory ($Prefix + [guid]::NewGuid().ToString('N'))
    if ([IO.Directory]::Exists($path) -or [IO.File]::Exists($path)) {
        Stop-Installation "'$path' already exists."
    }

    return $path
}

function Remove-StagingDirectory {
    # Removes only verified staged files, the staged manifest, and empty directories. Reports anything else.
    param($Layout, [string]$Path)
    try {
        $inventory = Get-SecureTreeInventory -Root $Path -UserSid $Layout.UserSid
    }
    catch {
        Write-Step "Left the staging directory '$Path' in place: $($_.Exception.Message)"
        return
    }

    $manifestPath = Join-Path $Path $script:ManifestName
    if (-not [IO.File]::Exists($manifestPath)) {
        if ($inventory.Count -eq 0) {
            Remove-EmptyDirectoryTree -Root $Path
            return
        }

        Write-Step "Left the staging directory '$Path' in place: it has files but no manifest."
        return
    }

    try {
        $manifest = Read-InstallManifest -Path $manifestPath -Root $Path
    }
    catch {
        Write-Step "Left the staging directory '$Path' in place: $($_.Exception.Message)"
        return
    }

    $leftovers = Remove-ManifestScopedFiles -Root $Path -Manifest $manifest -Inventory $inventory
    if ($leftovers.Count -gt 0) {
        Write-Step ("Left files that could not be verified in '$Path':" + [Environment]::NewLine + (($leftovers | ForEach-Object { "  $_" }) -join [Environment]::NewLine))
    }
}

function Remove-EmptyDirectoryTree {
    param([string]$Root)
    $directories = @([IO.Directory]::GetDirectories($Root, '*', [IO.SearchOption]::AllDirectories) | Sort-Object -Property Length -Descending)
    foreach ($directory in $directories) {
        Remove-EmptyDirectory -Root $Root -Path $directory
    }

    Remove-EmptyDirectory -Root $Root -Path $Root
}

function Resolve-InterruptedOperation {
    # Reconciles a crashed staging or upgrade swap before any new mutation.
    param($Layout)

    $previous = Get-SiblingDirectories -Layout $Layout -Prefix $script:PreviousPrefix
    if ($previous.Count -gt 1) {
        Stop-Installation "More than one previous installation exists under '$($Layout.ProgramsDirectory)'. Resolve them manually."
    }

    if ($previous.Count -eq 1) {
        $old = $previous[0]
        $oldPayload = Get-VerifiedDirectoryPayload -Root $old -UserSid $Layout.UserSid
        if (-not [IO.Directory]::Exists($Layout.ApplicationDirectory)) {
            if (-not $oldPayload.Comparison.IsExact) {
                Stop-Installation ("An interrupted upgrade left '$old', but its files do not match its manifest:" + [Environment]::NewLine + (Format-PayloadConflicts $oldPayload.Comparison))
            }

            $null = Assert-InstallationLocation $Layout
            Move-PayloadDirectory -Source $old -Destination $Layout.ApplicationDirectory
            Write-Step 'Restored the previous installation after an interrupted upgrade.'
        }
        else {
            $active = Get-VerifiedDirectoryPayload -Root $Layout.ApplicationDirectory -UserSid $Layout.UserSid
            if (-not $active.Comparison.IsExact) {
                Stop-Installation ("An interrupted upgrade left '$old', and the installed files do not match their manifest:" + [Environment]::NewLine + (Format-PayloadConflicts $active.Comparison))
            }

            if ($oldPayload.Comparison.Changed.Count -gt 0 -or $oldPayload.Comparison.Unexpected.Count -gt 0) {
                Stop-Installation ("An interrupted upgrade left '$old' with files that do not match its manifest:" + [Environment]::NewLine + (Format-PayloadConflicts $oldPayload.Comparison))
            }

            $inventory = Get-SecureTreeInventory -Root $old -UserSid $Layout.UserSid
            $leftovers = Remove-ManifestScopedFiles -Root $old -Manifest $oldPayload.Manifest -Inventory $inventory
            if ($leftovers.Count -gt 0) {
                Stop-Installation ("Could not finish removing the previous installation '$old':" + [Environment]::NewLine + (($leftovers | ForEach-Object { "  $_" }) -join [Environment]::NewLine))
            }

            Write-Step 'Removed the previous installation left by an interrupted upgrade.'
        }
    }

    foreach ($stage in (Get-SiblingDirectories -Layout $Layout -Prefix $script:StagePrefix)) {
        Write-Step "Cleaning up the staging directory left by an interrupted operation."
        Remove-StagingDirectory -Layout $Layout -Path $stage
    }
}

function Get-RunValueState {
    # Returns Absent, Owned, or Foreign. Owned requires REG_SZ and exactly the quoted canonical executable path.
    param($Layout)
    $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryView]::Registry64)
    try {
        $key = $base.OpenSubKey($script:RunKeyPath, $false)
        if ($null -eq $key) {
            return 'Absent'
        }

        try {
            if (@($key.GetValueNames()) -notcontains $script:RunValueName) {
                return 'Absent'
            }

            $kind = $key.GetValueKind($script:RunValueName)
            $data = $key.GetValue($script:RunValueName, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            if ($kind -eq [Microsoft.Win32.RegistryValueKind]::String -and $data -is [string] -and $data -ceq $Layout.RunValueData) {
                return 'Owned'
            }

            return 'Foreign'
        }
        finally {
            $key.Dispose()
        }
    }
    finally {
        $base.Dispose()
    }
}

function Set-OwnedRunValue {
    param($Layout)
    $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryView]::Registry64)
    try {
        $key = $base.CreateSubKey($script:RunKeyPath)
        try {
            $key.SetValue($script:RunValueName, $Layout.RunValueData, [Microsoft.Win32.RegistryValueKind]::String)
        }
        finally {
            $key.Dispose()
        }
    }
    finally {
        $base.Dispose()
    }
}

function Remove-OwnedRunValue {
    param($Layout)
    if ((Get-RunValueState $Layout) -ne 'Owned') {
        Stop-Installation 'The Run value changed during the operation; it was left unchanged.'
    }

    $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryView]::Registry64)
    try {
        $key = $base.OpenSubKey($script:RunKeyPath, $true)
        try {
            $key.DeleteValue($script:RunValueName, $true)
        }
        finally {
            $key.Dispose()
        }
    }
    finally {
        $base.Dispose()
    }
}

function Stop-InstalledBar {
    # Closes a bar started from the canonical installed executable through its handled WM_CLOSE path.
    param($Layout)

    $sessionId = (Get-Process -Id $PID).SessionId
    $matching = New-Object System.Collections.Generic.List[System.Diagnostics.Process]
    foreach ($process in @(Get-Process -Name $script:BarProcessName -ErrorAction SilentlyContinue)) {
        $path = $null
        try {
            $path = $process.Path
        }
        catch {
            $path = $null
        }

        if ([string]::IsNullOrEmpty($path)) {
            if ($process.SessionId -eq $sessionId) {
                Stop-Installation "Cannot read the executable path of bar process $($process.Id); exit the bar and try again."
            }

            continue
        }

        if ([string]::Equals([IO.Path]::GetFullPath($path), $Layout.ExecutablePath, [StringComparison]::OrdinalIgnoreCase)) {
            $matching.Add($process)
        }
    }

    if ($matching.Count -eq 0) {
        return $false
    }

    if ($matching.Count -gt 1) {
        Stop-Installation 'More than one process runs the installed executable; exit the bar and try again.'
    }

    $bar = $matching[0]
    $windows = @([SystemResourceMonitorInstall.Native]::GetVisibleTopLevelWindows([uint32]$bar.Id))
    if ($windows.Count -ne 1) {
        Stop-Installation "The installed bar (process $($bar.Id)) has no unique bar window; exit the bar from its notification-area icon and try again."
    }

    Write-Step 'Closing the installed bar.'
    [SystemResourceMonitorInstall.Native]::PostClose($windows[0])
    if (-not $bar.WaitForExit($script:CloseTimeoutMilliseconds)) {
        Stop-Installation 'The installed bar did not exit within 5 seconds; nothing was changed. Exit it from its notification-area icon and try again.'
    }

    return $true
}

function Get-CandidatePayload {
    # Validates the package beside the scripts: its manifest and an exact payload inventory.
    param([string]$PackageDirectory)

    $package = [IO.Path]::GetFullPath($PackageDirectory)
    $payload = Join-Path $package $script:PackagePayloadDirectoryName
    if (-not [IO.Directory]::Exists($payload)) {
        Stop-Installation "The package has no '$($script:PackagePayloadDirectoryName)' folder next to the scripts."
    }

    $manifest = Read-InstallManifest -Path (Join-Path $package $script:ManifestName) -Root $payload
    $files = New-Object System.Collections.Generic.List[string]
    foreach ($entry in [IO.Directory]::GetFiles($payload, '*', [IO.SearchOption]::AllDirectories)) {
        $files.Add($entry.Substring($payload.Length + 1).Replace('\', '/'))
    }

    $comparison = Compare-InstalledPayload -Root $payload -Manifest $manifest -Inventory $files
    if (-not $comparison.IsExact) {
        Stop-Installation ("The package payload does not match its manifest:" + [Environment]::NewLine + (Format-PayloadConflicts $comparison))
    }

    return [pscustomobject]@{ Directory = $payload; Manifest = $manifest }
}

function New-StagedPayload {
    # Copies the candidate into a new sibling staging directory and verifies it before activation.
    param($Layout, $Candidate)

    $stage = New-SiblingPath -Layout $Layout -Prefix $script:StagePrefix
    $null = [IO.Directory]::CreateDirectory($stage)
    try {
        Write-InstallManifest -Path (Join-Path $stage $script:ManifestName) -Manifest $Candidate.Manifest
        foreach ($file in $Candidate.Manifest.Files) {
            $target = Get-ManifestFilePath -Root $stage -RelativePath $file.RelativePath
            $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
            Copy-PayloadFile -Source (Get-ManifestFilePath -Root $Candidate.Directory -RelativePath $file.RelativePath) -Destination $target
        }

        $null = Assert-InstallationLocation $Layout
        $staged = Get-VerifiedDirectoryPayload -Root $stage -UserSid $Layout.UserSid
        if (-not $staged.Comparison.IsExact -or -not (Test-ManifestEquivalent $staged.Manifest $Candidate.Manifest)) {
            Stop-Installation ("The staged copy does not match the package:" + [Environment]::NewLine + (Format-PayloadConflicts $staged.Comparison))
        }
    }
    catch {
        $failure = $_
        Remove-StagingDirectory -Layout $Layout -Path $stage
        throw $failure
    }

    return $stage
}

function Assert-ActivePayloadMatches {
    param($Layout, $Candidate)
    $active = Get-VerifiedDirectoryPayload -Root $Layout.ApplicationDirectory -UserSid $Layout.UserSid
    if (-not $active.Comparison.IsExact -or -not (Test-ManifestEquivalent $active.Manifest $Candidate.Manifest)) {
        Stop-Installation ("The installed files do not match the package:" + [Environment]::NewLine + (Format-PayloadConflicts $active.Comparison))
    }
}

function Invoke-InstallerOperation {
    # Runs one operation under the installer lock and maps failures to exit code 1.
    param([Parameter(Mandatory = $true)][scriptblock]$Operation)

    $mutex = $null
    $owned = $false
    try {
        Assert-NotElevated
        $mutex = New-Object System.Threading.Mutex ($false, $script:InstallerMutexName)
        try {
            $owned = $mutex.WaitOne(0)
        }
        catch [System.Threading.AbandonedMutexException] {
            $owned = $true
        }

        if (-not $owned) {
            Stop-Installation 'Another System Resource Monitor installation script is running.'
        }

        $null = & $Operation
        return 0
    }
    catch {
        [Console]::Error.WriteLine('Error: ' + $_.Exception.Message)
        return 1
    }
    finally {
        if ($owned) {
            $mutex.ReleaseMutex()
        }

        if ($null -ne $mutex) {
            $mutex.Dispose()
        }
    }
}

function Invoke-Install {
    param([Parameter(Mandatory = $true)][string]$PackageDirectory)

    $layout = Get-InstallLayout
    $candidate = Get-CandidatePayload -PackageDirectory $PackageDirectory
    if (-not [IO.Directory]::Exists($layout.ProgramsDirectory)) {
        $null = [IO.Directory]::CreateDirectory($layout.ProgramsDirectory)
    }

    $null = Assert-InstallationLocation $layout
    Resolve-InterruptedOperation $layout
    $inventory = Assert-InstallationLocation $layout
    if ($null -eq $inventory) {
        $stage = New-StagedPayload -Layout $layout -Candidate $candidate
        try {
            $null = Assert-InstallationLocation $layout
            if ([IO.Directory]::Exists($layout.ApplicationDirectory)) {
                Stop-Installation 'The application directory appeared during installation; it was left unchanged.'
            }

            Move-PayloadDirectory -Source $stage -Destination $layout.ApplicationDirectory
        }
        catch {
            $failure = $_
            Remove-StagingDirectory -Layout $layout -Path $stage
            throw $failure
        }

        try {
            Assert-ActivePayloadMatches -Layout $layout -Candidate $candidate
        }
        catch {
            $failure = $_
            $rejected = New-SiblingPath -Layout $layout -Prefix $script:StagePrefix
            Move-PayloadDirectory -Source $layout.ApplicationDirectory -Destination $rejected
            Remove-StagingDirectory -Layout $layout -Path $rejected
            throw $failure
        }

        Write-Step "Installed System Resource Monitor $($candidate.Manifest.SourceCommit.Substring(0, 7)) in '$($layout.ApplicationDirectory)'."
        Write-Step 'The bar was not started. Launch it from the installed folder or enable autostart.'
        return
    }

    $installed = Read-InstallManifest -Path $layout.ManifestPath -Root $layout.ApplicationDirectory
    $comparison = Compare-InstalledPayload -Root $layout.ApplicationDirectory -Manifest $installed -Inventory $inventory
    if (-not $comparison.IsExact) {
        Stop-Installation ("The installed files do not match their manifest; nothing was changed:" + [Environment]::NewLine + (Format-PayloadConflicts $comparison))
    }

    if (Test-ManifestEquivalent $installed $candidate.Manifest) {
        Write-Step "System Resource Monitor $($candidate.Manifest.SourceCommit.Substring(0, 7)) is already installed; nothing was changed."
        return
    }

    $stage = New-StagedPayload -Layout $layout -Candidate $candidate
    try {
        $null = Stop-InstalledBar $layout
        $inventory = Assert-InstallationLocation $layout
        $comparison = Compare-InstalledPayload -Root $layout.ApplicationDirectory -Manifest $installed -Inventory $inventory
        if (-not $comparison.IsExact) {
            Stop-Installation ("The installed files changed during the upgrade; nothing was replaced:" + [Environment]::NewLine + (Format-PayloadConflicts $comparison))
        }

        $previous = New-SiblingPath -Layout $layout -Prefix $script:PreviousPrefix
        Move-PayloadDirectory -Source $layout.ApplicationDirectory -Destination $previous
    }
    catch {
        $failure = $_
        Remove-StagingDirectory -Layout $layout -Path $stage
        throw $failure
    }

    try {
        Move-PayloadDirectory -Source $stage -Destination $layout.ApplicationDirectory
        Assert-ActivePayloadMatches -Layout $layout -Candidate $candidate
    }
    catch {
        $failure = $_
        if ([IO.Directory]::Exists($layout.ApplicationDirectory)) {
            $rejected = New-SiblingPath -Layout $layout -Prefix $script:StagePrefix
            Move-PayloadDirectory -Source $layout.ApplicationDirectory -Destination $rejected
            $stage = $rejected
        }

        Move-PayloadDirectory -Source $previous -Destination $layout.ApplicationDirectory
        Remove-StagingDirectory -Layout $layout -Path $stage
        Write-Step 'The upgrade failed; the previous installation was restored.'
        throw $failure
    }

    $oldInventory = Get-SecureTreeInventory -Root $previous -UserSid $layout.UserSid
    $leftovers = Remove-ManifestScopedFiles -Root $previous -Manifest $installed -Inventory $oldInventory
    if ($leftovers.Count -gt 0) {
        Write-Step ("Left files of the previous installation in '$previous':" + [Environment]::NewLine + (($leftovers | ForEach-Object { "  $_" }) -join [Environment]::NewLine))
    }

    Write-Step "Upgraded System Resource Monitor from $($installed.SourceCommit.Substring(0, 7)) to $($candidate.Manifest.SourceCommit.Substring(0, 7)). Settings and the autostart choice were kept."
    Write-Step 'The bar was not started. Launch it from the installed folder or sign in again with autostart enabled.'
}

function Assert-OwnedInstallation {
    # Requires a safe location and an installed payload that exactly matches its manifest.
    param($Layout)
    $inventory = Assert-InstallationLocation $Layout
    if ($null -eq $inventory) {
        Stop-Installation 'System Resource Monitor is not installed for this user.'
    }

    $manifest = Read-InstallManifest -Path $Layout.ManifestPath -Root $Layout.ApplicationDirectory
    $comparison = Compare-InstalledPayload -Root $Layout.ApplicationDirectory -Manifest $manifest -Inventory $inventory
    if (-not $comparison.IsExact) {
        Stop-Installation ("The installed files do not match their manifest:" + [Environment]::NewLine + (Format-PayloadConflicts $comparison))
    }
}

function Invoke-SetAutostart {
    param([Parameter(Mandatory = $true)][bool]$Enabled)

    $layout = Get-InstallLayout
    $null = Assert-InstallationLocation $layout
    Resolve-InterruptedOperation $layout
    $state = Get-RunValueState $layout
    if (-not $Enabled -and $state -eq 'Absent') {
        Write-Step 'Autostart is already disabled.'
        return
    }

    if (-not [IO.Directory]::Exists($layout.ApplicationDirectory)) {
        if ($state -eq 'Foreign' -or $state -eq 'Owned') {
            Stop-Installation "The Run value '$($script:RunValueName)' exists, but no installation proves that it belongs to this app; it was left unchanged."
        }

        Stop-Installation 'System Resource Monitor is not installed for this user.'
    }

    Assert-OwnedInstallation $layout
    if ($state -eq 'Foreign') {
        Stop-Installation "The Run value '$($script:RunValueName)' does not match the installed executable; it was left unchanged."
    }

    if ($Enabled) {
        if ($state -eq 'Owned') {
            Write-Step 'Autostart is already enabled.'
            return
        }

        $null = Assert-InstallationLocation $layout
        Set-OwnedRunValue $layout
        Write-Step 'Enabled autostart. The bar starts after the next sign-in if Windows Startup Apps allows it.'
        return
    }

    $null = Assert-InstallationLocation $layout
    Remove-OwnedRunValue $layout
    Write-Step 'Disabled autostart. The installed app and its settings were kept.'
}

function Invoke-Uninstall {
    $layout = Get-InstallLayout
    $null = Assert-InstallationLocation $layout
    Resolve-InterruptedOperation $layout
    $inventory = Assert-InstallationLocation $layout
    $runState = Get-RunValueState $layout
    if ($null -eq $inventory) {
        if ($runState -ne 'Absent') {
            Stop-Installation "System Resource Monitor is not installed, but the Run value '$($script:RunValueName)' exists; it was left unchanged."
        }

        Write-Step 'System Resource Monitor is not installed for this user; nothing was changed.'
        return
    }

    $manifest = Read-InstallManifest -Path $layout.ManifestPath -Root $layout.ApplicationDirectory
    $null = Stop-InstalledBar $layout
    $inventory = Assert-InstallationLocation $layout
    $comparison = Compare-InstalledPayload -Root $layout.ApplicationDirectory -Manifest $manifest -Inventory $inventory
    $ownershipProven = ($comparison.Changed.Count -eq 0 -and $comparison.Unexpected.Count -eq 0)
    $runLeft = $false
    if ($runState -eq 'Owned' -and $ownershipProven) {
        Remove-OwnedRunValue $layout
        Write-Step 'Removed the autostart Run value.'
    }
    elseif ($runState -ne 'Absent') {
        $runLeft = $true
    }

    $null = Assert-InstallationLocation $layout
    $leftovers = Remove-ManifestScopedFiles -Root $layout.ApplicationDirectory -Manifest $manifest -Inventory $inventory
    $messages = New-Object System.Collections.Generic.List[string]
    if ($runLeft) {
        $messages.Add("The Run value '$($script:RunValueName)' was left unchanged because the installation could not prove that it owns it.")
    }

    if ($leftovers.Count -gt 0) {
        $messages.Add("These files were left in '$($layout.ApplicationDirectory)':" + [Environment]::NewLine + (($leftovers | ForEach-Object { "  $_" }) -join [Environment]::NewLine))
    }

    if ($messages.Count -gt 0) {
        Stop-Installation ("Uninstall removed only the unchanged installed files. " + ($messages -join [Environment]::NewLine))
    }

    Write-Step "Uninstalled System Resource Monitor. Settings in '$(Join-Path $layout.LocalAppData 'SystemResourceMonitor')' were kept."
}

function Invoke-SrmInstall {
    param([Parameter(Mandatory = $true)][string]$PackageDirectory)
    return Invoke-InstallerOperation { Invoke-Install -PackageDirectory $PackageDirectory }
}

function Invoke-SrmSetAutostart {
    param([Parameter(Mandatory = $true)][bool]$Enabled)
    return Invoke-InstallerOperation { Invoke-SetAutostart -Enabled $Enabled }
}

function Invoke-SrmUninstall {
    return Invoke-InstallerOperation { Invoke-Uninstall }
}

function New-SrmCandidateManifest {
    # Packaging helper: writes the manifest for a payload folder after validating every relative path.
    param(
        [Parameter(Mandatory = $true)][string]$PayloadDirectory,
        [Parameter(Mandatory = $true)][string]$SourceCommit,
        [Parameter(Mandatory = $true)][string]$OutputPath
    )

    $payload = [IO.Path]::GetFullPath($PayloadDirectory)
    $files = New-Object System.Collections.Generic.List[object]
    foreach ($entry in [IO.Directory]::GetFiles($payload, '*', [IO.SearchOption]::AllDirectories)) {
        $relativePath = $entry.Substring($payload.Length + 1).Replace('\', '/')
        if (-not (Test-ManifestRelativePath $relativePath)) {
            Stop-Installation "The payload file '$relativePath' cannot be listed in a manifest."
        }

        $files.Add([pscustomobject]@{ RelativePath = $relativePath; Sha256 = Get-FileSha256 $entry })
    }

    Write-InstallManifest -Path $OutputPath -Manifest ([pscustomobject]@{ SourceCommit = $SourceCommit; Files = $files })
    $null = Read-InstallManifest -Path $OutputPath -Root $payload
}

Export-ModuleMember -Function Invoke-SrmInstall, Invoke-SrmSetAutostart, Invoke-SrmUninstall, New-SrmCandidateManifest
