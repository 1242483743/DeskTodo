param([switch]$Test, [string]$OutputDirectory = 'dist')
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskFramework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$taskCompiler = Join-Path $taskFramework 'csc.exe'
if (-not (Test-Path -LiteralPath $taskCompiler)) { throw '.NET Framework 4.8 C# compiler was not found.' }
$taskOutput = Join-Path $taskRoot $OutputDirectory
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskAppIconName = (-join @(0x5F85,0x529E,0x8F6F,0x4EF6,0x56FE,0x6807 | ForEach-Object { [char]$_ })) + '.ico'
$taskInternalIconDirectory = (-join @(0x56FE,0x6807 | ForEach-Object { [char]$_ }))
$taskInternalIconName = (-join @(0x5F85,0x529E,0x8F6F,0x4EF6,0x5185,0x56FE,0x6807 | ForEach-Object { [char]$_ })) + '.png'
$taskIcon = Join-Path (Join-Path $taskRoot $taskInternalIconDirectory) $taskAppIconName
$taskInternalIcon = Join-Path (Join-Path $taskRoot $taskInternalIconDirectory) $taskInternalIconName
if (-not (Test-Path -LiteralPath $taskIcon)) { throw 'The application icon was not found.' }
if (-not (Test-Path -LiteralPath $taskInternalIcon)) { throw 'The internal UI icon was not found.' }
$taskReadme = Get-ChildItem -LiteralPath $taskRoot -Filter '*.md' -File | Where-Object { $_.BaseName.Length -eq 4 } | Select-Object -First 1
if (-not $taskReadme) { throw 'The user guide was not found.' }
$taskRefs = @('System.dll', 'System.Core.dll', 'System.Runtime.Serialization.dll', 'System.Xaml.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'WPF\WindowsBase.dll', 'WPF\PresentationCore.dll', 'WPF\PresentationFramework.dll')
$taskArguments = @('/nologo', '/target:winexe', '/main:DesktopTodo.Program', '/platform:x64', '/optimize+', '/utf8output', '/warn:4', '/codepage:65001', ('/out:' + (Join-Path $taskOutput 'DeskTodo.exe')), ('/win32manifest:' + (Join-Path $taskRoot 'src\app.manifest')), ('/win32icon:' + $taskIcon), ('/resource:' + $taskInternalIcon + ',DesktopTodo.InternalIcon.png'), ('/resource:' + (Join-Path $taskRoot 'src\MainWindow.xaml') + ',DesktopTodo.MainWindow.xaml'), ('/resource:' + (Join-Path $taskRoot 'src\ReminderWindow.xaml') + ',DesktopTodo.ReminderWindow.xaml'))
foreach ($taskRef in $taskRefs) { $taskArguments += '/reference:' + (Join-Path $taskFramework $taskRef) }
$taskArguments += @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
& $taskCompiler @taskArguments
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Copy-Item -LiteralPath (Join-Path $taskRoot 'src\DesktopTodo.exe.config') -Destination (Join-Path $taskOutput 'DeskTodo.exe.config')
Copy-Item -LiteralPath $taskReadme.FullName -Destination (Join-Path $taskOutput $taskReadme.Name)
Write-Output ('Built: ' + (Join-Path $taskOutput 'DeskTodo.exe'))
if ($Test) {
    $taskTest = Start-Process -FilePath (Join-Path $taskOutput 'DeskTodo.exe') -ArgumentList @('--self-test', '--output-dir', ('"' + (Join-Path $taskRoot 'qa') + '"')) -WindowStyle Hidden -PassThru -Wait
    if ($taskTest.ExitCode -ne 0) { throw 'Self-test failed. See qa\test-report.txt.' }
    Get-Content -LiteralPath (Join-Path $taskRoot 'qa\test-report.txt') -Encoding UTF8
    foreach ($taskMode in @('Windowed', 'Desktop', 'Repair')) {
        $taskQaExe = Join-Path $taskRoot ('qa\DesktopTodo.' + $taskMode + '-QA.exe')
        $taskQaArgs = @(('/main:DesktopTodo.' + $taskMode + 'QaProgram'), ('/out:' + $taskQaExe))
        $taskQaArgs += @($taskArguments | Where-Object { -not $_.StartsWith('/out:') -and -not $_.StartsWith('/main:') })
        & $taskCompiler @taskQaArgs
        if ($LASTEXITCODE -ne 0) { throw 'UI test launcher compilation failed.' }
        Copy-Item -LiteralPath (Join-Path $taskRoot 'src\DesktopTodo.exe.config') -Destination ($taskQaExe + '.config')
    }
}
