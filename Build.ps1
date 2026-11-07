param([string]$RuntimeFrom)
$ErrorActionPreference='Stop'
Expand-Archive -LiteralPath (Join-Path $PSScriptRoot 'assets\engine-resources.zip') -DestinationPath (Join-Path $PSScriptRoot 'app\Edge') -Force
& (Join-Path $PSScriptRoot 'app\Build.ps1') -RuntimeFrom $RuntimeFrom
