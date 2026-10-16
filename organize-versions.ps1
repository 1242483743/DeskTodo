param([string]$Version, [switch]$Preview)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath $PSScriptRoot).Path.TrimEnd('\')
$taskRootPrefix = $taskRoot + '\'
$taskOldName = -join @([char]0x65E7,[char]0x7248,[char]0x672C)
$taskBuildName = -join @([char]0x6784,[char]0x5EFA,[char]0x8F93,[char]0x51FA)
$taskLegacyName = -join @([char]0x684C,[char]0x9762,[char]0x5F85,[char]0x529E)
$taskGithubName = 'GitHub' + (-join @([char]0x53D1,[char]0x5E03))
$taskOld = Join-Path $taskRoot $taskOldName
$taskRelease = Join-Path $taskRoot 'release'

if ([string]::IsNullOrWhiteSpace($Version)) {
    $taskInfo = Get-Content -LiteralPath (Join-Path $taskRoot 'src\AssemblyInfo.cs') -Raw
    $taskMatch = [regex]::Match($taskInfo, 'AssemblyVersion\("([0-9]+\.[0-9]+)\.')
    if (-not $taskMatch.Success) { throw 'Cannot determine the current version.' }
    $Version = $taskMatch.Groups[1].Value
}
if ($Version -notmatch '^[0-9]+\.[0-9]+$') { throw 'Version must look like 1.26.' }

function Assert-ProjectPath([string]$taskPath) {
    $taskFull = [IO.Path]::GetFullPath($taskPath).TrimEnd('\')
    if (-not $taskFull.StartsWith($taskRootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw ('Refusing to move outside the project: ' + $taskFull)
    }
    return $taskFull
}

function Move-ToArchive([string]$taskSource, [string]$taskSubfolder) {
    $taskSource = Assert-ProjectPath ((Resolve-Path -LiteralPath $taskSource).Path)
    $taskDestinationDirectory = Assert-ProjectPath (Join-Path $taskOld $taskSubfolder)
    $taskDestination = Assert-ProjectPath (Join-Path $taskDestinationDirectory (Split-Path -Leaf $taskSource))
    if (Test-Path -LiteralPath $taskDestination) { throw ('Archive target already exists: ' + $taskDestination) }
    if ($Preview) { Write-Output ('Would archive: ' + $taskSource + ' -> ' + $taskDestination); return }
    New-Item -ItemType Directory -Path $taskDestinationDirectory -Force | Out-Null
    Move-Item -LiteralPath $taskSource -Destination $taskDestination
    Write-Output ('Archived: ' + $taskSource + ' -> ' + $taskDestination)
}

function Get-ArtifactVersion([string]$taskName) {
    if ($taskName -match '^DeskTodo-Setup-v([0-9]+(?:\.[0-9]+)*)\.exe$') { return $Matches[1] }
    if ($taskName -match '^DeskTodo-v([0-9]+(?:\.[0-9]+)*)\.zip$') { return $Matches[1] }
    if ($taskName.StartsWith($taskLegacyName + '-v') -and $taskName -match '-v([0-9]+(?:\.[0-9]+)*)\.zip$') { return $Matches[1] }
    return $null
}

function Get-OutputVersion([string]$taskDirectory) {
    $taskExe = Get-ChildItem -LiteralPath $taskDirectory -Filter '*.exe' -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $taskExe) { return $null }
    $taskInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($taskExe.FullName)
    if ($taskInfo.FileMajorPart -le 0 -or $taskInfo.FileMinorPart -lt 0) { return $null }
    return ($taskInfo.FileMajorPart.ToString() + '.' + $taskInfo.FileMinorPart.ToString())
}

# Keep all old copies intact, including same-named files from the root and release.
foreach ($taskFile in @(Get-ChildItem -LiteralPath $taskRoot -File)) {
    $taskFileVersion = Get-ArtifactVersion $taskFile.Name
    if ($null -ne $taskFileVersion -and $taskFileVersion -ne $Version) {
        $taskKind = if ($taskFile.Extension -eq '.exe') { 'installers' } else { 'portable' }
        Move-ToArchive $taskFile.FullName $taskKind
    }
}
if (Test-Path -LiteralPath $taskRelease) {
    foreach ($taskFile in @(Get-ChildItem -LiteralPath $taskRelease -File)) {
        $taskFileVersion = Get-ArtifactVersion $taskFile.Name
        if ($null -ne $taskFileVersion -and $taskFileVersion -ne $Version) {
            Move-ToArchive $taskFile.FullName 'release-copies'
        }
    }
    foreach ($taskName in @(('DeskTodo-Setup-v{0}.exe' -f $Version), ('DeskTodo-v{0}.zip' -f $Version))) {
        $taskSource = Join-Path $taskRelease $taskName
        if (-not (Test-Path -LiteralPath $taskSource)) { continue }
        $taskDestination = Assert-ProjectPath (Join-Path $taskRoot $taskName)
        if (Test-Path -LiteralPath $taskDestination) { throw ('Current artifact already exists: ' + $taskDestination) }
        if ($Preview) { Write-Output ('Would show current: ' + $taskSource + ' -> ' + $taskDestination); continue }
        Move-Item -LiteralPath (Assert-ProjectPath $taskSource) -Destination $taskDestination
        Write-Output ('Current: ' + $taskDestination)
    }
}

foreach ($taskDirectory in @(Get-ChildItem -LiteralPath $taskRoot -Directory)) {
    if ($taskDirectory.Name -match '^build-v([0-9]+(?:\.[0-9]+)*)(?:-|$)' -and $Matches[1] -ne $Version) {
        Move-ToArchive $taskDirectory.FullName $taskBuildName
    }
    elseif ($taskDirectory.Name -eq 'dist' -or $taskDirectory.Name -eq 'staging' -or
        $taskDirectory.Name -eq $taskGithubName -or
        $taskDirectory.Name -like 'dist-v*') {
        $taskOutputVersion = Get-OutputVersion $taskDirectory.FullName
        if ($null -ne $taskOutputVersion -and $taskOutputVersion -ne $Version) {
            Move-ToArchive $taskDirectory.FullName $taskBuildName
        }
    }
}
Write-Output ('Current version at project root: ' + $Version)
