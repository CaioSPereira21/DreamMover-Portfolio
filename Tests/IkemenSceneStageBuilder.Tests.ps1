$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$assemblyPath = Join-Path $projectRoot 'bin\DreamMover.dll'
$newtonsoftPath = Join-Path $projectRoot 'bin\Newtonsoft.Json.dll'

if (-not [System.IO.File]::Exists($assemblyPath)) {
    throw 'Compile o projeto antes de executar os testes de cenas do IKEMEN.'
}

[System.Reflection.Assembly]::LoadFrom($newtonsoftPath) | Out-Null
[System.Reflection.Assembly]::LoadFrom($assemblyPath) | Out-Null

function Assert-True {
    param([bool]$Value, [string]$Message)
    if (-not $Value) { throw $Message }
}

function Assert-Equal {
    param([object]$Expected, [object]$Actual, [string]$Message)
    if ($Expected -ne $Actual) { throw "$Message Esperado: $Expected. Atual: $Actual." }
}

function Set-UInt32 {
    param([byte[]]$Bytes, [int]$Offset, [uint32]$Value)
    [System.BitConverter]::GetBytes($Value).CopyTo($Bytes, $Offset)
}

function Set-UInt16 {
    param([byte[]]$Bytes, [int]$Offset, [uint16]$Value)
    [System.BitConverter]::GetBytes($Value).CopyTo($Bytes, $Offset)
}

