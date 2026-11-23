# Faz3a fork: publish a new version of ترجمان (Turjuman) to GitHub releases, which installed copies update from.
# Usage (from the repository root):  .\scripts\release-turjuman.ps1 -Version 1.0.2 -Notes "ما الجديد"
# Needs: the x64 .NET 10 SDK, vpk 1.2.161 (dotnet tool), gh signed in to the account that owns Fork.Repository,
# and windows\Directory.Build.local.props with ArabicModelDir (the offline model is copied into the package).
# dist\release\Releases must keep the previous version's full package: vpk builds the small delta update from it.
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Notes = "",
    [string]$Vpk = "vpk",
    [string]$Repository = "chfcfh/turjuman"
)
$ErrorActionPreference = "Stop"
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
$app = "dist\release\$Version"
$out = "dist\release\Releases"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

& $dotnet publish windows\src\CouchSubs.App -c Release -r win-x64 --self-contained -o $app -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "publish failed" }
if (-not (Test-Path "$app\models\opus-mt-en-ar\encoder.onnx")) { throw "the offline model is missing from $app (ArabicModelDir)" }
Copy-Item LICENSE, NOTICE, FORK-NOTICE.md, THIRD-PARTY-NOTICES.md $app

& $Vpk pack --packId Turjuman --packVersion $Version --packDir $app --mainExe Faz3aSubs.exe --packTitle "ترجمان" `
    --packAuthors Faz3a --icon windows\src\CouchSubs.App\Assets\CouchSubs.ico --outputDir $out
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

$files = @("$out\Turjuman-$Version-full.nupkg", "$out\releases.win.json", "$out\RELEASES", "$out\assets.win.json",
           "$out\Turjuman-win-Portable.zip")
if (Test-Path "$out\Turjuman-$Version-delta.nupkg") { $files += "$out\Turjuman-$Version-delta.nupkg" }
$body = "$Notes`n`n**التشغيل:** نزّل Turjuman-win-Portable.zip، فك الضغط في أي مجلد، وشغّل «ترجمان.exe». النسخ الموجودة تتحدث لحالها.`n`nModified version of CouchSubs by Sinan Yorulmaz (AGPL-3.0). Source: this repository."
gh release create "v$Version" @files --repo $Repository --title "ترجمان $Version" --notes $body
if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }
Write-Host "Released $Version. Installed copies find it at start-up, every 6 hours, or with «تحقق الآن من وجود تحديث»."
