# Local release layout

Keep the current DeskTodo installer (`DeskTodo-Setup-vX.Y.exe`) and portable ZIP
(`DeskTodo-vX.Y.zip`) at the project root. Archive older installers, ZIPs,
and versioned build outputs under `旧版本/`; never delete or overwrite an
older artifact while organizing. Leave `备份/`, source, icons, and QA data alone.

After building and testing a new release in `release/`, preview with
`powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\organize-versions.ps1 -Preview`,
then run the same command without `-Preview` from the project root. The script reads the current version from
`src/AssemblyInfo.cs`. Verify both current files exist at the root afterward.

# GitHub publication is part of every user-requested software update

The user explicitly requests publishing every completed software update to
https://github.com/1242483743/DeskTodo. Do not stop at local packaging. After tests
pass, synchronize reviewed source to main, update README/release notes, and publish
a stable Release for the exact tested version. Upload its installer, portable ZIP
and SHA256SUMS.txt, and mark it Latest. Keep previous releases; never replace
published assets. Use publish-github.ps1 -PrepareOnly to validate/stage files.

Use an already authenticated GitHub CLI or the GitHub connector/browser. Never
extract browser credentials or create tokens automatically. Upload only reviewed
source/docs/demo images and release artifacts, never personal tasks/backups/logs.
Verify the public latest-release API returns the new tag and all three uploaded
assets with expected sizes and SHA-256 digests. Confirm the app's update endpoint
can read the release. Report publication as incomplete if any step is blocked;
never equate a local build with a published update. This standing request does not
authorize installing over a running app, changing security settings or scheduling
unrelated recurring jobs.
