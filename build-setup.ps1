param([string]$AppDirectory = 'build-v1.22', [string]$OutputDirectory = 'release', [switch]$Test)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskFramework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$taskCompiler = Join-Path $taskFramework 'csc.exe'
$taskAppDirectory = Join-Path $taskRoot $AppDirectory
$taskOutput = Join-Path $taskRoot $OutputDirectory
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskApp = Join-Path $taskAppDirectory 'DeskTodo.exe'
$taskVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($taskApp)
$taskInstaller = Join-Path $taskOutput ('DeskTodo-Setup-v' + $taskVersion.FileMajorPart + '.' + $taskVersion.FileMinorPart + '.exe')
$taskIconDirectory = -join @(0x56FE,0x6807 | ForEach-Object { [char]$_ })
$taskIconName = (-join @(0x5F85,0x529E,0x8F6F,0x4EF6,0x56FE,0x6807 | ForEach-Object { [char]$_ })) + '.ico'
$taskIcon = Join-Path (Join-Path $taskRoot $taskIconDirectory) $taskIconName
$taskReadme = Get-ChildItem -LiteralPath $taskAppDirectory -Filter '*.md' -File | Where-Object { $_.BaseName.Length -eq 4 } | Select-Object -First 1
$taskArguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/utf8output', '/codepage:65001', ('/out:' + $taskInstaller), ('/win32icon:' + $taskIcon), ('/win32manifest:' + (Join-Path $taskRoot 'src\app.manifest')), ('/resource:' + $taskApp + ',DeskTodo.Setup.App'), ('/resource:' + (Join-Path $taskAppDirectory 'DeskTodo.exe.config') + ',DeskTodo.Setup.Config'), ('/resource:' + $taskReadme.FullName + ',DeskTodo.Setup.Readme'), ('/resource:' + $taskIcon + ',DeskTodo.Setup.Icon'), ('/resource:' + (Join-Path $taskRoot 'installer\SetupWindow.xaml') + ',DeskTodo.Setup.Window'))
foreach ($taskRef in @('System.dll','System.Core.dll','System.Xaml.dll','System.Drawing.dll','System.Windows.Forms.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll')) { $taskArguments += '/reference:' + (Join-Path $taskFramework $taskRef) }
$taskArguments += @((Join-Path $taskRoot 'installer\Setup.cs'),(Join-Path $taskRoot 'src\AssemblyInfo.cs'))
& $taskCompiler @taskArguments
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
Write-Output ('Installer: ' + $taskInstaller)
if ($Test) {
    $taskQa = Join-Path $taskRoot 'qa'
    $taskProcess = Start-Process -FilePath $taskInstaller -ArgumentList @('--self-test',('"' + $taskQa + '"')) -WindowStyle Hidden -PassThru -Wait
    Get-Content -LiteralPath (Join-Path $taskQa 'setup-test-report.txt') -Encoding UTF8
    if ($taskProcess.ExitCode -ne 0) { throw 'Installer self-test failed.' }
}
