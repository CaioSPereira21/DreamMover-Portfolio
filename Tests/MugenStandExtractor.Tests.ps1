$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$assemblyPath = Join-Path $projectRoot 'bin\DreamMover.dll'
$assembly = [System.Reflection.Assembly]::LoadFrom($assemblyPath)
$decoderType = $assembly.GetType('DreamMover.Helpers.SffV2SpriteSource', $true)
$decodeLz5 = $decoderType.GetMethod(
    'DecodeLz5',
    [System.Reflection.BindingFlags]'NonPublic,Static'
)

if ($null -eq $decodeLz5) {
    throw 'Nao foi possivel localizar o decodificador LZ5.'
}

# Dois pixels literais (1, 2), seguidos de uma referencia de distancia 2.
# O resultado precisa repetir os dois pixels na mesma ordem: 1, 2, 1, 2.
[byte[]]$compressed = 0x04, 0x21, 0x22, 0x01, 0x01
[byte[]]$actual = $decodeLz5.Invoke($null, @($compressed, 4))
[byte[]]$expected = 1, 2, 1, 2

$matches = $expected.Length -eq $actual.Length
for ($i = 0; $matches -and $i -lt $expected.Length; $i++) {
    $matches = $expected[$i] -eq $actual[$i]
}

if (-not $matches) {
    throw "A referencia LZ5 foi copiada da posicao errada. Esperado: $($expected -join ','). Atual: $($actual -join ',')."
}

$spriteType = $assembly.GetType('DreamMover.Helpers.IndexedSprite', $true)
$rendererType = $assembly.GetType('DreamMover.Helpers.StandRenderer', $true)
$paletteReaderType = $assembly.GetType('DreamMover.Helpers.PaletteReader', $true)
$choosePalette = $rendererType.GetMethod(
    'ChooseIndexedPalette',
    [System.Reflection.BindingFlags]'NonPublic,Static'
)
$reversePalette = $paletteReaderType.GetMethod(
    'ReversePalette',
    [System.Reflection.BindingFlags]'Public,Static'
)

[byte[]]$referencePalette = New-Object byte[] 768
$referencePalette[3] = 230
$referencePalette[4] = 40
$referencePalette[5] = 20
$referencePalette[6] = 20
$referencePalette[7] = 80
$referencePalette[8] = 230
$reverseArguments = New-Object object[] 1
$reverseArguments[0] = $referencePalette
[byte[]]$actPalette = $reversePalette.Invoke($null, $reverseArguments)
[byte[]]$pixels = 0, 1, 2
$spriteArguments = New-Object object[] 9
$spriteArguments[0] = 0
$spriteArguments[1] = 0
$spriteArguments[2] = 0
$spriteArguments[3] = 0
$spriteArguments[4] = 3
$spriteArguments[5] = 1
$spriteArguments[6] = $pixels
$spriteArguments[7] = $null
$spriteArguments[8] = [byte]0
$sprite = [Activator]::CreateInstance(
    $spriteType,
    $spriteArguments
)
$sprites = [Array]::CreateInstance($spriteType, 1)
$sprites.SetValue($sprite, 0)
$chooseArguments = New-Object object[] 5
$chooseArguments[0] = $sprites
$chooseArguments[1] = $actPalette
$chooseArguments[2] = $referencePalette
$chooseArguments[3] = $referencePalette
$chooseArguments[4] = $true
[byte[]]$selectedPalette = $choosePalette.Invoke(
    $null,
    $chooseArguments
)

for ($i = 0; $i -lt $referencePalette.Length; $i++) {
    if ($selectedPalette[$i] -ne $referencePalette[$i]) {
        throw 'A orientacao da paleta ACT nao respeitou a paleta de referencia do SFF v1.'
    }
}

$airReaderType = $assembly.GetType('DreamMover.Helpers.AirFileReader', $true)
$getActionFrames = $airReaderType.GetMethod(
    'GetActionFrames',
    [System.Reflection.BindingFlags]'Public,Static'
)
$getAction = $airReaderType.GetMethod(
    'GetAction',
    [System.Reflection.BindingFlags]'Public,Static'
)
$loopSelectorType = $assembly.GetType('DreamMover.Helpers.StandLoopSelector', $true)
$selectFrames = $loopSelectorType.GetMethod(
    'SelectFrames',
    [System.Reflection.BindingFlags]'Public,Static'
)
$temporaryAirPath = [System.IO.Path]::GetTempFileName()

try {
    [System.IO.File]::WriteAllText(
        $temporaryAirPath,
        @'
[Begin Action 5]
5,0, 0,0, 1

[Begin Action 0]
0,0, 0,0, 8
0,1, 0,0, 8

[Begin Action 10]
10,0, 0,0, 1

[Begin Action 0]
1100,0, 0,0, 8
'@
    )

    $frames = $getActionFrames.Invoke($null, @($temporaryAirPath, 0))
    if ($frames.Count -ne 2) {
        throw "A primeira Action 0 deveria produzir 2 frames, mas produziu $($frames.Count)."
    }

    if ($frames[0].Group -ne 0 -or $frames[0].Image -ne 0 -or
        $frames[1].Group -ne 0 -or $frames[1].Image -ne 1) {
        throw 'O leitor misturou frames de uma Action 0 duplicada.'
    }

    [System.IO.File]::WriteAllText(
        $temporaryAirPath,
        @'
[Begin Action 0]
0,0, 0,0, 4
0,1, 0,0, 8
0,0, 0,0, 6
0,2, 0,0, 6
LoopStart
0,3, 0,0, 6
'@
    )

    $action = $getAction.Invoke($null, @($temporaryAirPath, 0))
    if ($action.LoopStartFrameIndex -ne 4) {
        throw "O LoopStart deveria apontar para o frame 4, mas apontou para $($action.LoopStartFrameIndex)."
    }

    $selectArguments = New-Object object[] 1
    $selectArguments[0] = $action
    $selectedFrames = $selectFrames.Invoke($null, $selectArguments)
    if ($selectedFrames.Count -ne 3 -or $selectedFrames[2].Image -ne 0) {
        throw 'O loop estatico deveria preservar o ciclo animado que fecha antes da transicao final.'
    }

    [System.IO.File]::WriteAllText(
        $temporaryAirPath,
        @'
[Begin Action 0]
0,0, 0,0, 4
LoopStart
0,1, 0,0, 8
0,2, 0,0, 8
'@
    )

    $action = $getAction.Invoke($null, @($temporaryAirPath, 0))
    $selectArguments[0] = $action
    $selectedFrames = $selectFrames.Invoke($null, $selectArguments)
    if ($selectedFrames.Count -ne 2 -or
        $selectedFrames[0].Image -ne 1 -or
        $selectedFrames[1].Image -ne 2) {
        throw 'O trecho animado depois de LoopStart deveria ser usado como loop do GIF.'
    }
}
finally {
    Remove-Item -LiteralPath $temporaryAirPath -Force -ErrorAction SilentlyContinue
}

Write-Output 'MugenStandExtractor: todos os testes passaram.'
