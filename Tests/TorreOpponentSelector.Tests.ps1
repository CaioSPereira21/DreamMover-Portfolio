$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$selectorPath = Join-Path $projectRoot 'Services\TorreOpponentSelector.cs'

Add-Type -Path $selectorPath

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

function New-Candidate {
    param(
        [int]$Id,
        [int]$Rating,
        [bool]$Defeated = $false
    )

    $candidate = [DreamMover.Services.TowerFighterCandidate]::new()
    $candidate.Id = $Id
    $candidate.Nome = "Lutador $Id"
    $candidate.RatingElo = $Rating
    $candidate.Nivel = 1
    $candidate.JaDerrotado = $Defeated
    return $candidate
}

$candidates = [System.Collections.Generic.List[DreamMover.Services.TowerFighterCandidate]]::new()
1..8 | ForEach-Object {
    $candidates.Add((New-Candidate -Id $_ -Rating (700 + ($_ * 100)) -Defeated ($_ -le 6)))
}

$selected = [DreamMover.Services.TorreOpponentSelector]::Selecionar($candidates, 5)

Assert-Equal 5 $selected.Count 'A Torre Rapida deve possuir cinco adversarios.'
Assert-Equal 5 (($selected | Select-Object -ExpandProperty Id -Unique).Count) 'Os adversarios nao podem repetir.'

if (-not ($selected.Id -contains 7)) {
    throw 'Um adversario ainda nao derrotado deve ser priorizado.'
}

if (-not ($selected.Id -contains 8)) {
    throw 'O adversario mais dificil ainda nao derrotado deve entrar na torre.'
}

Assert-Equal 8 $selected[$selected.Count - 1].Id 'O adversario mais dificil deve ocupar o ultimo andar.'
Assert-Equal 'Chefe' $selected[$selected.Count - 1].Funcao 'O ultimo andar deve ser classificado como chefe.'
Assert-Equal 'Forte' $selected[$selected.Count - 2].Funcao 'O penultimo adversario deve ser classificado como forte.'

$idsOrdenados = @($selected | Select-Object -ExpandProperty Id)
for ($i = 1; $i -lt $selected.Count; $i++) {
    if ($selected[$i].RatingElo -lt $selected[$i - 1].RatingElo) {
        throw 'A dificuldade deve crescer ao longo da torre.'
    }
}

$insufficientRejected = $false
try {
    [DreamMover.Services.TorreOpponentSelector]::Selecionar($candidates.GetRange(0, 4), 5) | Out-Null
}
catch [System.ArgumentException] {
    $insufficientRejected = $true
}

Assert-Equal $true $insufficientRejected 'Universos com menos de cinco adversarios devem ser recusados.'

Write-Output 'TorreOpponentSelector: todos os testes passaram.'
