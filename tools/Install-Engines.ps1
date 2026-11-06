param([string]$CacheDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($CacheDirectory)) { $CacheDirectory = Join-Path $projectRoot 'artifacts\downloads' }
[IO.Directory]::CreateDirectory($CacheDirectory) | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$packages = @(
    @{
        Name = 'Mugen'; File = 'mugen.zip'
        Url = 'https://codeload.github.com/Tunlan/mugen-1.1b1/zip/5ed02054fe5e80c24259b9da1f7d129b995676c9'
        Sha256 = '42A178ECFCCE327355B60D029A82A38B351650E19B9651F810792E2920C123B3'
        Subdirectory = 'mugen-1.1b1-5ed02054fe5e80c24259b9da1f7d129b995676c9'
        Executable = 'mugen.exe'
    },
    @{
        Name = 'Ikemen'; File = 'ikemen.zip'
        Url = 'https://github.com/ikemen-engine/Ikemen-GO/releases/download/v1.0.0-rc.3/Ikemen_GO-v1.0.0-rc.3-windows.zip'
        Sha256 = 'F7A144DCA8393C4B1B21A904E409CD2FFFB63B098BB73F9A5953DFB0077F5B7E'
        Subdirectory = ''; Executable = 'Ikemen_GO.exe'
    }
)
foreach ($package in $packages) {
    $destination = Join-Path $projectRoot ('Engines\' + $package.Name)
    if (Test-Path -LiteralPath $destination) {
        if (Test-Path -LiteralPath (Join-Path $destination $package.Executable)) {
            Write-Output ($package.Name + ': instalacao existente preservada.')
            continue
        }
        throw ('Pasta existente incompleta: ' + $destination)
    }
    $archive = Join-Path $CacheDirectory $package.File
    if (-not (Test-Path -LiteralPath $archive)) {
        Invoke-WebRequest -Uri $package.Url -OutFile $archive -UseBasicParsing
    }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $package.Sha256) {
        throw ('Checksum invalido: ' + $archive)
    }
    $staging = Join-Path $projectRoot ('artifacts\engine-staging\' + [Guid]::NewGuid().ToString('N'))
    [IO.Compression.ZipFile]::ExtractToDirectory($archive, $staging)
    $contentRoot = if ($package.Subdirectory) { Join-Path $staging $package.Subdirectory } else { $staging }
    [IO.Directory]::CreateDirectory($destination) | Out-Null
    Get-ChildItem -LiteralPath $contentRoot -Force | Copy-Item -Destination $destination -Recurse
    if (-not (Test-Path -LiteralPath (Join-Path $destination $package.Executable))) {
        throw ('Executavel nao encontrado apos extracao: ' + $package.Name)
    }
    Write-Output ($package.Name + ': instalado com checksum verificado.')
}
Write-Output 'Engines prontas. Configuracoes e recursos originais dos pacotes foram mantidos.'
