$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
foreach ($test in (Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Tests') -Filter '*.Tests.ps1' | Sort-Object Name)) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $test.FullName
    if ($LASTEXITCODE -ne 0) { throw ('Falha: ' + $test.Name) }
}
& node (Join-Path $projectRoot 'Tests\PartidaAutomaticSelection.Tests.js')
if ($LASTEXITCODE -ne 0) { throw 'Falha no teste de selecao de partidas.' }
Write-Output 'Todos os testes da edicao de portfolio passaram.'
