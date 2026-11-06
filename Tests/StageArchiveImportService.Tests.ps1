$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Load-TestDependencies.ps1')

$projectRoot = Split-Path -Parent $PSScriptRoot
$assemblyPath = Join-Path $projectRoot 'bin\DreamMover.dll'
Add-Type -Path $assemblyPath
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-Equal {
    param([object]$Expected, [object]$Actual, [string]$Message)
    if ($Expected -ne $Actual) { throw "$Message Esperado: $Expected. Atual: $Actual." }
}

function New-StageZip {
    param([string]$Path, [hashtable]$Entries)

    $archive = [System.IO.Compression.ZipFile]::Open($Path, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($item in $Entries.GetEnumerator()) {
            $entry = $archive.CreateEntry($item.Key)
            $stream = $entry.Open()
            try {
                $bytes = if ($item.Value -is [byte[]]) {
                    $item.Value
                }
                else {
                    [System.Text.Encoding]::UTF8.GetBytes([string]$item.Value)
                }
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

    $upload = [DreamMover.Services.StageArchiveUpload]::new()
    $upload.FileName = [System.IO.Path]::GetFileName($Path)
    $upload.Length = (Get-Item -LiteralPath $Path).Length
    $upload.InputStream = [System.IO.File]::OpenRead($Path)
    return $upload
}

$testDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("DreamMover-StageArchive-" + [Guid]::NewGuid().ToString('N'))
$tempRoot = Join-Path $testDirectory 'imports'
$stagesPath = Join-Path $testDirectory 'stages'
[System.IO.Directory]::CreateDirectory($testDirectory) | Out-Null
[System.IO.Directory]::CreateDirectory($stagesPath) | Out-Null

try {
    $zipComMusica = Join-Path $testDirectory 'arena-um.zip'
    $zipSemMusica = Join-Path $testDirectory 'arena-dois.zip'
    New-StageZip $zipComMusica @{
        'Arena Um/AttachedChar/PitController.def' = "[Info]`r`nname = Controlador`r`n[Files]`r`nst = PitController.cns"
        'Arena Um/AttachedChar/PitController.chr' = "[Files]`r`nst = PitController.cns`r`nanim = PitController.air"
        'Arena Um/AttachedChar/PitController.cns' = "[Statedef 0]`r`ntype = S"
        'Arena Um/AttachedChar/PitController.air' = "[Begin Action 0]"
        'Arena Um/AttachedChar/PitController.cmd' = "[Remap]`r`nx = x"
        'Arena Um/Arena Um.def' = "[Info]`r`nname = Arena Um`r`nauthor = Teste`r`nAttachedChar = Arena Um/AttachedChar/PitController.chr`r`n[Music]`r`nbgmusic = sound/antiga.mp3`r`n[BGDef]`r`nspr = stages/Arena Um/Arena Um.sff"
        'Arena Um/Arena Um.sff' = [byte[]](1, 2, 3)
        'Arena Um/Arena Um.glb' = [byte[]](103, 108, 84, 70)
        'Arena Um/Arena Um.hdr' = [byte[]](35, 63, 82, 65, 68, 73, 65, 78, 67, 69)
        'Arena Um/Arena Um.chr' = "[Files]`r`nstates = Arena Um.zss"
        'Arena Um/Arena Um.zss' = "[StateDef 0; type: S; physics: N;]"
        'Arena Um/tema.mp3' = [byte[]](4, 5, 6)
        'Arena Um/leia-me.txt' = 'arquivo nao suportado'
    }
    New-StageZip $zipSemMusica @{
        'Arena Dois.def' = "[Info]`r`nname = Arena Dois"
        'Arena Dois.sff' = [byte[]](7, 8, 9)
    }

    [DreamMover.Services.StageArchiveUpload[]]$uploads = @((New-Upload $zipComMusica), (New-Upload $zipSemMusica))
    try {
        $analysis = [DreamMover.Services.StageArchiveImportService]::Analisar($uploads, $tempRoot)
    }
    finally {
        $uploads | ForEach-Object { $_.InputStream.Dispose() }
    }

    Assert-Equal 2 $analysis.Pacotes.Count 'Os dois pacotes devem ser analisados no mesmo lote.'
    $pacoteComMusica = $analysis.Pacotes | Where-Object Nome -eq 'arena-um.zip'
    $pacoteSemMusica = $analysis.Pacotes | Where-Object Nome -eq 'arena-dois.zip'
    Assert-True $pacoteComMusica.PossuiDef 'O arquivo .def deve ser identificado.'
    Assert-True $pacoteComMusica.PossuiMusica 'A musica dentro do ZIP deve ser identificada.'
    $defPrincipal = $pacoteComMusica.Arquivos | Where-Object Caminho -eq 'Arena Um/Arena Um.def'
    $defAuxiliar = $pacoteComMusica.Arquivos | Where-Object Caminho -eq 'Arena Um/AttachedChar/PitController.def'
    Assert-True $defPrincipal.Selecionado 'O .def que possui [BGDef] deve ser selecionado como cenario.'
    Assert-True (-not $defAuxiliar.Selecionado) 'O .def auxiliar do AttachedChar nao deve substituir o .def do cenario.'
    $arquivoGlb = $pacoteComMusica.Arquivos | Where-Object Nome -eq 'Arena Um.glb'
    Assert-True ($arquivoGlb.Suportado -and $arquivoGlb.Tipo -eq 'recurso' -and $arquivoGlb.Selecionado) 'O modelo .glb deve ser identificado como recurso suportado e selecionado.'
    $arquivoHdr = $pacoteComMusica.Arquivos | Where-Object Nome -eq 'Arena Um.hdr'
    Assert-True ($arquivoHdr.Suportado -and $arquivoHdr.Tipo -eq 'recurso' -and $arquivoHdr.Selecionado) 'O arquivo .hdr deve ser identificado como recurso suportado e selecionado.'
    $arquivoChr = $pacoteComMusica.Arquivos | Where-Object Nome -eq 'Arena Um.chr'
    Assert-True ($arquivoChr.Suportado -and $arquivoChr.Tipo -eq 'recurso' -and $arquivoChr.Selecionado) 'A definicao .chr do AttachedChar deve ser identificada como recurso suportado e selecionado.'
    $arquivoZss = $pacoteComMusica.Arquivos | Where-Object Nome -eq 'Arena Um.zss'
    Assert-True ($arquivoZss.Suportado -and $arquivoZss.Tipo -eq 'recurso' -and $arquivoZss.Selecionado) 'O script .zss deve ser identificado como recurso suportado e selecionado.'
    foreach ($nomeAuxiliar in @('PitController.cns', 'PitController.air', 'PitController.cmd')) {
        $arquivoAuxiliar = $pacoteComMusica.Arquivos | Where-Object Nome -eq $nomeAuxiliar
        Assert-True ($arquivoAuxiliar.Suportado -and $arquivoAuxiliar.Selecionado) "O arquivo auxiliar $nomeAuxiliar deve ser preservado."
    }
    Assert-True (-not $pacoteSemMusica.PossuiMusica) 'O pacote sem musica deve ser sinalizado.'
    Assert-True (($pacoteComMusica.Arquivos | Where-Object Tipo -eq 'nao-suportado').Count -eq 1) 'Arquivos nao suportados devem aparecer na analise.'

    [DreamMover.Services.StagePackageSelection[]]$selections = foreach ($package in $analysis.Pacotes) {
        $selection = [DreamMover.Services.StagePackageSelection]::new()
        $selection.PacoteId = $package.Id
        $selection.Importar = $true
        $selection.Arquivos = [System.Collections.Generic.List[string]]::new()
        $package.Arquivos | Where-Object { $_.Suportado -and $_.Selecionado } | ForEach-Object { $selection.Arquivos.Add($_.Id) }
        $selection
    }

    $musicRequired = $false
    try {
        [DreamMover.Services.StageArchiveImportService]::Importar(
            $analysis.Token,
            $selections,
            [DreamMover.Services.StageAdditionalMusic[]]@(),
            $tempRoot,
            $stagesPath,
            $false) | Out-Null
    }
    catch {
        $exception = if ($_.Exception.InnerException) { $_.Exception.InnerException } else { $_.Exception }
        $musicRequired = $exception -is [DreamMover.Services.StageMusicRequiredException]
    }
    Assert-True $musicRequired 'A importacao deve parar quando um pacote ativo nao possui musica.'

    $additionalStream = [System.IO.MemoryStream]::new([byte[]](10, 11, 12))
    try {
        $additionalMusic = [DreamMover.Services.StageAdditionalMusic]::new()
        $additionalMusic.PacoteId = $pacoteSemMusica.Id
        $additionalMusic.FileName = 'musica-extra.ogg'
        $additionalMusic.Length = $additionalStream.Length
        $additionalMusic.InputStream = $additionalStream

        $imported = [DreamMover.Services.StageArchiveImportService]::Importar(
            $analysis.Token,
            $selections,
            [DreamMover.Services.StageAdditionalMusic[]]@($additionalMusic),
            $tempRoot,
            $stagesPath,
            $false)
    }
    finally {
        $additionalStream.Dispose()
    }

    Assert-Equal 2 $imported.Count 'Os dois cenarios devem ser importados juntos.'
    Assert-True (Test-Path -LiteralPath (Join-Path $stagesPath 'Arena Um\Arena Um.def')) 'O .def deve preservar a pasta raiz do pacote.'
    Assert-True (Test-Path -LiteralPath (Join-Path $stagesPath 'Arena Um\Arena Um.glb')) 'O modelo .glb do cenario 3D deve preservar sua pasta.'
    Assert-True (Test-Path -LiteralPath (Join-Path $stagesPath 'Arena Um\Arena Um.hdr')) 'O arquivo .hdr necessario ao cenario deve preservar sua pasta.'
    Assert-True (Test-Path -LiteralPath (Join-Path $stagesPath 'Arena Um\AttachedChar\PitController.chr')) 'A definicao .chr do AttachedChar deve preservar a subpasta.'
    Assert-True (Test-Path -LiteralPath (Join-Path $stagesPath 'Arena Um\AttachedChar\PitController.cns')) 'O CNS do AttachedChar deve preservar a subpasta.'
    Assert-True (Test-Path -LiteralPath (Join-Path $stagesPath 'Arena Um\AttachedChar\PitController.air')) 'O AIR do AttachedChar deve preservar a subpasta.'
    Assert-True (Test-Path -LiteralPath (Join-Path $stagesPath 'Arena Um\Arena Um.zss')) 'O script .zss do cenario deve preservar sua pasta.'
    Assert-True (Test-Path -LiteralPath (Join-Path $stagesPath 'Arena Um\Arena Um.mp3')) 'A musica interna deve ficar ao lado do .def.'
    Assert-True (Test-Path -LiteralPath (Join-Path $stagesPath 'arena-dois\Arena Dois.ogg')) 'Pacotes sem pasta raiz devem receber uma pasta propria.'

    $importadoUm = $imported | Where-Object NomePacote -eq 'arena-um.zip'
    $importadoDois = $imported | Where-Object NomePacote -eq 'arena-dois.zip'
    Assert-Equal 'Arena Um/Arena Um.def' $importadoUm.ArquivoDef 'A referencia do primeiro cenario deve incluir a pasta.'
    Assert-Equal 'arena-dois/Arena Dois.def' $importadoDois.ArquivoDef 'A referencia do segundo cenario deve incluir a pasta criada.'

    $defUm = [System.IO.File]::ReadAllText((Join-Path $stagesPath 'Arena Um\Arena Um.def'), [System.Text.Encoding]::Default)
    $defDois = [System.IO.File]::ReadAllText((Join-Path $stagesPath 'arena-dois\Arena Dois.def'), [System.Text.Encoding]::Default)
    Assert-True ($defUm -match 'bgmusic = stages/Arena Um/Arena Um\.mp3') 'A referencia bgmusic do primeiro cenario deve incluir a pasta.'
    Assert-True ($defDois -match 'bgmusic = stages/arena-dois/Arena Dois\.ogg') 'A referencia bgmusic deve incluir a pasta criada.'

    [DreamMover.Services.StageArchiveImportService]::Cancelar($analysis.Token, $tempRoot)
    Assert-Equal 0 @([System.IO.Directory]::GetDirectories($tempRoot)).Count 'Cancelar deve remover os arquivos temporarios da analise.'
}
finally {
    if ([System.IO.Directory]::Exists($testDirectory)) {
        [System.IO.Directory]::Delete($testDirectory, $true)
    }
}

Write-Output 'StageArchiveImportService: todos os testes passaram.'
