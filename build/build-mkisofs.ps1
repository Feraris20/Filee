# Prepares Filee's pinned UDF writer. Windows uses cdrtfe's matching binary/runtime; Unix builds the same source.
# Matching source archives and license texts travel with the engine. Build on Ubuntu 22.04 for Linux packages.
[CmdletBinding()]
param(
    [string]$Destination = (Join-Path $PSScriptRoot '../engines'),
    [string]$PreparedEngine
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'mkisofs.json') -Raw | ConvertFrom-Json
$cache = Join-Path $PSScriptRoot '.cache/mkisofs'
$target = Join-Path $Destination 'mkisofs'
$onWindows = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
$programName = if ($onWindows) { 'mkisofs.exe' } else { 'mkisofs' }
New-Item -ItemType Directory -Force -Path $cache, $target | Out-Null

if (-not $PreparedEngine -and (Test-Path -LiteralPath (Join-Path $target $programName)) -and
    (Test-Path -LiteralPath (Join-Path $target 'sources/schily-2021-09-18.tar.bz2')) -and
    (Get-FileHash -LiteralPath (Join-Path $target 'sources/schily-2021-09-18.tar.bz2') -Algorithm SHA256).Hash -eq $pin.source.sha256) {
    $installedVersion = (& (Join-Path $target $programName) -version) -join "`n"
    if ($LASTEXITCODE -eq 0 -and $installedVersion -match [regex]::Escape($pin.version)) {
        Copy-Item -LiteralPath $PSCommandPath, (Join-Path $PSScriptRoot 'mkisofs.json') -Destination (Join-Path $target 'sources') -Force
        Write-Host "mkisofs $($pin.version) ready in $target (cached)"
        return
    }
}

function Fetch-Pinned($component) {
    $path = Join-Path $cache $component.file
    if (-not (Test-Path -LiteralPath $path)) {
        $partial = "$path.$([Guid]::NewGuid().ToString('N')).partial"
        Invoke-WebRequest -UseBasicParsing -Uri $component.url -OutFile $partial
        if ((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash -ne $component.sha256) {
            throw "mkisofs download checksum mismatch: $($component.file)"
        }
        Move-Item -LiteralPath $partial -Destination $path
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $component.sha256) {
        throw "mkisofs cache checksum mismatch: $($component.file)"
    }
    return $path
}

if ($PreparedEngine) {
    $prepared = (Resolve-Path -LiteralPath $PreparedEngine).Path
    if (-not (Test-Path -LiteralPath (Join-Path $prepared $programName))) { throw 'Prepared mkisofs program is missing.' }
    if (-not (Test-Path -LiteralPath (Join-Path $prepared 'sources/schily-2021-09-18.tar.bz2'))) { throw 'Prepared mkisofs source archive is missing.' }
    if ((Get-FileHash -LiteralPath (Join-Path $prepared 'sources/schily-2021-09-18.tar.bz2') -Algorithm SHA256).Hash -ne $pin.source.sha256) { throw 'Prepared mkisofs sources do not match the pin.' }
    Copy-Item -Path (Join-Path $prepared '*') -Destination $target -Recurse -Force
}
else {
    $sourceArchive = Fetch-Pinned $pin.source
    $work = Join-Path $cache ('build-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work | Out-Null
    & tar -xf $sourceArchive -C $work
    if ($LASTEXITCODE -ne 0) { throw 'Could not unpack mkisofs sources.' }
    $source = Join-Path $work 'schily-2021-09-18'
    $sources = Join-Path $target 'sources'
    New-Item -ItemType Directory -Force -Path $sources | Out-Null
    Copy-Item -LiteralPath $sourceArchive -Destination $sources -Force
    if ($onWindows) {
        $sevenZip = Join-Path $Destination '7zip/7z.exe'
        if (-not (Test-Path -LiteralPath $sevenZip)) { throw 'Prepare the bundled 7-Zip first (build/fetch-engines.ps1 -Only 7zip).' }
        foreach ($component in @($pin.windows, $pin.windowsRuntime)) {
            $archive = Fetch-Pinned $component
            & $sevenZip x -y -bso0 "-o$work/bin" $archive
            if ($LASTEXITCODE -ne 0) { throw "Could not unpack $($component.file)." }
        }
        Copy-Item -LiteralPath (Join-Path $work 'bin/mkisofs.exe'), (Join-Path $work 'bin/cygwin1.dll') -Destination $target -Force
        $runtimeSource = Fetch-Pinned $pin.windowsRuntimeSource
        Copy-Item -LiteralPath $runtimeSource -Destination $sources -Force
        & tar -xf $runtimeSource -C $work
        if ($LASTEXITCODE -ne 0) { throw 'Could not unpack Cygwin source licenses.' }
        $innerSource = Join-Path $work 'cygwin-2.3.1-1.src/newlib-cygwin-2.3.1.tar.bz2'
        & tar -xf $innerSource -C $work
        if ($LASTEXITCODE -ne 0) { throw 'Could not unpack Cygwin source tree.' }
        $runtimeLicense = Get-ChildItem -LiteralPath $work -Recurse -File | Where-Object { $_.Name -in @('COPYING', 'CYGWIN_LICENSE') -and $_.FullName -match 'cygwin' }
        foreach ($license in $runtimeLicense) {
            Copy-Item -LiteralPath $license.FullName -Destination (Join-Path $target ('Cygwin-' + $license.Name + '.txt')) -Force
        }
    }
    else {
        # Native compilation gives us x64/ARM64 executables on Linux and macOS. No root or system installation.
        Push-Location $source
        try {
            & make 'GMAKE_NOWARN=true' 'COPTX=-Wno-error=implicit-function-declaration' *> (Join-Path $work 'make.log')
            if ($LASTEXITCODE -ne 0) {
                Get-Content -LiteralPath (Join-Path $work 'make.log') -Tail 60 | Write-Host
                throw 'mkisofs native build failed.'
            }
        }
        finally { Pop-Location }
        $binary = Get-ChildItem -LiteralPath (Join-Path $source 'mkisofs/OBJ') -Recurse -File -Filter mkisofs | Select-Object -First 1
        if (-not $binary) { throw 'mkisofs build produced no program.' }
        Copy-Item -LiteralPath $binary.FullName -Destination (Join-Path $target $programName) -Force
        & chmod +x (Join-Path $target $programName)
        if ($LASTEXITCODE -ne 0) { throw 'Could not set mkisofs executable permissions.' }
    }
    Copy-Item -LiteralPath (Join-Path $source 'mkisofs/COPYING') -Destination (Join-Path $target 'mkisofs-COPYING.txt') -Force
    Copy-Item -LiteralPath (Join-Path $source 'CDDL.Schily.txt'), (Join-Path $source 'GPL-2.0.txt'), (Join-Path $source 'README.compile') -Destination $target -Force
    Copy-Item -LiteralPath $PSCommandPath, (Join-Path $PSScriptRoot 'mkisofs.json') -Destination $sources -Force
}

$version = (& (Join-Path $target $programName) -version) -join "`n"
if ($LASTEXITCODE -ne 0 -or $version -notmatch [regex]::Escape($pin.version)) { throw 'mkisofs executable failed its version check.' }
Write-Host "mkisofs $($pin.version) ready in $target"
if ($work -and (Test-Path -LiteralPath $work)) {
    $resolvedWork = [IO.Path]::GetFullPath($work)
    $cacheRoot = [IO.Path]::GetFullPath($cache).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedWork.StartsWith($cacheRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Build cleanup escaped its cache.' }
    Remove-Item -LiteralPath $resolvedWork -Recurse -Force
}
