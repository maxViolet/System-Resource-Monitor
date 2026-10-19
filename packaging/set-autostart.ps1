#Requires -Version 5.1
<#
.SYNOPSIS
Enables or disables starting System Resource Monitor at sign-in for the current user.

.DESCRIPTION
-Enabled $true writes the HKCU Run value 'SystemResourceMonitor' with the quoted installed executable path.
-Enabled $false removes that value only when it still matches. Neither operation changes the installed files,
the settings, or the Windows Startup Apps approval state. Exit code 0 means success.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [bool]$Enabled
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SystemResourceMonitor.Install.psm1') -Force
exit (Invoke-SrmSetAutostart -Enabled $Enabled)
