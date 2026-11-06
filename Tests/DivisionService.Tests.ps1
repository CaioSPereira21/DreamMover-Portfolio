$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$entityFrameworkPath = Join-Path $projectRoot 'packages\EntityFramework.6.5.1\lib\net45\EntityFramework.dll'
$assemblyPath = Join-Path $projectRoot 'bin\DreamMover.dll'

[void][Reflection.Assembly]::LoadFrom($entityFrameworkPath)
[void][Reflection.Assembly]::LoadFrom($assemblyPath)

function Assert-Equal {
    param([object]$Expected, [object]$Actual, [string]$Message)

    if ($Expected -ne $Actual) {
        throw "$Message Esperado: $Expected. Atual: $Actual."
    }
}

$sequenceCases = @(
    @{ Current = 0; Won = $true; Expected = 1 },
    @{ Current = 4; Won = $true; Expected = 5 },
    @{ Current = 4; Won = $false; Expected = -1 },
    @{ Current = -4; Won = $false; Expected = -5 },
    @{ Current = -4; Won = $true; Expected = 1 },
    @{ Current = 10; Won = $true; Expected = 10 }
)

foreach ($case in $sequenceCases) {
    $actual = [DreamMover.Services.DivisionService]::AtualizarSequencia($case.Current, $case.Won)
    Assert-Equal $case.Expected $actual 'A sequência competitiva deve mudar de direção e respeitar os limites.'
}

$promotion = [DreamMover.Services.DivisionState]::new()
$promotion.DivisaoAtual = 3
$promotion.Sequencia = 5
Assert-Equal 'PROMOCAO' ([DreamMover.Services.DivisionService]::ObterTipoDesafio($promotion)) 'Cinco vitórias devem liberar promoção.'
Assert-Equal 2 ([DreamMover.Services.DivisionService]::ObterDivisaoDestino($promotion)) 'A promoção deve avançar uma divisão.'

$relegation = [DreamMover.Services.DivisionState]::new()
$relegation.DivisaoAtual = 2
$relegation.Sequencia = -5
Assert-Equal 'REBAIXAMENTO' ([DreamMover.Services.DivisionService]::ObterTipoDesafio($relegation)) 'Cinco derrotas devem liberar rebaixamento.'
Assert-Equal 3 ([DreamMover.Services.DivisionService]::ObterDivisaoDestino($relegation)) 'O rebaixamento deve recuar uma divisão.'

$fifthDivision = [DreamMover.Services.DivisionState]::new()
$fifthDivision.DivisaoAtual = 5
$fifthDivision.Sequencia = -5
Assert-Equal $null ([DreamMover.Services.DivisionService]::ObterTipoDesafio($fifthDivision)) 'Cinco derrotas na 5ª divisão não devem liberar a 6ª.'
$fifthDivision.Sequencia = -9
Assert-Equal $null ([DreamMover.Services.DivisionService]::ObterDivisaoMovimentoExtremo($fifthDivision)) 'Nove derrotas na 5ª divisão ainda não devem causar rebaixamento.'
$fifthDivision.Sequencia = -10
Assert-Equal 6 ([DreamMover.Services.DivisionService]::ObterDivisaoMovimentoExtremo($fifthDivision)) 'Dez derrotas na 5ª divisão devem rebaixar diretamente para a 6ª.'

$sixthDivision = [DreamMover.Services.DivisionState]::new()
$sixthDivision.DivisaoAtual = 6
$sixthDivision.Sequencia = 5
Assert-Equal 'PROMOCAO' ([DreamMover.Services.DivisionService]::ObterTipoDesafio($sixthDivision)) 'Cinco vitórias na 6ª divisão devem liberar o retorno à 5ª.'
Assert-Equal 5 ([DreamMover.Services.DivisionService]::ObterDivisaoDestino($sixthDivision)) 'A promoção da 6ª divisão deve ter a 5ª como destino.'

Assert-Equal 2 ([DreamMover.Services.DivisionService]::ObterProximaDivisaoTeste(3, $true)) 'Uma vitória na colocação deve elevar o próximo teste.'
Assert-Equal 4 ([DreamMover.Services.DivisionService]::ObterProximaDivisaoTeste(3, $false)) 'Uma derrota na colocação deve baixar o próximo teste.'
Assert-Equal 0 ([DreamMover.Services.DivisionService]::ObterProximaDivisaoTeste(0, $true)) 'O teste de colocação não deve ultrapassar a 0ª divisão.'
Assert-Equal 5 ([DreamMover.Services.DivisionService]::ObterProximaDivisaoTeste(5, $false)) 'A colocação nunca deve enviar o personagem para a 6ª divisão.'
Assert-Equal 6 ([DreamMover.Services.DivisionService]::DivisaoMaisBaixa) 'A 6ª divisão deve ser reconhecida como o limite inferior.'

$ratings = @(1545, 1300, 1299, 1200, 1199, 1100, 1099, 1000, 999, 900, 899, 650)
$expectedDivisions = @(0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5)
for ($index = 0; $index -lt $ratings.Count; $index++) {
    $actual = [DreamMover.Services.DivisionService]::ObterDivisaoInicial($ratings[$index])
    Assert-Equal $expectedDivisions[$index] $actual "Faixa incorreta para Elo $($ratings[$index])."
}

Assert-Equal 5 ([DreamMover.Services.DivisionService]::ObterDivisaoInicial(100)) 'A colocação por rating nunca deve atribuir diretamente a 6ª divisão.'

Write-Output 'DivisionService: todos os testes passaram.'
