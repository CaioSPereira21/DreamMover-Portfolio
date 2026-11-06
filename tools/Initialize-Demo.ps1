param(
    [string]$ServerInstance = '(localdb)\MSSQLLocalDB',
    [ValidatePattern('^DreamMoverPortfolio(?:_[A-Za-z0-9]+)?$')]
    [string]$Database = 'DreamMoverPortfolio'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName System.Data
function Invoke-Sql([string]$Catalog, [string]$Sql, [switch]$Scalar) {
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder['Data Source'] = $ServerInstance
    $builder['Initial Catalog'] = $Catalog
    $builder['Integrated Security'] = $true
    $builder['Connect Timeout'] = 15
    $connection = New-Object System.Data.SqlClient.SqlConnection($builder.ConnectionString)
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandTimeout = 120
        $command.CommandText = $Sql
        if ($Scalar) { return $command.ExecuteScalar() }
        [void]$command.ExecuteNonQuery()
    } finally { $connection.Dispose() }
}
Invoke-Sql 'master' "IF DB_ID(N'$Database') IS NULL CREATE DATABASE [$Database];"
Invoke-Sql $Database "IF OBJECT_ID('dbo.__PortfolioMigrations', 'U') IS NULL CREATE TABLE dbo.__PortfolioMigrations (Nome NVARCHAR(255) NOT NULL PRIMARY KEY, AplicadaEmUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME());"
foreach ($migration in (Get-ChildItem -LiteralPath (Join-Path $projectRoot 'App_Data\Migrations') -Filter '*.sql' | Sort-Object Name)) {
    $migrationName = $migration.Name.Replace("'", "''")
    if ((Invoke-Sql $Database "SELECT COUNT(*) FROM dbo.__PortfolioMigrations WHERE Nome = N'$migrationName';" -Scalar) -gt 0) {
        Write-Output ('Ja aplicada: ' + $migration.Name)
        continue
    }
    foreach ($batch in [regex]::Split([IO.File]::ReadAllText($migration.FullName), '(?im)^\s*GO\s*(?:--[^\r\n]*)?$')) {
        if (-not [string]::IsNullOrWhiteSpace($batch)) { Invoke-Sql $Database $batch }
    }
    Invoke-Sql $Database "INSERT dbo.__PortfolioMigrations (Nome) VALUES (N'$migrationName');"
    Write-Output ('Aplicada: ' + $migration.Name)
}
$seed = @'
IF NOT EXISTS (SELECT 1 FROM dbo.Lutador WHERE PastaPersonagem = N'kfm')
    INSERT dbo.Lutador (Nome, Universo, Autor, Exp, Nivel, RatingElo, Portrait, Stand, PastaPersonagem)
    VALUES (N'Kung Fu Man', N'Demonstracao', N'Elecbyte', 0, 1, 1000, N'Undefined.png', N'Undefined.png', N'kfm');
IF NOT EXISTS (SELECT 1 FROM dbo.Lutador WHERE PastaPersonagem = N'kfm720')
    INSERT dbo.Lutador (Nome, Universo, Autor, Exp, Nivel, RatingElo, Portrait, Stand, PastaPersonagem)
    VALUES (N'Kung Fu Man 720', N'Demonstracao', N'Elecbyte', 0, 1, 1000, N'Undefined.png', N'Undefined.png', N'kfm720');
'@
Invoke-Sql $Database $seed
$configPath = Join-Path $projectRoot 'ConnectionStrings.config'
if (Test-Path -LiteralPath $configPath) {
    Write-Output 'ConnectionStrings.config existente preservado. Confira se aponta para o banco preparado.'
} else {
    [xml]$config = Get-Content -LiteralPath (Join-Path $projectRoot 'ConnectionStrings.example.config') -Raw
    $entityBuilder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $entityBuilder['Data Source'] = $ServerInstance
    $entityBuilder['Initial Catalog'] = $Database
    $entityBuilder['Integrated Security'] = $true
    $entityBuilder['MultipleActiveResultSets'] = $true
    $config.connectionStrings.add.connectionString = 'metadata=res://*/Models.DreamMoverEntities.csdl|res://*/Models.DreamMoverEntities.ssdl|res://*/Models.DreamMoverEntities.msl;provider=System.Data.SqlClient;provider connection string="' + $entityBuilder.ConnectionString + '"'
    $config.Save($configPath)
}
$charsPath = Join-Path $projectRoot 'Engines\Mugen\chars'
$assemblyPath = Join-Path $projectRoot 'bin\DreamMover.dll'
if ((Test-Path -LiteralPath $charsPath) -and (Test-Path -LiteralPath $assemblyPath)) {
    Add-Type -AssemblyName System.Drawing
    Add-Type -Path $assemblyPath
    $extractor = New-Object DreamMover.Helpers.MugenStandExtractor($charsPath, (Join-Path $projectRoot 'Imagens'))
    foreach ($reference in @('kfm','kfm720')) {
        $standName = 'DemoKfm-' + $reference + '-Stand.gif'
        $portraitName = 'DemoKfm-' + $reference + '-Portrait.png'
        [void]$extractor.ExtractStandFromCharacter($reference, $reference, $standName, $false, 80, $true, 0, $null, $false)
        [void]$extractor.ExtractPortraitFromCharacter($reference, $reference, $portraitName, $false, $true, 9000, 0, $null)
        Invoke-Sql $Database "UPDATE dbo.Lutador SET Stand = N'$standName' WHERE PastaPersonagem = N'$reference' AND (Stand IS NULL OR Stand = N'Undefined.png'); UPDATE dbo.Lutador SET Portrait = N'$portraitName' WHERE PastaPersonagem = N'$reference' AND (Portrait IS NULL OR Portrait = N'Undefined.png');"
    }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'Imagens\DemoKfm-kfm-Portrait.png') -Destination (Join-Path $projectRoot 'Imagens\Demonstracao.png')
} else {
    Copy-Item -LiteralPath (Join-Path $projectRoot 'Imagens\Undefined.png') -Destination (Join-Path $projectRoot 'Imagens\Demonstracao.png')
    Write-Output 'Compile a aplicacao e instale as engines para gerar as imagens dos exemplos.'
}
Write-Output ('Banco de demonstracao pronto: ' + $Database)
