$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$helperPath = Join-Path $projectRoot 'Helpers\MugenStageMusic.cs'

Add-Type -Path $helperPath

function Assert-Equal {
    param(
        [object]$Expected,
        [object]$Actual,
        [string]$Message
    )

    if ($Expected -ne $Actual) {
        throw "$Message Esperado: $Expected. Atual: $Actual."
    }
}

$testDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("DreamMover-MugenStageMusic-" + [Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($testDirectory) | Out-Null

try {
    $defPath = Join-Path $testDirectory 'Arena Nova.def'
    $musicPath = Join-Path $testDirectory 'Arena Nova.mp3'
    $defContent = "[Info]`r`nname = Arena Nova`r`n`r`n[Music]`r`n  bgmusic = sound/tema-antigo.mp3 ; manter comentario`r`n[BG]"
    [System.IO.File]::WriteAllText($defPath, $defContent, [System.Text.Encoding]::Default)
    [System.IO.File]::WriteAllBytes($musicPath, [byte[]](1, 2, 3))

    [DreamMover.Helpers.MugenStageMusic]::CorrigirReferenciaNoDef($defPath, 'Arena Nova.mp3')
    $updated = [System.IO.File]::ReadAllText($defPath, [System.Text.Encoding]::Default)

    if ($updated -notmatch '  bgmusic = stages/Arena Nova\.mp3 ; manter comentario') {
        throw 'A referencia bgmusic existente nao foi corrigida preservando indentacao e comentario.'
    }

    Assert-Equal 'Arena Nova.mp3' ([DreamMover.Helpers.MugenStageMusic]::ObterArquivo($testDirectory, 'Arena Nova.def')) 'A musica com o mesmo nome-base do .def deve ser localizada.'
    Assert-Equal 'Arena Nova.ogg' ([DreamMover.Helpers.MugenStageMusic]::ObterNomeNormalizado('Arena Nova.def', 'tema.OGG')) 'O nome da musica deve acompanhar o nome-base do .def.'
    Assert-Equal 'audio/mpeg' ([DreamMover.Helpers.MugenStageMusic]::ObterContentType('Arena Nova.mp3')) 'O content type de MP3 deve permitir reproducao no navegador.'

    $round2 = Join-Path $testDirectory 'Arena Nova.round2.mp3'
    $round3 = Join-Path $testDirectory 'Arena Nova.round3.ogg'
    [System.IO.File]::WriteAllBytes($round2, [byte[]](7, 8, 9))
    [System.IO.File]::WriteAllBytes($round3, [byte[]](10, 11, 12))
    [DreamMover.Helpers.MugenStageMusic]::CorrigirReferenciaNoDef($defPath, 'Arena Nova.round2.mp3', 2)
    [DreamMover.Helpers.MugenStageMusic]::CorrigirReferenciaNoDef($defPath, 'Arena Nova.round3.ogg', 3)
    $updatedRounds = [System.IO.File]::ReadAllText($defPath, [System.Text.Encoding]::Default)
    if (($updatedRounds -notmatch 'round2\.bgmusic = stages/Arena Nova\.round2\.mp3') -or
        ($updatedRounds -notmatch 'round3\.bgmusic = stages/Arena Nova\.round3\.ogg')) {
        throw 'As musicas dos rounds 2 e 3 devem seguir a sintaxe nativa do IKEMEN.'
    }
    Assert-Equal 'Arena Nova.round2.mp3' ([DreamMover.Helpers.MugenStageMusic]::ObterArquivo($testDirectory, 'Arena Nova.def', 2)) 'A musica do round 2 deve ser localizada.'
    Assert-Equal 'Arena Nova.round3.ogg' ([DreamMover.Helpers.MugenStageMusic]::ObterArquivo($testDirectory, 'Arena Nova.def', 3)) 'A musica do round 3 deve ser localizada.'
    Assert-Equal 'Arena Nova.round2.mp3' ([DreamMover.Helpers.MugenStageMusic]::ObterNomeDaFaixa('Arena Nova.def', 'nova.mp3', 2)) 'O nome enviado pelo sistema deve ser deterministico por round.'
    Assert-Equal 'Arena Nova.round2-abc123.mp3' ([DreamMover.Helpers.MugenStageMusic]::ObterNomeDaFaixa('Arena Nova.def', 'nova.mp3', 2, 'abc123')) 'Um novo upload deve receber nome proprio para nao sobrescrever faixa atribuida a outro round.'
    Assert-Equal 3 ([DreamMover.Helpers.MugenStageMusic]::ListarFaixas($testDirectory, 'Arena Nova.def')).Count 'A pagina deve listar as tres musicas disponiveis.'

    [DreamMover.Helpers.MugenStageMusic]::CorrigirReferenciaNoDef($defPath, 'Arena Nova.round2.mp3', 1)
    [DreamMover.Helpers.MugenStageMusic]::CorrigirReferenciaNoDef($defPath, 'Arena Nova.mp3', 2)
    Assert-Equal 'Arena Nova.round2.mp3' ([DreamMover.Helpers.MugenStageMusic]::ObterArquivo($testDirectory, 'Arena Nova.def', 1)) 'A troca deve atribuir a faixa B ao round 1.'
    Assert-Equal 'Arena Nova.mp3' ([DreamMover.Helpers.MugenStageMusic]::ObterArquivo($testDirectory, 'Arena Nova.def', 2)) 'A troca deve atribuir a faixa A ao round 2.'

    [DreamMover.Helpers.MugenStageMusic]::RemoverReferenciaNoDef($defPath, 2)
    Assert-Equal $null ([DreamMover.Helpers.MugenStageMusic]::ObterArquivo($testDirectory, 'Arena Nova.def', 2)) 'Remover um round extra deve retirar sua atribuicao.'
    [DreamMover.Helpers.MugenStageMusic]::CorrigirReferenciaNoDef($defPath, 'Arena Nova.round3.ogg', 2)
    Assert-Equal 'Arena Nova.round3.ogg' ([DreamMover.Helpers.MugenStageMusic]::ObterArquivo($testDirectory, 'Arena Nova.def', 2)) 'Uma faixa existente deve poder ser usada em outro round.'
    Assert-Equal 'Arena Nova.round3.ogg' ([DreamMover.Helpers.MugenStageMusic]::ObterArquivo($testDirectory, 'Arena Nova.def', 3)) 'Usar uma faixa em outro round deve preservar o round de origem.'
    [DreamMover.Helpers.MugenStageMusic]::RemoverReferenciaNoDef($defPath, 2)
    [DreamMover.Helpers.MugenStageMusic]::RemoverReferenciaNoDef($defPath, 1)
    Assert-Equal $null ([DreamMover.Helpers.MugenStageMusic]::ObterArquivo($testDirectory, 'Arena Nova.def', 1)) 'Remover o round 1 nao deve ressuscitar o arquivo com nome-base.'
    Assert-Equal 1 ([DreamMover.Helpers.MugenStageMusic]::ListarFaixas($testDirectory, 'Arena Nova.def')).Count 'A pagina deve atualizar a lista apos a remocao.'

    $nestedDirectory = Join-Path $testDirectory 'pit'
    [System.IO.Directory]::CreateDirectory($nestedDirectory) | Out-Null
    $nestedDefPath = Join-Path $nestedDirectory 'pit.def'
    $nestedMusicPath = Join-Path $nestedDirectory 'pit.mp3'
    [System.IO.File]::WriteAllText($nestedDefPath, "[Music]`r`nbgmusic = sound/pit.mp3", [System.Text.Encoding]::Default)
    [System.IO.File]::WriteAllBytes($nestedMusicPath, [byte[]](4, 5, 6))

    Assert-Equal 'pit/pit.ogg' ([DreamMover.Helpers.MugenStageMusic]::ObterNomeNormalizado('pit/pit.def', 'tema.OGG')) 'A musica deve permanecer na pasta do .def.'
    Assert-Equal 'pit/pit.mp3' ([DreamMover.Helpers.MugenStageMusic]::ObterArquivo($testDirectory, 'pit/pit.def')) 'A musica de um cenario em subpasta deve ser localizada.'
    [DreamMover.Helpers.MugenStageMusic]::CorrigirReferenciaNoDef($nestedDefPath, 'pit/pit.mp3')
    $nestedUpdated = [System.IO.File]::ReadAllText($nestedDefPath, [System.Text.Encoding]::Default)
    if ($nestedUpdated -notmatch 'bgmusic = stages/pit/pit\.mp3') {
        throw 'A referencia bgmusic deve incluir a subpasta do cenario.'
    }

    $defSemMusica = Join-Path $testDirectory 'Sem Musica.def'
    [System.IO.File]::WriteAllText($defSemMusica, "[Info]`r`nname = Sem Musica", [System.Text.Encoding]::Default)
    [DreamMover.Helpers.MugenStageMusic]::CorrigirReferenciaNoDef($defSemMusica, 'Sem Musica.ogg')
    $updatedWithoutSection = [System.IO.File]::ReadAllText($defSemMusica, [System.Text.Encoding]::Default)

    if ($updatedWithoutSection -notmatch '\[Music\][\s\S]*bgmusic = stages/Sem Musica\.ogg') {
        throw 'A secao Music deve ser criada quando estiver ausente.'
    }
}
finally {
    if ([System.IO.Directory]::Exists($testDirectory)) {
        [System.IO.Directory]::Delete($testDirectory, $true)
    }
}

Write-Output 'MugenStageMusic: todos os testes passaram.'
