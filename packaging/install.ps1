#Requires -Version 5.1
<#
.SYNOPSIS
Installs or upgrades System Resource Monitor for the current user.

.DESCRIPTION
Copies the package's app folder to %LocalAppData%\Programs\SystemResourceMonitor after verifying it against
install-manifest.json. An existing installation is upgraded only when its files still match its own manifest.
The script never starts the bar, never changes autostart, and keeps settings. Exit code 0 means success.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SystemResourceMonitor.Install.psm1') -Force
exit (Invoke-SrmInstall -PackageDirectory $PSScriptRoot)
