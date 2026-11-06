$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$calculatorPath = Join-Path $projectRoot 'Services\ProgressionCalculator.cs'

Add-Type -Path $calculatorPath

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

function New-FighterState {
    param(
        [int]$Id,
        [long]$Experience = 0,
        [int]$Level = 1,
        [int]$Rating = 1000
    )

    $state = [DreamMover.Services.FighterProgressionState]::new()
    $state.Id = $Id
    $state.Nome = "Lutador $Id"
    $state.Experiencia = $Experience
    $state.Nivel = $Level
    $state.RatingElo = $Rating
    return $state
}

$homeState = New-FighterState -Id 1 -Experience 900
$awayState = New-FighterState -Id 2
$result = [DreamMover.Services.ProgressionCalculator]::Calcular($homeState, $awayState, 2, 0)

Assert-Equal 300 $result.Casa.ExperienciaGanha 'Vencedor perfeito deve receber participacao, rounds e bonus de vitoria.'
Assert-Equal 2 $result.Casa.NivelAtual 'O XP deve promover o lutador.'
Assert-Equal 200 $result.Casa.ExperienciaAtual 'O excedente de XP deve ser preservado.'
Assert-Equal 16 $result.Casa.VariacaoRatingElo 'Vitoria entre ratings iguais deve valer 16 pontos.'
Assert-Equal -16 $result.Visitante.VariacaoRatingElo 'Derrota entre ratings iguais deve retirar 16 pontos.'

$placementResult = [DreamMover.Services.ProgressionCalculator]::Calcular(
    (New-FighterState -Id 3),
    (New-FighterState -Id 4),
    2,
    0,
    [DreamMover.Services.ProgressionCalculator]::FatorKColocacao,
    [DreamMover.Services.ProgressionCalculator]::FatorK
)

Assert-Equal 48 $placementResult.Casa.VariacaoRatingElo 'O lutador em colocacao deve usar o fator K acelerado.'
Assert-Equal -16 $placementResult.Visitante.VariacaoRatingElo 'O adversario classificado deve manter o fator K normal.'

$homeState = New-FighterState -Id 1 -Experience 2900
$awayState = New-FighterState -Id 2
$result = [DreamMover.Services.ProgressionCalculator]::Calcular($homeState, $awayState, 2, 0)

Assert-Equal 3 $result.Casa.NivelAtual 'O calculo deve aceitar mais de uma promocao acumulada.'
Assert-Equal 200 $result.Casa.ExperienciaAtual 'O excedente deve atravessar multiplos niveis.'

$underdog = New-FighterState -Id 1 -Rating 800
$favorite = New-FighterState -Id 2 -Rating 1200
$upset = [DreamMover.Services.ProgressionCalculator]::Calcular($underdog, $favorite, 2, 1)

if ($upset.Casa.VariacaoRatingElo -le 16) {
    throw 'Uma zebra deve ganhar mais Elo que uma vitoria entre ratings iguais.'
}

Assert-Equal 300 $upset.Casa.ExperienciaGanha 'O XP de carreira nao deve depender do rating do adversario.'

$longMatch = [DreamMover.Services.ProgressionCalculator]::Calcular($homeState, $awayState, 3, 2)
Assert-Equal 375 $longMatch.Casa.ExperienciaGanha 'A progressao deve respeitar a quantidade de rounds configurada na engine.'
Assert-Equal 200 $longMatch.Visitante.ExperienciaGanha 'O perdedor deve receber XP pelos rounds vencidos.'

$invalidScoreRejected = $false
try {
    [DreamMover.Services.ProgressionCalculator]::Calcular($underdog, $favorite, 0, 0) | Out-Null
}
catch [System.ArgumentException] {
    $invalidScoreRejected = $true
}

Assert-Equal $true $invalidScoreRejected 'Placares incompletos devem ser rejeitados.'

Write-Output 'ProgressionCalculator: todos os testes passaram.'
