param([ValidateRange(1024,65535)][int]$Port = 55820)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path -LiteralPath (Join-Path $projectRoot 'ConnectionStrings.config'))) {
    throw 'Execute tools\Initialize-Demo.ps1 antes de iniciar a aplicacao.'
}
if (-not (Test-Path -LiteralPath (Join-Path $projectRoot 'bin\DreamMover.dll'))) {
    throw 'Compile DreamMover.sln no Visual Studio antes de iniciar a aplicacao.'
}
$iis = @(
    (Join-Path $env:ProgramFiles 'IIS Express\iisexpress.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'IIS Express\iisexpress.exe')
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $iis) { throw 'Instale o IIS Express pelo Visual Studio Installer.' }
$logs = Join-Path $projectRoot 'artifacts\iis'
[IO.Directory]::CreateDirectory($logs) | Out-Null
$process = Start-Process -FilePath $iis -ArgumentList "/path:`"$projectRoot`" /port:$Port /clr:v4.0" -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logs ('iis-' + $Port + '.log')) -RedirectStandardError (Join-Path $logs ('iis-' + $Port + '.error.log'))
Write-Output ('DreamMover: http://localhost:' + $Port)
Write-Output ('Processo IIS Express: ' + $process.Id + '. Para encerrar: Stop-Process -Id ' + $process.Id)
