$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$servicePath = Join-Path $projectRoot 'Services\MugenCharacterSecurityService.cs'
Add-Type -Path $servicePath

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-False {
    param([bool]$Condition, [string]$Message)
    if ($Condition) { throw $Message }
}

function Assert-Equal {
    param([object]$Expected, [object]$Actual, [string]$Message)
    if ($Expected -ne $Actual) { throw "$Message Esperado: $Expected. Atual: $Actual." }
}

function New-CharacterFiles {
    param(
        [string]$Root,
        [string]$CnsContent,
        [string]$CnsReference = 'fighter.cns'
    )

    [System.IO.Directory]::CreateDirectory($Root) | Out-Null
    $defPath = Join-Path $Root 'fighter.def'
    [System.IO.File]::WriteAllText($defPath, "[Info]`r`nname = Fighter`r`n[Files]`r`ncns = $CnsReference", [System.Text.Encoding]::Default)
    if ($CnsReference -notmatch '^\.\.') {
        $cnsPath = Join-Path $Root $CnsReference
        $cnsFolder = Split-Path -Parent $cnsPath
        [System.IO.Directory]::CreateDirectory($cnsFolder) | Out-Null
        [System.IO.File]::WriteAllText($cnsPath, $CnsContent, [System.Text.Encoding]::Default)
    }
    return $defPath
}

$testDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("DreamMover-CharacterSecurity-" + [Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($testDirectory) | Out-Null

try {
    $safeDef = New-CharacterFiles (Join-Path $testDirectory 'safe') @'
[Data]
life = 1000
power = 3000
attack = 100
defence = 100

[Size]
xscale = 1
yscale = 1

[Velocity]
attack = 999 ; nao pode ser confundido com o atributo de ataque
'@
    $safe = [DreamMover.Services.MugenCharacterSecurityService]::Inspect($safeDef)
    Assert-True $safe.Success 'Um CNS valido deve ser inspecionado.'
    Assert-True $safe.Reliable 'Valores numericos devem produzir verificacao confiavel.'
    Assert-True $safe.Safe 'Valores padrao devem ser considerados seguros.'
    Assert-False $safe.HasHigherAttribute 'Ocorrencias fora de [Data] devem ser ignoradas.'
    Assert-Equal 6 $safe.Attributes.Count 'A verificacao deve incluir os quatro atributos de Data e os dois de Size.'

    $strongDef = New-CharacterFiles (Join-Path $testDirectory 'strong') @'
[Data]
life = 1250
power = 4000
attack = 135
defense = 115 ; aceita a grafia alternativa

[Size]
xscale = 1.25
yscale = 0.75
'@
    $strong = [DreamMover.Services.MugenCharacterSecurityService]::Inspect($strongDef)
    Assert-True $strong.Success 'A grafia defense deve ser aceita.'
    Assert-False $strong.Safe 'Atributos acima do padrao nao podem ser considerados seguros.'
    Assert-True $strong.HasHigherAttribute 'A verificacao deve sinalizar vantagem acima do padrao.'
    Assert-Equal 4 ($strong.Attributes | Where-Object { $_.HigherThanStandard }).Count 'Somente os quatro atributos com limites padrao devem ser sinalizados.'
    Assert-False ($strong.Attributes | Where-Object { $_.Key -eq 'xscale' }).HigherThanStandard 'X Scale nao deve gerar alerta por nao possuir limite padrao.'
    Assert-False ($strong.Attributes | Where-Object { $_.Key -eq 'yscale' }).HigherThanStandard 'Y Scale nao deve gerar alerta quando estiver abaixo de 1.'

    $defaultsDef = New-CharacterFiles (Join-Path $testDirectory 'defaults') "[Data]`r`nattack = 90"
    $defaults = [DreamMover.Services.MugenCharacterSecurityService]::Inspect($defaultsDef)
    Assert-True $defaults.Safe 'Campos ausentes devem usar os valores padrao do motor.'
    Assert-Equal 1000 ($defaults.Attributes | Where-Object { $_.Key -eq 'life' }).Value 'Life ausente deve assumir o padrao 1000.'
    Assert-False ($defaults.Attributes | Where-Object { $_.Key -eq 'life' }).Informed 'O resultado deve indicar que Life nao foi informado.'
    Assert-Equal 1 ($defaults.Attributes | Where-Object { $_.Key -eq 'xscale' }).Value 'X Scale ausente deve assumir o padrao 1.'

    $invalidDef = New-CharacterFiles (Join-Path $testDirectory 'invalid') "[Data]`r`nlife = muito`r`npower = NaN"
    $invalid = [DreamMover.Services.MugenCharacterSecurityService]::Inspect($invalidDef)
    Assert-True $invalid.Success 'O arquivo foi lido mesmo quando um valor e invalido.'
    Assert-False $invalid.Reliable 'Valor nao numerico deve tornar a verificacao inconclusiva.'
    Assert-True $invalid.HasInvalidValue 'Valores nao numericos ou nao finitos devem ser explicitamente sinalizados.'
    Assert-Equal 2 ($invalid.Attributes | Where-Object { -not $_.Valid }).Count 'Texto e NaN devem ser rejeitados.'

    $updateDef = New-CharacterFiles (Join-Path $testDirectory 'update') @'
[Data]
life = 900 ; manter comentario
power = 2500
attack = 90
defense = 95
'@
    $updatedValues = New-Object 'System.Collections.Generic.Dictionary[string,double]'
    $updatedValues.Add('life', 1100)
    $updatedValues.Add('attack', 105.5)
    $updatedValues.Add('defence', 101)
    $updatedValues.Add('xscale', 1.2)
    $updatedValues.Add('yscale', 0.85)
    [DreamMover.Services.MugenCharacterSecurityService]::UpdateEditableAttributes($updateDef, $updatedValues)

    $updated = [DreamMover.Services.MugenCharacterSecurityService]::Inspect($updateDef)
    Assert-Equal 1100 ($updated.Attributes | Where-Object { $_.Key -eq 'life' }).Value 'Life deve ser atualizado.'
    Assert-Equal 105.5 ($updated.Attributes | Where-Object { $_.Key -eq 'attack' }).Value 'Attack deve aceitar casas decimais.'
    Assert-Equal 101 ($updated.Attributes | Where-Object { $_.Key -eq 'defence' }).Value 'Defense deve ser atualizado mesmo com a grafia alternativa no arquivo.'
    Assert-Equal 1.2 ($updated.Attributes | Where-Object { $_.Key -eq 'xscale' }).Value 'X Scale deve ser criado na secao Size.'
    Assert-Equal 0.85 ($updated.Attributes | Where-Object { $_.Key -eq 'yscale' }).Value 'Y Scale deve ser criado na secao Size.'
    Assert-Equal 2500 ($updated.Attributes | Where-Object { $_.Key -eq 'power' }).Value 'Power deve permanecer inalterado e somente para consulta.'

    $updatedCns = [System.IO.File]::ReadAllText((Join-Path (Split-Path -Parent $updateDef) 'fighter.cns'), [System.Text.Encoding]::Default)
    Assert-True ($updatedCns -match 'life\s*=\s*1100\s+; manter comentario') 'Comentarios existentes devem ser preservados.'
    Assert-True ($updatedCns -match '(?m)^\[Size\]\r?$') 'A secao Size deve ser criada quando estiver ausente.'

    $escapeDef = New-CharacterFiles (Join-Path $testDirectory 'escape') '[Data]' '..\outside.cns'
    $escape = [DreamMover.Services.MugenCharacterSecurityService]::Inspect($escapeDef)
    Assert-False $escape.Success 'A verificacao nao deve ler CNS fora da pasta do personagem.'
}
finally {
    if ([System.IO.Directory]::Exists($testDirectory)) {
        [System.IO.Directory]::Delete($testDirectory, $true)
    }
}

Write-Output 'MugenCharacterSecurityService: todos os testes passaram.'
