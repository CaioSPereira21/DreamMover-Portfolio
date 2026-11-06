$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Load-TestDependencies.ps1')

$projectRoot = Split-Path -Parent $PSScriptRoot
$assemblyPath = Join-Path $projectRoot 'bin\DreamMover.dll'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -Path $assemblyPath

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-Equal {
    param([object]$Expected, [object]$Actual, [string]$Message)
    if ($Expected -ne $Actual) { throw "$Message Esperado: $Expected. Atual: $Actual." }
}

function New-CharacterZip {
    param(
        [string]$Path,
        [string]$Folder,
        [string]$InternalName,
        [byte]$Marker
    )

    $archive = [System.IO.Compression.ZipFile]::Open($Path, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $entries = @{
            "$Folder/$Folder.def" = "[Info]`r`nname = `"$InternalName`"`r`nauthor = `"Autor $Marker`"`r`n[Files]`r`ncmd = $Folder.cmd`r`ncns = $Folder.cns`r`nsprite = $Folder.sff`r`nanim = $Folder.air"
            "$Folder/$Folder.cmd" = '[Command]'
            "$Folder/$Folder.cns" = '[Data]'
            "$Folder/$Folder.sff" = [byte[]]($Marker, 2, 3)
            "$Folder/$Folder.air" = '[Begin Action 0]'
        }
        foreach ($item in $entries.GetEnumerator()) {
            $entry = $archive.CreateEntry($item.Key)
            $stream = $entry.Open()
            try {
                $bytes = if ($item.Value -is [byte[]]) { $item.Value } else { [System.Text.Encoding]::UTF8.GetBytes([string]$item.Value) }
                $stream.Write($bytes, 0, $bytes.Length)
            }
            finally {
                $stream.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

function New-Upload {
    param([string]$Path)
    $upload = [DreamMover.Services.CharacterArchiveUpload]::new()
    $upload.FileName = [System.IO.Path]::GetFileName($Path)
    $upload.Length = (Get-Item -LiteralPath $Path).Length
    $upload.InputStream = [System.IO.File]::OpenRead($Path)
    return $upload
}

$testDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("DreamMover-CharacterArchive-" + [Guid]::NewGuid().ToString('N'))
$tempRoot = Join-Path $testDirectory 'imports'
$charsPath = Join-Path $testDirectory 'chars'
[System.IO.Directory]::CreateDirectory($testDirectory) | Out-Null
[System.IO.Directory]::CreateDirectory($charsPath) | Out-Null

try {
    $zipA = Join-Path $testDirectory 'versao-a.zip'
    $zipB = Join-Path $testDirectory 'versao-b.zip'
    New-CharacterZip $zipA 'FighterA' 'Nome Antigo A' 1
    New-CharacterZip $zipB 'FighterB' 'Nome Antigo B' 2

    [DreamMover.Services.CharacterArchiveUpload[]]$uploads = @((New-Upload $zipA), (New-Upload $zipB))
    try {
        $analysis = [DreamMover.Services.CharacterArchiveImportService]::Analisar($uploads, $tempRoot, 'Herói Final')
    }
    finally {
        $uploads | ForEach-Object { $_.InputStream.Dispose() }
    }

    Assert-Equal 'Herói Final' $analysis.Nome 'O nome obrigatorio deve ser preservado.'
    Assert-Equal 2 $analysis.Candidatos.Count 'Os dois personagens devem ser analisados.'
    Assert-Equal 'Nome Antigo A' $analysis.Candidatos[0].NomeOriginal 'O name original deve ser lido do .def.'

    $fight = [DreamMover.Services.CharacterTournamentFightResult]::new()
    $fight.CandidatoCasaId = $analysis.Candidatos[0].Id
    $fight.CandidatoCasaPacote = $analysis.Candidatos[0].Pacote
    $fight.CandidatoVisitanteId = $analysis.Candidatos[1].Id
    $fight.CandidatoVisitantePacote = $analysis.Candidatos[1].Pacote
    $fight.PontosCasa = 2
    $fight.PontosVisitante = 1
    $fight.VencedorId = $analysis.Candidatos[0].Id

    [DreamMover.Services.CharacterTournamentFightResult[]]$fights = @($fight)
    $ranking = [DreamMover.Services.CharacterArchiveImportService]::Classificar($analysis.Candidatos, $fights)
    Assert-Equal $analysis.Candidatos[0].Id $ranking.RecomendadoId 'Quem mais venceu deve ser recomendado.'
    Assert-Equal 1 $ranking.Ranking[0].Vitorias 'A vitoria deve entrar no ranking.'
    Assert-Equal 1 $ranking.Ranking[0].SaldoRounds 'O saldo de rounds deve ser calculado.'

    $imported = [DreamMover.Services.CharacterArchiveImportService]::Importar(
        $analysis.Token,
        $analysis.Candidatos[1].Id,
        'Herói Final',
        $tempRoot,
        $charsPath,
        $false,
        $false)

    $finalFolder = Join-Path $charsPath 'Herói Final'
    $finalDef = Join-Path $finalFolder 'Herói Final.def'
    Assert-True (Test-Path -LiteralPath $finalFolder -PathType Container) 'A pasta final deve usar o nome informado.'
    Assert-True (Test-Path -LiteralPath $finalDef -PathType Leaf) 'O .def final deve usar o nome informado.'
    Assert-True (Test-Path -LiteralPath (Join-Path $finalFolder 'FighterB.sff')) 'Os arquivos do candidato escolhido devem ser preservados.'

    $defContent = [System.IO.File]::ReadAllText($finalDef, [System.Text.Encoding]::Default)
    Assert-True ($defContent -match 'name\s*=\s*"Herói Final"') 'O campo name deve receber o nome obrigatorio.'
    Assert-True ($defContent -match 'displayname\s*=\s*"Herói Final"') 'O campo displayname deve ser criado com o nome obrigatorio.'
    Assert-Equal 'Herói Final' $imported.Pasta 'A resposta deve informar a pasta instalada.'

    [DreamMover.Services.CharacterArchiveFileDescriptor[]]$descriptors = @(
        [DreamMover.Services.CharacterArchiveFileDescriptor]@{ FileName = 'versao-a.zip'; Length = (Get-Item -LiteralPath $zipA).Length },
        [DreamMover.Services.CharacterArchiveFileDescriptor]@{ FileName = 'versao-b.zip'; Length = (Get-Item -LiteralPath $zipB).Length }
    )
    $stageToken = [DreamMover.Services.CharacterArchiveImportService]::IniciarEnvio($descriptors, $tempRoot)
    $incompletoFalhou = $false
    try {
        [DreamMover.Services.CharacterArchiveImportService]::AnalisarEnvio($stageToken, $tempRoot, 'Heroi Final') | Out-Null
    }
    catch { $incompletoFalhou = $true }
    Assert-True $incompletoFalhou 'A analise deve rejeitar uploads incompletos.'

    $foraDeOrdemFalhou = $false
    $parteInvalida = [System.IO.MemoryStream]::new([byte[]](1))
    try {
        [DreamMover.Services.CharacterArchiveImportService]::ReceberParte($stageToken, 0, 1, $parteInvalida, 1, $tempRoot) | Out-Null
    }
    catch { $foraDeOrdemFalhou = $true }
    finally { $parteInvalida.Dispose() }
    Assert-True $foraDeOrdemFalhou 'Partes fora de ordem devem ser rejeitadas.'

    $paths = @($zipA, $zipB)
    for ($indice = 0; $indice -lt $paths.Count; $indice++) {
        $bytes = [System.IO.File]::ReadAllBytes($paths[$indice])
        $metade = [int][Math]::Floor($bytes.Length / 2)
        foreach ($offset in @(0, $metade)) {
            $tamanho = if ($offset -eq 0) { $metade } else { $bytes.Length - $metade }
            $parte = [System.IO.MemoryStream]::new($bytes, $offset, $tamanho, $false)
            try {
                $recebido = [DreamMover.Services.CharacterArchiveImportService]::ReceberParte($stageToken, $indice, $offset, $parte, $tamanho, $tempRoot)
                Assert-Equal ($offset + $tamanho) $recebido 'Cada parte deve ser gravada na posicao correta.'
            }
            finally { $parte.Dispose() }
        }
    }
    $stagedAnalysis = [DreamMover.Services.CharacterArchiveImportService]::AnalisarEnvio($stageToken, $tempRoot, 'Heroi Final')
    Assert-Equal 2 $stagedAnalysis.Candidatos.Count 'A analise deve aceitar os dois pacotes enviados em partes.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $tempRoot $stageToken))) 'A pasta de envio deve ser removida apos a analise.'
    [DreamMover.Services.CharacterArchiveImportService]::Cancelar($stagedAnalysis.Token, $tempRoot)

    [DreamMover.Services.CharacterArchiveFileDescriptor[]]$largeDescriptors = @(
        [DreamMover.Services.CharacterArchiveFileDescriptor]@{ FileName = 'grande.zip'; Length = 129L * 1024L * 1024L },
        [DreamMover.Services.CharacterArchiveFileDescriptor]@{ FileName = 'outro.zip'; Length = 1L }
    )
    $largeToken = [DreamMover.Services.CharacterArchiveImportService]::IniciarEnvio($largeDescriptors, $tempRoot)
    Assert-True (Test-Path -LiteralPath (Join-Path $tempRoot $largeToken)) 'Pacotes acima de 128 MB devem ser aceitos para envio.'
    [DreamMover.Services.CharacterArchiveImportService]::Cancelar($largeToken, $tempRoot)

    if ($env:DREAMMOVER_TEST_LARGE_UPLOAD -eq '1') {
        $largeZip = Join-Path $testDirectory 'versao-grande.zip'
        [System.IO.File]::Copy($zipA, $largeZip)
        $archive = [System.IO.Compression.ZipFile]::Open($largeZip, [System.IO.Compression.ZipArchiveMode]::Update)
        try {
            $entry = $archive.CreateEntry('FighterA/large.bin', [System.IO.Compression.CompressionLevel]::NoCompression)
            $stream = $entry.Open()
            try {
                $block = [byte[]]::new(1024 * 1024)
                for ($i = 0; $i -lt 129; $i++) { $stream.Write($block, 0, $block.Length) }
            }
            finally { $stream.Dispose() }
        }
        finally { $archive.Dispose() }
        Assert-True ((Get-Item -LiteralPath $largeZip).Length -gt 128MB) 'O arquivo de teste deve superar 128 MB.'

        [DreamMover.Services.CharacterArchiveFileDescriptor[]]$largeFiles = @(
            [DreamMover.Services.CharacterArchiveFileDescriptor]@{ FileName = 'versao-grande.zip'; Length = (Get-Item -LiteralPath $largeZip).Length },
            [DreamMover.Services.CharacterArchiveFileDescriptor]@{ FileName = 'versao-b.zip'; Length = (Get-Item -LiteralPath $zipB).Length }
        )
        $largeStage = [DreamMover.Services.CharacterArchiveImportService]::IniciarEnvio($largeFiles, $tempRoot)
        foreach ($item in @(@{ Path = $largeZip; Index = 0 }, @{ Path = $zipB; Index = 1 })) {
            $source = [System.IO.File]::OpenRead($item.Path)
            try {
                $offset = 0L
                $buffer = [byte[]]::new(8 * 1024 * 1024)
                while (($read = $source.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    $part = [System.IO.MemoryStream]::new($buffer, 0, $read, $false)
                    try { $offset = [DreamMover.Services.CharacterArchiveImportService]::ReceberParte($largeStage, $item.Index, $offset, $part, $read, $tempRoot) }
                    finally { $part.Dispose() }
                }
            }
            finally { $source.Dispose() }
        }
        $largeAnalysis = [DreamMover.Services.CharacterArchiveImportService]::AnalisarEnvio($largeStage, $tempRoot, 'Heroi Grande')
        Assert-Equal 2 $largeAnalysis.Candidatos.Count 'Pacote real acima de 128 MB deve ser analisado.'
        [DreamMover.Services.CharacterArchiveImportService]::Cancelar($largeAnalysis.Token, $tempRoot)
    }

    $nomeObrigatorioFalhou = $false
    try {
        [DreamMover.Services.CharacterArchiveImportService]::NormalizarNomeObrigatorio('  ') | Out-Null
    }
    catch {
        $nomeObrigatorioFalhou = $true
    }
    Assert-True $nomeObrigatorioFalhou 'O nome vazio deve ser rejeitado.'
}
finally {
    if ([System.IO.Directory]::Exists($testDirectory)) {
        [System.IO.Directory]::Delete($testDirectory, $true)
    }
}

Write-Output 'CharacterArchiveImportService: todos os testes passaram.'
