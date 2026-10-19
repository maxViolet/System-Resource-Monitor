#Requires -Version 5.1
<#
.SYNOPSIS
Uninstalls System Resource Monitor for the current user.

.DESCRIPTION
Closes the installed bar, removes the autostart Run value it owns, and removes the unchanged installed files
listed in install-manifest.json. Changed or unexpected files stay in place and are reported. Settings in
%LocalAppData%\SystemResourceMonitor are always kept. Exit code 0 means success.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SystemResourceMonitor.Install.psm1') -Force
exit (Invoke-SrmUninstall)