function New-TestSffV1 {
    param([string]$Path, [uint16]$Group, [uint16]$Number)

    $bytes = New-Object byte[] 548
    [System.Text.Encoding]::ASCII.GetBytes("ElecbyteSpr`0").CopyTo($bytes, 0)
    $bytes[12] = 0
    $bytes[13] = 1
    $bytes[14] = 0
    $bytes[15] = 1
    Set-UInt32 $bytes 16 1
    Set-UInt32 $bytes 20 1
    Set-UInt32 $bytes 24 512
    Set-UInt32 $bytes 28 32
    Set-UInt32 $bytes 512 0
    Set-UInt32 $bytes 516 4
    Set-UInt16 $bytes 524 $Group
    Set-UInt16 $bytes 526 $Number
    Set-UInt16 $bytes 528 0
    $bytes[530] = 0
    $bytes[544] = 1
    $bytes[545] = 2
    $bytes[546] = 3
    $bytes[547] = 4
    [System.IO.File]::WriteAllBytes($Path, $bytes)
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("DreamMover-Scenes-" + [Guid]::NewGuid().ToString('N'))
$stagesPath = Join-Path $testRoot 'stages'
[System.IO.Directory]::CreateDirectory($stagesPath) | Out-Null

try {
    New-TestSffV1 (Join-Path $stagesPath 'Arena1.sff') 10 0
    New-TestSffV1 (Join-Path $stagesPath 'Arena2.sff') 20 0
    New-TestSffV1 (Join-Path $stagesPath 'Arena3.sff') 30 0

    $stage1 = @'
[Info]
name = Arena 1
[StageInfo]
localcoord = 320, 240
zoffset = 220
[BGDef]
spr = Arena1.sff
debugbg = 1
[BG Fundo]
type = anim
actionno = 0
start = 0, 0
[Begin Action 0]
10,0, 0,0, 1
'@
    $stage2 = @'
[Info]
name = Arena 2
[StageInfo]
localcoord = 320, 240
zoffset = 212
[BGDef]
spr = Arena2.sff
[BG Fundo]
type = anim
actionno = 0
start = 0, 0
[Begin Action 0]
20,0, 0,0, 1
'@
    $stage3 = @'
[Info]
name = Arena 3
[StageInfo]
localcoord = 320, 240
zoffset = 218
[BGDef]
spr = Arena3.sff
[BG Fundo]
type = anim
actionno = 0
start = 0, 4
[Begin Action 0]
30,0, 0,0, 1
'@
    [System.IO.File]::WriteAllText((Join-Path $stagesPath 'Arena1.def'), $stage1, [System.Text.Encoding]::Default)
    [System.IO.File]::WriteAllText((Join-Path $stagesPath 'Arena2.def'), $stage2, [System.Text.Encoding]::Default)
    [System.IO.File]::WriteAllText((Join-Path $stagesPath 'Arena3.def'), $stage3, [System.Text.Encoding]::Default)

    $json = @'
[
  {"Round":1,"CenarioNome":"Arena 1","Transicao":"corte","DuracaoFrames":0},
  {"Round":2,"CenarioNome":"Arena 2","Transicao":"queda","DuracaoFrames":60},
  {"Round":3,"CenarioNome":"Arena 3","Transicao":"fade","DuracaoFrames":45}
]
'@
    $catalogNames = [string[]]@('Arena 1', 'Arena 2', 'Arena 3')
    $normalized = [DreamMover.Services.CenarioSceneConfiguration]::ValidarESerializar($json, $catalogNames)
    Assert-True (-not [string]::IsNullOrWhiteSpace($normalized)) 'O roteiro valido deve ser serializado.'
    $scenes = [DreamMover.Services.CenarioSceneConfiguration]::Deserializar($normalized)
    Assert-Equal 3 $scenes.Count 'Os tres cenarios devem ser preservados.'
    Assert-Equal 'Arena 2' $scenes[1].CenarioNome 'O nome do catalogo deve identificar a cena.'
    Assert-Equal 'queda' $scenes[1].Transicao 'A transicao de queda configurada deve ser preservada.'
    Assert-Equal 'fade' $scenes[2].Transicao 'A transicao de fade configurada deve ser preservada.'

    $sources = New-Object 'System.Collections.Generic.List[DreamMover.Services.IkemenSceneStageSource]'
    $sources.Add([DreamMover.Services.IkemenSceneStageSource]@{
        Round = 1; Nome = 'Arena 1'; StageReference = 'Arena1.def'; Transicao = 'corte'; DuracaoFrames = 0
    })
    $sources.Add([DreamMover.Services.IkemenSceneStageSource]@{
        Round = 2; Nome = 'Arena 2'; StageReference = 'Arena2.def'; Transicao = 'queda'; DuracaoFrames = 60
    })
    $sources.Add([DreamMover.Services.IkemenSceneStageSource]@{
        Round = 3; Nome = 'Arena 3'; StageReference = 'Arena3.def'; Transicao = 'fade'; DuracaoFrames = 45
    })

    $runtime = [DreamMover.Services.IkemenSceneStageBuilder]::Preparar($testRoot, 'Arena1.def', $sources)
    try {
        Assert-True $runtime.IsGenerated 'Um roteiro deve gerar um stage temporario.'
        Assert-True ($runtime.StageArgument -match '^stages/__dreammover-.*-Arena1\.def$') 'O IKEMEN deve receber o stage temporario.'

        $generatedStagePath = Join-Path $testRoot ($runtime.StageArgument.Replace('/', '\'))
        Assert-True ([System.IO.File]::Exists($generatedStagePath)) 'O stage temporario deve existir durante a luta.'
        $generatedStage = [System.IO.File]::ReadAllText($generatedStagePath, [System.Text.Encoding]::Default)
        Assert-True ($generatedStage -match '(?im)^attachedChar\s*=\s*__dreammover-') 'O stage deve apontar para o controlador temporario.'
        Assert-True ($generatedStage -match '(?im)^debugbg\s*=\s*0\s*$') 'O fundo de diagnostico rosa deve ser desativado no stage temporario.'
        Assert-True ($generatedStage -match '(?im)^ID\s*=\s*800000\r?$') 'A primeira cena deve receber IDs internos automaticamente.'
        Assert-True ($generatedStage -match '(?im)^ID\s*=\s*800001\r?$') 'A segunda cena deve receber IDs internos automaticamente.'
        Assert-True ($generatedStage -match '(?im)^ID\s*=\s*800002\r?$') 'A terceira cena deve receber IDs internos automaticamente.'
        Assert-True ($generatedStage -match '(?im)^1\s*,\s*0\s*,') 'A animacao da segunda cena deve usar o grupo de sprites remapeado.'
        Assert-True ($generatedStage -match '(?im)^\[Begin Action 700001\]') 'As acoes dos stages devem ser isoladas entre si.'

        $runtimeDirectory = Get-ChildItem $stagesPath -Directory -Filter '__dreammover-*' | Select-Object -First 1
        Assert-True ($null -ne $runtimeDirectory) 'Os recursos temporarios devem ser criados.'
        $mergedSff = Join-Path $runtimeDirectory.FullName 'scenes.sff'
        Assert-True ([System.IO.File]::Exists($mergedSff)) 'O pacote visual combinado deve ser criado.'
        $mergedBytes = [System.IO.File]::ReadAllBytes($mergedSff)
        Assert-Equal 3 ([System.BitConverter]::ToUInt32($mergedBytes, 20)) 'O SFF combinado deve conter os sprites das tres cenas.'
        Assert-Equal 0 ([System.BitConverter]::ToUInt16($mergedBytes, 524)) 'O primeiro grupo deve ser remapeado.'
        $secondHeader = [System.BitConverter]::ToUInt32($mergedBytes, 512)
        Assert-Equal 1 ([System.BitConverter]::ToUInt16($mergedBytes, ([int]$secondHeader + 12))) 'O segundo grupo deve ser remapeado sem colisao.'

        $zss = [System.IO.File]::ReadAllText((Join-Path $runtimeDirectory.FullName 'controller.zss'))
        Assert-True ($zss -match 'modifyBGCtrl\{ID: 900001; value: 1\}') 'O round 2 deve ativar o segundo cenario.'
        Assert-True ($zss -match 'bgPalFX\{ID: 800002;') 'O terceiro cenario deve receber o fade configurado.'
        Assert-True ($zss -match 'roundNo = 1 && roundState = 4 && !matchOver') 'A queda deve ocorrer no intervalo anterior ao round configurado.'
        Assert-True ($zss -match '(?s)roundNo = 1 && roundState = 4.*?modifyBGCtrl\{ID: 900001; value: 0\}') 'O novo cenario deve permanecer oculto durante toda a queda.'
        Assert-True ($zss -match 'modifyStageBG\{ID: 800000; start\.y: 0 - \$dreamMoverDropOffset2\}') 'O cenario anterior deve descer junto com a plataforma na orientacao do IKEMEN.'
        Assert-True ($zss -notmatch 'modifyStageBG\{ID: 800001; start\.y: 248 [+-] \$dreamMoverDropOffset2\}') 'O novo cenario nao deve participar da queda.'
        Assert-True ($zss -match 'assertSpecial\{flag: roundnotover; flag2: roundnotskip\}') 'O intervalo deve aguardar a animacao de queda terminar.'
        Assert-True ($zss -match 'targetAdd\{playerid: player\(\$dreamMoverPlayer\), ID\}') 'Os lutadores reais devem ser selecionados para a queda.'
        Assert-True ($zss -match 'max\(fightScreenVar\(round\.over\.wintime\), 60\)') 'O anuncio do resultado deve permanecer visivel antes do salto.'
        Assert-True ($zss -match 'player\(\$dreamMoverPlayer\), win = 1') 'Somente o vencedor deve sair da pose de resultado e executar o salto.'
        Assert-True ($zss -match 'targetState\{value: 203\}') 'O vencedor deve permanecer neutro durante o anuncio do resultado.'
        Assert-True ($zss -match 'targetState\{value: 201\}') 'O vencedor deve entrar no estado de salto ao iniciar a quebra.'
        Assert-True ($zss -notmatch 'targetState\{value: 202\}') 'O derrotado nao deve ser levantado antes da quebra.'
        Assert-True ($zss -notmatch 'selfAnimExist\(5120\)') 'A animacao de levantar nao deve ser usada na transicao.'
        Assert-True ($zss -match '(?s)\[StateDef 201;.*?selfAnimExist\(40\).*?selfAnimExist\(41\).*?selfAnimExist\(44\)') 'O vencedor deve executar as fases de inicio, subida e descida do pulo.'
        Assert-True ($zss -match '(?s)player\(\$dreamMoverPlayer\), lose = 1.*?offset\{y: \$dreamMoverDropOffset2;') 'O derrotado deve continuar caido e acompanhar a plataforma.'
        Assert-True ($zss -match '(?s)player\(\$dreamMoverPlayer\), win = 1.*?offset\{y: \$dreamMoverJumpOffset2;') 'O vencedor deve seguir a trajetoria completa do salto.'
        Assert-True ($zss -match 'assertSpecial\{flag: nobardisplay; flag2: noBG; flag3: noFG\}') 'A interface e os cenarios devem desaparecer durante o apagao.'
        Assert-True ($zss -match 'allPalFX\{time: 2; mul: 0, 0, 0\}') 'A transicao deve terminar em tela preta antes do novo round.'
    }
    finally {
        $runtime.Dispose()
    }

    Assert-Equal 0 @(Get-ChildItem $stagesPath -Force | Where-Object { $_.Name -like '__dreammover-*' }).Count 'Os arquivos temporarios devem ser removidos ao final.'
    Assert-True ([System.IO.File]::Exists((Join-Path $stagesPath 'Arena1.def'))) 'O stage original deve permanecer intacto.'
    Assert-True ([System.IO.File]::ReadAllText((Join-Path $stagesPath 'Arena1.def')).Contains('debugbg = 1')) 'O modo de diagnostico do stage original nao deve ser alterado.'

    $invalidJson = '[{"Round":1,"CenarioNome":"Arena 1"},{"Round":2,"CenarioNome":"Inexistente"}]'
    $failed = $false
    try {
        [DreamMover.Services.CenarioSceneConfiguration]::ValidarESerializar($invalidJson, $catalogNames) | Out-Null
    }
    catch {
        $failed = $true
    }
    Assert-True $failed 'Um nome ausente do catalogo deve ser rejeitado.'
}
finally {
    if ([System.IO.Directory]::Exists($testRoot)) {
        [System.IO.Directory]::Delete($testRoot, $true)
    }
}

Write-Output 'IkemenSceneStageBuilder: todos os testes passaram.'
