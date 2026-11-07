param([switch]$VerifyOnly)
$ErrorActionPreference = 'Stop'
$target = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
$marker = Join-Path $target 'package-marker.json'
if (-not (Test-Path -LiteralPath $marker)) { throw 'Package marker missing; nothing was removed.' }
$package = Get-Content -LiteralPath $marker -Raw -Encoding UTF8 | ConvertFrom-Json
if ($package.product -ne 'Qingjian-V2' -or (Split-Path $target -Leaf) -ne 'Qingjian-V2') { throw 'Unexpected package location; nothing was removed.' }
# Validate the final absolute path and all descendants before any recursive deletion.
if ($target.Length -lt 20 -or [IO.Path]::GetPathRoot($target).TrimEnd('\') -eq $target) { throw 'Unsafe target; nothing was removed.' }
$all = @((Get-Item -LiteralPath $target -Force)) + @(Get-ChildItem -LiteralPath $target -Recurse -Force)
if ($all | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Reparse point found; nothing was removed.' }
foreach ($item in $all) { if (-not ($item.FullName -eq $target -or $item.FullName.StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase))) { throw 'Unsafe descendant; nothing was removed.' } }
if ($VerifyOnly) { Write-Output ('SAFE: ' + $target); return }
$config = $null
if (Test-Path -LiteralPath (Join-Path $target 'local-config.json')) { $config = Get-Content -LiteralPath (Join-Path $target 'local-config.json') -Raw -Encoding UTF8 | ConvertFrom-Json }
if ($config) { try { $event = [Threading.EventWaitHandle]::OpenExisting('Local\' + $config.pipe + '_exit'); $event.Set() | Out-Null; $event.Dispose() } catch {} }
Start-Sleep -Milliseconds 1200
Get-CimInstance Win32_Process -Filter "Name = '清简.exe' OR Name = 'Qingjian.exe' OR Name = 'Qingjian040.exe' OR Name = 'Qingjian050.exe' OR Name = 'Qingjian051.exe' OR Name = 'Qingjian052.exe' OR Name = 'Qingjian053.exe' OR Name = 'Qingjian054.exe' OR Name = 'QingjianLocal.exe' OR Name = 'QingjianLocal2.exe' OR Name = 'QingjianLocal3.exe' OR Name = 'QingjianExpandedPreview.exe' OR Name = 'QingjianExpandedPreview2.exe' OR Name = 'QingjianBridge.exe' OR Name = 'QingjianBridge051.exe' OR Name = 'QingjianBridge052.exe' OR Name = 'QingjianBridge053.exe' OR Name = 'QingjianBridge054.exe' OR Name = 'node.exe' OR Name = 'llama-server.exe'" | Where-Object {
    $_.ExecutablePath -and ([IO.Path]::GetFullPath($_.ExecutablePath)).StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase)
} | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
$key = 'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.qingjian.local'
if (Test-Path -LiteralPath $key) {
    $registered = (Get-Item -LiteralPath $key).GetValue('')
    if ($registered -and [IO.Path]::GetFullPath($registered) -eq (Join-Path $target 'com.qingjian.local.json')) { Remove-Item -LiteralPath $key -Force }
}
$executables = @('清简.exe','Qingjian.exe','Qingjian040.exe','Qingjian050.exe','Qingjian051.exe','Qingjian052.exe','Qingjian053.exe','Qingjian054.exe','QingjianLocal.exe','QingjianLocal2.exe','QingjianLocal3.exe','QingjianExpandedPreview.exe','QingjianExpandedPreview2.exe') | ForEach-Object { Join-Path $target $_ }
foreach ($name in @('清简.lnk','启动清简助手.lnk')) {
    $link = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) $name
    if (Test-Path -LiteralPath $link) {
        $shell = New-Object -ComObject WScript.Shell
        if ($executables -contains $shell.CreateShortcut($link).TargetPath) { Remove-Item -LiteralPath $link -Force }
    }
}
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (Test-Path -LiteralPath $runKey) {
    $value = (Get-Item -LiteralPath $runKey).GetValue('QingjianAssistant')
    $ownedCommands = $executables | ForEach-Object { '"' + $_ + '"' }
    if ($ownedCommands -contains $value) { Remove-ItemProperty -LiteralPath $runKey -Name 'QingjianAssistant' -ErrorAction SilentlyContinue }
}
Set-Location -LiteralPath ([IO.Path]::GetTempPath())
Remove-Item -LiteralPath $target -Recurse -Force
Write-Host '清简已移除。请在 edge://extensions 移除清简扩展以完成卸载。'
