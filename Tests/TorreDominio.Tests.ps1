$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$dtoPath = Join-Path $projectRoot 'DTO\TorreDTO.cs'

Add-Type -Path $dtoPath

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

$dominio = [DreamMover.DTO.TorreDominioDTO]::new()
Assert-Equal $false $dominio.DominioCompleto 'Um universo sem elenco nao deve conceder a insignia de dominio.'

$dominio.TotalAtual = 5
$dominio.Derrotados = 4
Assert-Equal $false $dominio.DominioCompleto 'O dominio parcial nao deve conceder a insignia.'

$dominio.Derrotados = 5
Assert-Equal $true $dominio.DominioCompleto 'Derrotar todo o elenco deve conceder a insignia de dominio.'
Assert-Equal 100 $dominio.Percentual 'O dominio completo deve corresponder a 100 por cento.'

$dominio.TotalAtual = 6
Assert-Equal $false $dominio.DominioCompleto 'Um novo adversario deve desativar a insignia ate ser derrotado.'

Write-Output 'TorreDominio: todos os testes passaram.'
