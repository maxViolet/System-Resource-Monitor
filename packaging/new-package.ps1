#Requires -Version 5.1
<#
.SYNOPSIS
Builds the per-user installation package from a self-contained Desktop publish folder.

.DESCRIPTION
Run after the locked self-contained publish command from a clean source tree. The package contains the
installation scripts, their README, install-manifest.json, and the published folder as 'app'. The manifest
records the current Git commit and the SHA-256 of every payload file.
#>
[CmdletBinding()]
param(
    [string]$PublishDirectory,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $PublishDirectory) {
    $PublishDirectory = Join-Path $repository 'src\system-resources\SystemResourceMonitor.Desktop\bin\Release\net10.0-windows\win-x64\publish'
}

$publish = [IO.Path]::GetFullPath($PublishDirectory)
if (-not (Test-Path -LiteralPath (Join-Path $publish 'SystemResourceMonitor.Desktop.exe') -PathType Leaf)) {
    throw "'$publish' does not contain SystemResourceMonitor.Desktop.exe. Run the self-contained publish command first."
}

$commit = (& git -C $repository rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -cnotmatch '^[0-9a-f]{40}$') {
    throw 'Cannot read the source commit.'
}

# Publish inputs only; documentation and packaging scripts do not change the payload.
$publishInputs = @('src', 'Directory.Build.props', 'Directory.Packages.props', 'global.json', 'SystemResourceMonitor.slnx', 'README.md')
$dirty = @(& git -C $repository status --porcelain --untracked-files=all -- $publishInputs)
if ($LASTEXITCODE -ne 0) {
    throw 'Cannot read the source tree status.'
}

if ($dirty.Count -gt 0) {
    throw ("Publish inputs differ from commit $commit; commit or discard them and publish again:" + [Environment]::NewLine + ($dirty -join [Environment]::NewLine))
}

$prohibited = @(Get-ChildItem -LiteralPath $publish -Recurse -File | Where-Object {
        $_.Name -match '(?i)testhost|tests|xunit|fixture|testing|scripted' -or $_.Name -match '(?i)^appsettings\.Development\.json$'
    })
if ($prohibited.Count -gt 0) {
    throw ("The publish folder contains test or development files:" + [Environment]::NewLine + (($prohibited | ForEach-Object { $_.FullName.Substring($publish.Length + 1) }) -join [Environment]::NewLine))
}

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repository ('artifacts\SystemResourceMonitor-' + $commit.Substring(0, 7))
}

$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) {
    throw "'$output' already exists."
}

$payload = Join-Path $output 'app'
$null = New-Item -ItemType Directory -Path $payload
Copy-Item -Path (Join-Path $publish '*') -Destination $payload -Recurse
foreach ($name in @('install.ps1', 'set-autostart.ps1', 'uninstall.ps1', 'SystemResourceMonitor.Install.psm1', 'README.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $output $name)
}

Import-Module (Join-Path $PSScriptRoot 'SystemResourceMonitor.Install.psm1') -Force
New-SrmCandidateManifest -PayloadDirectory $payload -SourceCommit $commit -OutputPath (Join-Path $output 'install-manifest.json')

$executableHash = (Get-FileHash -LiteralPath (Join-Path $payload 'SystemResourceMonitor.Desktop.exe') -Algorithm SHA256).Hash
$fileCount = @(Get-ChildItem -LiteralPath $payload -Recurse -File).Count
Write-Host "Package: $output"
Write-Host "Source commit: $commit"
Write-Host "Payload files: $fileCount"
Write-Host "SystemResourceMonitor.Desktop.exe SHA-256: $executableHash"
