param([string]$Destination = (Join-Path $PSScriptRoot 'out\release'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = [IO.Path]::GetFullPath($PSScriptRoot)
$built = Join-Path $root 'out\pcvr-1.4.3-localdata'
if (!(Test-Path -LiteralPath (Join-Path $built 'The Internet\TheInternet.dll'))) { throw 'Run build.ps1 first.' }
if (Test-Path -LiteralPath (Join-Path $built 'The Internet\BrowserData')) { throw 'Refusing to package BrowserData. Export a clean build, never an installed user profile.' }
New-Item -ItemType Directory -Force $Destination | Out-Null
$destinationRoot = (Resolve-Path -LiteralPath $Destination).Path
$snapshot = [IO.Path]::GetFullPath((Join-Path $destinationRoot 'PCVR-1.4.3-Local-Data-Source'))
if (!$snapshot.StartsWith($destinationRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($snapshot) -ne 'PCVR-1.4.3-Local-Data-Source') { throw 'Invalid generated source destination.' }
if (Test-Path -LiteralPath $snapshot) { Remove-Item -LiteralPath $snapshot -Recurse -Force }
New-Item -ItemType Directory -Force $snapshot | Out-Null
foreach($name in @('.gitignore','.gitattributes','manifest.json','runtime.json','README.md','PRIVACY.md','REVIEWER-NOTES.md','build.ps1','package.ps1')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination $snapshot }
New-Item -ItemType Directory -Force (Join-Path $snapshot 'src'),(Join-Path $snapshot 'tests'),(Join-Path $snapshot 'app') | Out-Null
Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' -File | Copy-Item -Destination (Join-Path $snapshot 'src')
Get-ChildItem -LiteralPath (Join-Path $root 'tests') -File | Where-Object { $_.Extension -in @('.cs','.csproj','.cjs','.props') } | Copy-Item -Destination (Join-Path $snapshot 'tests')
foreach($name in @('package.json','package-lock.json','main.js','preload.js','cleanup.js','start.html','filters.bin','THIRD-PARTY-NOTICES.md','browser.test.js','adblock.test.cjs','build-filters.cjs','runtime-dependencies.cjs','youtube-blocker.cjs','youtube-blocker.test.cjs','preload-base.js','build-preload.cjs')) { Copy-Item -LiteralPath (Join-Path $root ('app\'+$name)) -Destination (Join-Path $snapshot 'app') }
foreach($folder in @('assets','filter-sources')) { Copy-Item -LiteralPath (Join-Path $root ('app\'+$folder)) -Destination (Join-Path $snapshot 'app') -Recurse }
$dependencies = (& node (Join-Path $root 'app\runtime-dependencies.cjs')) | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Dependency closure resolution failed.' }
foreach($name in $dependencies) {
    $target = Join-Path $snapshot ('app\node_modules\'+$name)
    New-Item -ItemType Directory -Force (Split-Path -Parent $target) | Out-Null
    Copy-Item -LiteralPath (Join-Path $root ('app\node_modules\'+$name)) -Destination $target -Recurse
}
function Write-Archive([string]$inputRoot,[string]$outputPath) {
    $inputRoot = (Resolve-Path -LiteralPath $inputRoot).Path
    $stream = [IO.File]::Open($outputPath,[IO.FileMode]::Create)
    $zip = [IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach($file in Get-ChildItem -LiteralPath $inputRoot -Recurse -File) {
            $relative = $file.FullName.Substring($inputRoot.Length+1).Replace('\','/')
            if ($relative -match '(?i)(^|/)BrowserData(/|$)') { throw 'Refusing to include saved browser data in an archive.' }
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$file.FullName,$relative,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $zip.Dispose();$stream.Dispose() }
}
$modZip = Join-Path $destinationRoot 'The Internet Local Data v1.4.3.zip'
$sourceZip = Join-Path $destinationRoot 'The Internet Local Data v1.4.3 Source.zip'
Write-Archive $built $modZip
Write-Archive $snapshot $sourceZip
$checksums = foreach($file in @($modZip,$sourceZip)) { ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($file)) }
$checksums | Set-Content -LiteralPath (Join-Path $destinationRoot 'SHA256SUMS-1.4.3-Local-Data.txt')
Write-Output "Local Data release/source snapshot and versioned checksums written to $destinationRoot"
