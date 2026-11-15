param(
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Blade & Sorcery',
    [string]$ElectronArchivePath
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PSScriptRoot)
& node (Join-Path $root 'app\build-preload.cjs')
if ($LASTEXITCODE -ne 0) { throw 'Sandboxed preload bundling failed.' }
$config = Get-Content -LiteralPath (Join-Path $root 'runtime.json') -Raw | ConvertFrom-Json
$managed = Join-Path $GamePath 'BladeAndSorcery_Data\Managed'
if (!(Test-Path -LiteralPath (Join-Path $managed 'ThunderRoad.dll'))) { throw 'Set -GamePath to an installed PCVR game with managed assemblies.' }
$cache = Join-Path $root '.build-cache'
New-Item -ItemType Directory -Force $cache | Out-Null
if (!$ElectronArchivePath) {
    $ElectronArchivePath = Join-Path $cache $config.archive
    if (!(Test-Path -LiteralPath $ElectronArchivePath)) { Invoke-WebRequest -UseBasicParsing -Uri $config.url -OutFile $ElectronArchivePath }
}
if ((Get-FileHash -LiteralPath $ElectronArchivePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $config.sha256) { throw 'Electron archive checksum differs from the pinned official release.' }
$out = [IO.Path]::GetFullPath((Join-Path $root 'out\pcvr-1.4.3'))
# This is generated output only. Verify the absolute target before replacing it.
if (!$out.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($out) -ne 'pcvr-1.4.3') { throw 'Output escaped the source workspace.' }
if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
$package = Join-Path $out 'The Internet'
$runtime = Join-Path $package 'BrowserRuntime'
New-Item -ItemType Directory -Force $package,$runtime | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory((Resolve-Path -LiteralPath $ElectronArchivePath).Path,$runtime)
Move-Item -LiteralPath (Join-Path $runtime 'electron.exe') -Destination (Join-Path $runtime 'TheInternetBrowser.exe')
$defaultApp = Join-Path $runtime 'resources\default_app.asar'
if (Test-Path -LiteralPath $defaultApp) { Remove-Item -LiteralPath $defaultApp }
Get-ChildItem -LiteralPath (Join-Path $runtime 'locales') -File | Where-Object { $_.Name -ne 'en-US.pak' } | Remove-Item -Force
$app = Join-Path $runtime 'resources\app'
New-Item -ItemType Directory -Force $app | Out-Null
foreach ($name in @('main.js','preload.js','cleanup.js','start.html','package.json','filters.bin','THIRD-PARTY-NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $root ('app\'+$name)) -Destination $app }
foreach ($folder in @('assets','filter-sources')) { Copy-Item -LiteralPath (Join-Path $root ('app\'+$folder)) -Destination $app -Recurse }
# Electron is the native runtime, not a runtime npm dependency. Exclude downloader,
# editor types and cross-platform installer binaries from the shipped app.
$dependencies = & node (Join-Path $root 'app\runtime-dependencies.cjs')
if ($LASTEXITCODE -ne 0) { throw 'Could not resolve runtime dependencies. Restore with npm ci --ignore-scripts.' }
foreach ($name in ($dependencies | ConvertFrom-Json)) {
    $target = Join-Path $app ('node_modules\'+$name)
    New-Item -ItemType Directory -Force (Split-Path -Parent $target) | Out-Null
    Copy-Item -LiteralPath (Join-Path $root ('app\node_modules\'+$name)) -Destination $target -Recurse
}
$sdk = @(& dotnet --list-sdks)[-1]
if ($sdk -notmatch '^([^ ]+) \[(.+)\]') { throw 'A .NET SDK is required.' }
$compiler = Join-Path $Matches[2] ($Matches[1] + '\Roslyn\bincore\csc.dll')
$argsFile = Join-Path $cache 'compile.rsp'
$compilerArgs = @('/nologo','/target:library','/nostdlib+','/langversion:latest','/debug-','/deterministic+',('/pathmap:"'+$root+'=/_src"'),('/out:"'+(Join-Path $package 'TheInternet.dll')+'"'))
$compilerArgs += @(Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object { '/reference:"' + $_.FullName + '"' })
$compilerArgs += @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object { '"' + $_.FullName + '"' })
$compilerArgs | Set-Content -LiteralPath $argsFile
& dotnet $compiler ('@' + $argsFile)
if ($LASTEXITCODE -ne 0) { throw 'C# compilation failed.' }
foreach ($name in @('manifest.json','README.md','PRIVACY.md','REVIEWER-NOTES.md')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination $package }
Copy-Item -LiteralPath (Join-Path $root 'runtime.json') -Destination $runtime
# Raw native libraries remain inspectable; the manifest restores their filenames
# outside Mods so the game's managed *.dll loader never attempts to load them.
Get-ChildItem -LiteralPath $runtime -File -Filter '*.dll' | ForEach-Object { Move-Item -LiteralPath $_.FullName -Destination ($_.FullName + '.payload') }
$runtimeEntries = foreach($file in Get-ChildItem -LiteralPath $runtime -Recurse -File) {
    $relative=$file.FullName.Substring($runtime.Length+1).Replace('\','/')
    $target=if($relative.EndsWith('.dll.payload')){$relative.Substring(0,$relative.Length-8)}else{$relative}
    [ordered]@{source=$relative;target=$target;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
}
[ordered]@{version='1.4.3';files=@($runtimeEntries)} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $runtime 'runtime-files.json')
Write-Output "Built PCVR 1.4.3: $out (one complete mod folder)"
