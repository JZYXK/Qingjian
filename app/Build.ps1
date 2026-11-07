param([string]$RuntimeFrom)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PSScriptRoot)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Requires Windows x64 with .NET Framework 4.8.' }
$wpf = Join-Path (Split-Path $compiler) 'WPF'
Push-Location -LiteralPath $root
try {
    & $compiler /nologo /target:winexe /platform:x64 /out:QingjianLocal3.exe /win32icon:assets\qingjian.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:Microsoft.CSharp.dll "/r:$wpf\UIAutomationClient.dll" "/r:$wpf\UIAutomationTypes.dll" "/r:$wpf\WindowsBase.dll" Common.cs Assistant.cs FallbackWindow.cs SelectionWorker.cs WordSelection.cs
    if ($LASTEXITCODE -ne 0) { throw 'Assistant build failed.' }
    & $compiler /nologo /target:exe /platform:x64 /out:QingjianBridge054.exe /r:System.Web.Extensions.dll Common.cs Bridge.cs
    if ($LASTEXITCODE -ne 0) { throw 'Bridge build failed.' }
    Copy-Item -LiteralPath QingjianLocal3.exe -Destination 清简.exe -Force
    if ($RuntimeFrom) { $runtimeTarget = Join-Path $root 'runtime'; New-Item -ItemType Directory -Path $runtimeTarget -Force | Out-Null; Get-ChildItem -LiteralPath ([IO.Path]::GetFullPath($RuntimeFrom)) | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $runtimeTarget -Recurse -Force } }
    Write-Output 'Built. Keep the Edge and runtime directories beside the executables.'
} finally { Pop-Location }
