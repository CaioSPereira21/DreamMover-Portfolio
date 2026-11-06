$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$calculatorPath = Join-Path $projectRoot 'Services\ProgressionCalculator.cs'
$historyPath = Join-Path $projectRoot 'Services\EloHistoryService.cs'
$partidaControllerPath = Join-Path $projectRoot 'Controllers\PartidaController.cs'

Add-Type -Path @($calculatorPath, $historyPath)

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

function New-HistoryMatch {
    param(
        [int]$Id,
        [datetime]$Date,
        [int]$WinnerId,
        [int]$LoserId,
        [int]$WinnerScore = 2,
        [int]$LoserScore = 0
    )

    $match = [DreamMover.Services.EloHistoryMatch]::new()
    $match.Id = $Id
    $match.DataPartida = $Date
    $match.IdVencedor = $WinnerId
    $match.IdPerdedor = $LoserId
    $match.PontosVencedor = $WinnerScore
    $match.PontosPerdedor = $LoserScore
    return $match
}

$matches = [System.Collections.Generic.List[DreamMover.Services.EloHistoryMatch]]::new()
$matches.Add((New-HistoryMatch -Id 2 -Date ([datetime]'2026-01-02') -WinnerId 2 -LoserId 1 -WinnerScore 2 -LoserScore 1))
$matches.Add((New-HistoryMatch -Id 1 -Date ([datetime]'2026-01-01') -WinnerId 1 -LoserId 2))
$matches.Add((New-HistoryMatch -Id 3 -Date ([datetime]'2026-01-03') -WinnerId 3 -LoserId 2))

$history = [DreamMover.Services.EloHistoryService]::Calcular($matches, 1)

Assert-Equal 2 $history.Count 'O historico deve conter somente as lutas do personagem consultado.'
Assert-Equal 1 $history[0].PartidaId 'As partidas devem ser reconstruidas em ordem cronologica.'
Assert-Equal 1016 $history[0].RatingEloAtual 'A primeira vitoria entre ratings iguais deve levar o Elo a 1016.'
Assert-Equal 2 $history[0].AdversarioId 'O historico deve identificar o adversario.'
Assert-Equal $true $history[0].Vitoria 'O resultado deve marcar a vitoria.'
Assert-Equal 2 $history[0].PontosLutador 'O placar deve ficar na perspectiva do lutador.'
Assert-Equal 1 $history[1].PontosLutador 'O placar de derrota deve ficar na perspectiva do lutador.'
Assert-Equal $false $history[1].Vitoria 'O segundo resultado deve marcar a derrota.'
Assert-Equal -17 $history[1].VariacaoRatingElo 'A revanche do azarão deve retirar 17 pontos do favorito.'
Assert-Equal 999 $history[1].RatingEloAtual 'O Elo final deve refletir a sequencia completa.'

$alignedHistory = [DreamMover.Services.EloHistoryService]::Calcular($matches, 1, 1005)
Assert-Equal 1000 $alignedHistory[0].RatingEloAnterior 'A reconciliacao deve preservar o Elo inicial.'
Assert-Equal 1005 $alignedHistory[1].RatingEloAtual 'O ultimo ponto deve coincidir com o rating atual persistido.'

$partidaController = Get-Content -Raw $partidaControllerPath
if (-not $partidaController.Contains('lutador.HistoricoRatingElo = ObterHistoricoRatingElo(lutador.Id, lutador.RatingElo);')) {
    throw 'A tela de cadastro de partida deve preencher o historico de Elo antes de abrir o perfil.'
}

Write-Output 'EloHistoryService: todos os testes passaram.'
