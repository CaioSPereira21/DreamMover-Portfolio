$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$binDirectory = Join-Path $projectRoot 'bin'
$dreamMoverAssembly = Join-Path $binDirectory 'DreamMover.dll'
$newtonsoftAssembly = Join-Path $binDirectory 'Newtonsoft.Json.dll'

if (-not [System.IO.File]::Exists($dreamMoverAssembly)) {
    throw 'Compile o projeto antes de executar os testes do piloto IKEMEN.'
}

[System.Reflection.Assembly]::LoadFrom($newtonsoftAssembly) | Out-Null
[System.Reflection.Assembly]::LoadFrom($dreamMoverAssembly) | Out-Null

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

function Set-PrivateProperty {
    param(
        [object]$Target,
        [string]$Name,
        [object]$Value
    )

    $property = $Target.GetType().GetProperty($Name)
    $rawValue = if ($null -eq $Value) { $null } else { $Value.PSObject.BaseObject }
    $property.GetSetMethod($true).Invoke($Target, [object[]]@($rawValue)) | Out-Null
}

$testDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("DreamMover-IkemenPilot-" + [Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($testDirectory) | Out-Null

try {
    $runnerType = [DreamMover.Services.IkemenPilotRunner]
    Assert-Equal 300000 $runnerType.GetField('DefaultExecutionTimeoutMilliseconds').GetRawConstantValue() 'O limite padrao da luta deve ser de cinco minutos.'

    $regexField = $runnerType.GetField('WinSideRegex', [System.Reflection.BindingFlags]'NonPublic, Static')
    $regex = $regexField.GetValue($null)
    $match = $regex.Match('["WinSide"] => 2')

    if (-not $match.Success) {
        throw 'O leitor do piloto nao reconheceu o campo WinSide do log do IKEMEN.'
    }

    Assert-Equal '2' $match.Groups[1].Value 'O lado vencedor deve ser extraido do log.'

    $winsField = $runnerType.GetField('WinsRegex', [System.Reflection.BindingFlags]'NonPublic, Static')
    $winsRegex = $winsField.GetValue($null)
    $winsLog = @'
["Wins"] => table: 0xc000000000 {
              [1] => 2
              [2] => 1
            }
["TeamModes"] => table: 0xc000000001 {
'@
    $winsMatch = $winsRegex.Match($winsLog)
    Assert-Equal $true $winsMatch.Success 'O leitor deve reconhecer o placar de rounds do IKEMEN.'
    Assert-Equal '2' $winsMatch.Groups[1].Value 'Os rounds do player 1 devem ser extraidos.'
    Assert-Equal '1' $winsMatch.Groups[2].Value 'Os rounds do player 2 devem ser extraidos.'

    $engineRoot = Join-Path $testDirectory 'engine'
    $abyssDirectory = Join-Path $engineRoot 'chars\Abyss'
    $kidGokuDirectory = Join-Path $engineRoot 'chars\kid goku v1'
    $stagesDirectory = Join-Path $engineRoot 'stages'
    [System.IO.Directory]::CreateDirectory($abyssDirectory) | Out-Null
    [System.IO.Directory]::CreateDirectory($kidGokuDirectory) | Out-Null
    [System.IO.Directory]::CreateDirectory($stagesDirectory) | Out-Null
    [System.IO.Directory]::CreateDirectory((Join-Path $engineRoot 'save')) | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $abyssDirectory 'Abyss.def'), '[Info]')
    [System.IO.File]::WriteAllText((Join-Path $kidGokuDirectory 'kid goku v1.def'), "[Info]`r`nname = Kid Goku Z2i`r`ndisplayname = Kid Goku V1")
    [System.IO.File]::WriteAllText((Join-Path $stagesDirectory 'Arena.def'), '[Info]')
    [System.IO.File]::WriteAllText((Join-Path $engineRoot 'save\config.ini'), "[Options]`r`nDifficulty = 7`r`nGameSpeed = 4`r`nTime = 60")

    $configuration = [DreamMover.Services.IkemenPilotConfiguration]::new()
    Set-PrivateProperty $configuration 'RootPath' $engineRoot
    Set-PrivateProperty $configuration 'Player1' 'Abyss'
    Set-PrivateProperty $configuration 'Player2' 'Abyss'
    Set-PrivateProperty $configuration 'IsConfigured' $true
    $runner = [DreamMover.Services.IkemenPilotRunner]::new($configuration, (Join-Path $testDirectory 'data'))

    $existsFighterMethod = $runnerType.GetMethod('ExisteLutador', [System.Reflection.BindingFlags]'NonPublic, Instance')
    $fighterArguments = [object[]]@('Abyss', $null)
    Assert-Equal $true $existsFighterMethod.Invoke($runner, $fighterArguments) 'O preflight deve localizar um lutador disponivel no IKEMEN.'
    Assert-Equal (Join-Path $abyssDirectory 'Abyss.def') $fighterArguments[1] 'O preflight deve informar o caminho do .def do lutador.'

    $existsStageMethod = $runnerType.GetMethod('ExisteCenario', [System.Reflection.BindingFlags]'NonPublic, Instance')
    $stageArguments = [object[]]@('stages/Arena.def', $null)
    Assert-Equal $true $existsStageMethod.Invoke($runner, $stageArguments) 'O preflight deve localizar um cenario disponivel no IKEMEN.'
    Assert-Equal (Join-Path $stagesDirectory 'Arena.def') $stageArguments[1] 'O preflight deve informar o caminho do .def do cenario.'

    $missingArguments = [object[]]@('Inexistente', $null)
    Assert-Equal $false $existsFighterMethod.Invoke($runner, $missingArguments) 'O preflight deve bloquear um lutador ausente antes de abrir o IKEMEN.'

    $aliasArguments = [object[]]@('kid goku', $null)
    Assert-Equal $true $existsFighterMethod.Invoke($runner, $aliasArguments) 'O preflight deve resolver diferencas seguras entre cadastro, pasta e displayname.'
    Assert-Equal (Join-Path $kidGokuDirectory 'kid goku v1.def') $aliasArguments[1] 'O resolvedor deve localizar o .def principal do alias.'

    $fighterReferenceMethod = $runnerType.GetMethod('ObterReferenciaLutador', [System.Reflection.BindingFlags]'NonPublic, Instance')
    $aliasPath = $aliasArguments[1].PSObject.BaseObject
    Assert-Equal 'kid goku v1/kid goku v1.def' $fighterReferenceMethod.Invoke($runner, [object[]]@($aliasPath)) 'O IKEMEN deve receber a referencia relativa do .def resolvido.'

    $extractVersionMethod = $runnerType.GetMethod('ExtrairVersao', [System.Reflection.BindingFlags]'NonPublic, Static')
    Assert-Equal 'v1.0.0-rc.3' $extractVersionMethod.Invoke($null, @("Check A`r`nVersion: v1.0.0-rc.3`r`nCheck B")) 'A versao deve ser lida da saida do motor.'

    $buildArgumentsMethod = $runnerType.GetMethod('MontarArgumentos', [System.Reflection.BindingFlags]'NonPublic, Static')
    $launchArguments = $buildArgumentsMethod.Invoke($null, @('Abyss', 'Kagami', 'stages/Arena.def', 'result.txt', 7, 'runtime-config.ini', $true))
    if ($launchArguments -notmatch '(?i)-nosound') {
        throw 'O torneio deve silenciar o som das lutas simultaneas.'
    }
    if ($launchArguments -notmatch '(?i)-windowed') {
        throw 'O piloto deve abrir em modo janela para depois maximiza-la.'
    }
    if ($launchArguments -notmatch '(?i)-p1\.ai\s+7' -or $launchArguments -notmatch '(?i)-p2\.ai\s+7') {
        throw 'O piloto deve usar a dificuldade salva no IKEMEN.'
    }
    if ($launchArguments -notmatch '(?i)-config\s+"runtime-config\.ini"') {
        throw 'Cada luta deve receber um perfil temporario isolado.'
    }
    if ($launchArguments -match '(?i)-(rounds|draws)\b') {
        throw 'O piloto deve respeitar rounds e empates configurados na engine.'
    }
    if ($launchArguments -match '(?i)-speedtest') {
        throw 'O piloto deve respeitar a velocidade configurada na engine.'
    }

    $soundArguments = $buildArgumentsMethod.Invoke($null, @('Abyss', 'Kagami', 'stages/Arena.def', 'result.txt', 7, 'runtime-config.ini', $false))
    if ($soundArguments -match '(?i)-nosound') {
        throw 'A partida executada com som nao deve receber o argumento -nosound.'
    }

    $aiLevelMethod = $runnerType.GetMethod('ObterNivelIa', [System.Reflection.BindingFlags]'NonPublic, Static')
    $engineConfigPath = (Join-Path $engineRoot 'save\config.ini').PSObject.BaseObject
    Assert-Equal 7 $aiLevelMethod.Invoke($null, [object[]]@($engineConfigPath)) 'A dificuldade deve ser lida do perfil salvo.'

    $createConfigMethod = $runnerType.GetMethod('CriarConfigTemporario', [System.Reflection.BindingFlags]'NonPublic, Instance')
    $temporaryConfigPath = $createConfigMethod.Invoke($runner, @('unit-test-profile'))
    Assert-Equal $true ([System.IO.File]::Exists($temporaryConfigPath)) 'O perfil isolado deve ser copiado antes da luta.'
    Assert-Equal 7 $aiLevelMethod.Invoke($null, [object[]]@($temporaryConfigPath)) 'O perfil isolado deve preservar a dificuldade salva.'

    $sourceConfig = [System.IO.File]::ReadAllText((Join-Path $engineRoot 'save\config.ini'))
    if ($sourceConfig -notmatch '(?im)^\s*GameSpeed\s*=\s*4\s*$') {
        throw 'A configuracao original da engine deve ser preservada.'
    }

    $deleteConfigMethod = $runnerType.GetMethod('ExcluirConfigTemporario', [System.Reflection.BindingFlags]'NonPublic, Static')
    $deleteConfigMethod.Invoke($null, [object[]]@($temporaryConfigPath)) | Out-Null
    Assert-Equal $false ([System.IO.File]::Exists($temporaryConfigPath)) 'O perfil isolado deve ser removido depois da luta.'

    $quadrantMethod = $runnerType.GetMethod('CalcularQuadrante', [System.Reflection.BindingFlags]'NonPublic, Static')
    $topLeft = $quadrantMethod.Invoke($null, @(0, 0, 1920, 1080, 0))
    $topRight = $quadrantMethod.Invoke($null, @(0, 0, 1920, 1080, 1))
    $bottomLeft = $quadrantMethod.Invoke($null, @(0, 0, 1920, 1080, 2))
    $bottomRight = $quadrantMethod.Invoke($null, @(0, 0, 1920, 1080, 3))
    Assert-Equal '0,0,960,540' ($topLeft -join ',') 'A primeira janela deve ocupar o canto superior esquerdo.'
    Assert-Equal '960,0,960,540' ($topRight -join ',') 'A segunda janela deve ocupar o canto superior direito.'
    Assert-Equal '0,540,960,540' ($bottomLeft -join ',') 'A terceira janela deve ocupar o canto inferior esquerdo.'
    Assert-Equal '960,540,960,540' ($bottomRight -join ',') 'A quarta janela deve ocupar o canto inferior direito.'

    $waitMethod = $runnerType.GetMethod('AguardarConclusao', [System.Reflection.BindingFlags]'NonPublic, Static')
    $stopInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $stopInfo.FileName = 'powershell.exe'
    $stopInfo.Arguments = '-NoProfile -Command "Start-Sleep -Seconds 10"'
    $stopInfo.UseShellExecute = $false
    $stopInfo.CreateNoWindow = $true
    $sleepingProcess = [System.Diagnostics.Process]::Start($stopInfo)
    try {
        $processStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        Assert-Equal $false $waitMethod.Invoke($null, @($sleepingProcess, $processStopwatch, 100)) 'Uma luta travada deve atingir o limite configurado.'

        $killMethod = $runnerType.GetMethod('EncerrarProcesso', [System.Reflection.BindingFlags]'NonPublic, Static')
        $killMethod.Invoke($null, @($sleepingProcess)) | Out-Null
        Assert-Equal $true $sleepingProcess.HasExited 'O processo da luta travada deve ser encerrado.'
    }
    finally {
        if (-not $sleepingProcess.HasExited) {
            $sleepingProcess.Kill()
        }
        $sleepingProcess.Dispose()
    }

    $registerErrorMethod = $runnerType.GetMethod('RegistrarErroExecucao', [System.Reflection.BindingFlags]'NonPublic, Static')
    $matchLogPath = (Join-Path $testDirectory 'data\logs\torneio-timeout.txt').PSObject.BaseObject
    $errorLogPath = $registerErrorMethod.Invoke($null, @(
        $matchLogPath,
        'Tempo limite excedido.',
        'Abyss',
        'Kid Goku',
        'stages/Arena.def',
        'Saida parcial'))
    Assert-Equal $true ([System.IO.File]::Exists($errorLogPath)) 'O timeout deve registrar um arquivo de diagnostico.'
    $errorLog = [System.IO.File]::ReadAllText($errorLogPath)
    if ($errorLog -notmatch 'Tempo limite excedido' -or
        $errorLog -notmatch 'Player 1: Abyss' -or
        $errorLog -notmatch 'Player 2: Kid Goku' -or
        $errorLog -notmatch 'Cenario: stages/Arena.def') {
        throw 'O diagnostico do timeout deve identificar a falha e o confronto afetado.'
    }
}
finally {
    if ([System.IO.Directory]::Exists($testDirectory)) {
        [System.IO.Directory]::Delete($testDirectory, $true)
    }
}

Write-Output 'IkemenPilot: todos os testes passaram.'
