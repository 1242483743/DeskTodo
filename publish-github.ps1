[CmdletBinding()]
param(
    [switch]$PrepareOnly,
    [string]$SourceCommit,
    [string]$Repository = '1242483743/DeskTodo'
)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$versionText = Get-Content -LiteralPath (Join-Path $projectRoot 'src\AssemblyInfo.cs') -Raw
if ($versionText -notmatch 'AssemblyVersion\("(\d+\.\d+)\.0\.0"\)') { throw 'Cannot read release version.' }
$tag = 'v' + $Matches[1]
$names = @("DeskTodo-Setup-$tag.exe", "DeskTodo-$tag.zip")
$notes = Join-Path $projectRoot "docs\releases\$tag.md"
$manifest = Join-Path $projectRoot 'docs\releases\SHA256SUMS.txt'
if (!(Test-Path -LiteralPath $notes)) { throw "Release notes missing: $notes" }
$expected = @{}
foreach ($line in Get-Content -LiteralPath $manifest) {
    if ($line -match '^([A-Fa-f0-9]{64})\s+(.+)$') { $expected[$Matches[2].Trim()] = $Matches[1] }
}
$stage = Join-Path $projectRoot "staging\github-release-$tag-assets"
$null = New-Item -ItemType Directory -Path $stage -Force
foreach ($name in $names) {
    $source = Join-Path $projectRoot $name
    if (!(Test-Path -LiteralPath $source)) { throw "Release artifact missing: $source" }
    $hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    if (!$expected.ContainsKey($name) -or $hash -ne $expected[$name]) { throw "Checksum mismatch: $name" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $stage $name) -Force
}
Copy-Item -LiteralPath $manifest -Destination (Join-Path $stage 'SHA256SUMS.txt') -Force
$uploadNames = @($names) + @('SHA256SUMS.txt')
$stagedNames = @(Get-ChildItem -LiteralPath $stage -File | ForEach-Object Name)
if (@(Compare-Object $uploadNames $stagedNames).Count -ne 0) { throw 'Staging contains unexpected files; inspect manually.' }
Write-Output "Validated $tag. Upload exactly these files from $stage"
if ($PrepareOnly) { return }
if ($SourceCommit -notmatch '^[a-fA-F0-9]{40}$') { throw 'Pass the reviewed, already-pushed source commit using -SourceCommit (40 hex characters).' }
if (!(Get-Command gh -ErrorAction SilentlyContinue)) { throw 'GitHub CLI unavailable. Publish the staged files with the authenticated GitHub connector/browser. Do not create credentials automatically.' }
& gh auth status
if ($LASTEXITCODE -ne 0) { throw 'GitHub CLI is not authenticated.' }
$remoteCommit = & gh api "repos/$Repository/commits/main" --jq '.sha'
if ($LASTEXITCODE -ne 0 -or $remoteCommit.Trim() -ne $SourceCommit) { throw 'SourceCommit must equal the published main branch head.' }
$existingJson = & gh release view $tag --repo $Repository --json isDraft,tagName 2>$null
if ($LASTEXITCODE -eq 0) { throw 'Release already exists. Inspect it manually; this script never replaces published artifacts or existing drafts.' }
$files = @($uploadNames | ForEach-Object { Join-Path $stage $_ })
& gh release create $tag @files --repo $Repository --target $SourceCommit --title "DeskTodo $tag" --notes-file $notes --draft
if ($LASTEXITCODE -ne 0) { throw 'Draft creation/upload failed. Inspect GitHub; publication is incomplete.' }
$release = (& gh api "repos/$Repository/releases/tags/$tag" | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0 -or !$release.draft) { throw 'Cannot verify uploaded draft.' }
function Assert-ReleaseAssets($releaseInfo) {
    if (@($releaseInfo.assets).Count -ne 3) { throw 'Expected exactly three release assets.' }
    foreach ($name in $uploadNames) {
        $asset = @($releaseInfo.assets | Where-Object name -eq $name)
        $file = Get-Item -LiteralPath (Join-Path $stage $name)
        $digest = 'sha256:' + (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($asset.Count -ne 1 -or $asset[0].state -ne 'uploaded' -or $asset[0].size -ne $file.Length -or $asset[0].digest -ne $digest) { throw "Remote asset validation failed: $name" }
    }
}
Assert-ReleaseAssets $release
& gh release edit $tag --repo $Repository --draft=false --latest
if ($LASTEXITCODE -ne 0) { throw 'Publishing failed; check the existing draft.' }
$latest = (& gh api "repos/$Repository/releases/latest" | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0 -or $latest.tag_name -ne $tag -or $latest.draft -or $latest.prerelease) { throw 'Latest stable release verification failed.' }
Assert-ReleaseAssets $latest
Write-Output "Published and verified: $($latest.html_url)"
