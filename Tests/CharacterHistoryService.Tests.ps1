$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Add-Type -Path (Join-Path $projectRoot 'bin/Newtonsoft.Json.dll')
Add-Type -Path (Join-Path $projectRoot 'bin/DreamMover.dll')
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('DreamMover-History-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
function Assert-Equal($Expected, $Actual, [string]$Message) {
    if ($Expected -cne $Actual) { throw "$Message Expected: $Expected; actual: $Actual" }
}
function New-Snapshot([int]$Id = 42) {
    $snapshot = New-Object DreamMover.Services.CharacterHistorySnapshot
    $snapshot.Id = $Id
    [DreamMover.Services.CharacterHistoryService]::Set($snapshot, 'Nome', 'Gotenks')
    [DreamMover.Services.CharacterHistoryService]::Set($snapshot, 'Autor', 'Team Z2')
    [DreamMover.Services.CharacterHistoryService]::Set($snapshot, 'Vida', '1000')
    return $snapshot
}
try {
    $first = New-Snapshot
    $doc = [DreamMover.Services.CharacterHistoryService]::Record($testRoot, $first, 'Cadastro', 'Teste')
    Assert-Equal 1 $doc.Eventos.Count 'Baseline must be recorded once.'
    Assert-Equal $true $doc.Eventos[0].Inicial 'Baseline must be identified.'
    $journal = Join-Path $testRoot '42/history.json'
    $unchangedTime = [DateTime]::UtcNow.AddDays(-1)
    [IO.File]::SetLastWriteTimeUtc($journal, $unchangedTime)
    $doc = [DreamMover.Services.CharacterHistoryService]::Record($testRoot, (New-Snapshot), 'Editar', 'Teste')
    Assert-Equal 1 $doc.Eventos.Count 'No-op save must not create another event.'
    Assert-Equal $unchangedTime ([IO.File]::GetLastWriteTimeUtc($journal)) 'An identical snapshot must not rewrite the journal.'
    $edited = New-Snapshot
    [DreamMover.Services.CharacterHistoryService]::Set($edited, 'Vida', '900')
    [DreamMover.Services.CharacterHistoryService]::Set($edited, 'Autor', 'Novo autor')
    $doc = [DreamMover.Services.CharacterHistoryService]::Record($testRoot, $edited, 'Edição do cadastro', 'Teste')
    Assert-Equal 2 $doc.Eventos.Count 'One edit must produce one event.'
    Assert-Equal 2 $doc.Eventos[1].Campos.Count 'Only changed fields must be included.'
    $life = $doc.Eventos[1].Campos | Where-Object Campo -eq Vida
    Assert-Equal '1000' $life.Anterior 'Old life is preserved.'
    Assert-Equal '900' $life.Atual 'New life is preserved.'
    $reloaded = [DreamMover.Services.CharacterHistoryService]::Read($testRoot, 42)
    Assert-Equal 2 $reloaded.Eventos.Count 'History survives reload.'
    $search = [DreamMover.Services.CharacterHistoryService]::RecordUpdateSearch($testRoot, 42, '2026-09-08', 'Comparada a IA atual.', 'https://example.com/baby', 'Caio')
    Assert-Equal '2026-09-08' $search.DataBusca 'Search date is persisted.'
    Assert-Equal 'Caio' $search.Responsavel 'Search responsible person is persisted.'
    $reloaded = [DreamMover.Services.CharacterHistoryService]::Read($testRoot, 42)
    Assert-Equal 'Comparada a IA atual.' $reloaded.UltimaBuscaAtualizacoes.Observacao 'Search notes survive reload.'
    Assert-Equal 'https://example.com/baby' $reloaded.UltimaBuscaAtualizacoes.Fonte 'Search source survives reload.'
    Assert-Equal 1 ([DreamMover.Services.CharacterHistoryService]::Record($testRoot, (New-Snapshot 43), 'Cadastro', 'Teste')).Eventos.Count 'Characters must have independent histories.'

    $image = Join-Path $testRoot 'portrait.png'
    [IO.File]::WriteAllBytes($image, [byte[]](1, 2, 3))
    [DreamMover.Services.CharacterHistoryService]::CaptureImage($testRoot, $edited, 'Retrato', $testRoot, 'portrait.png')
    $oldImage = $edited.Valores['Retrato'].Imagem
    [DreamMover.Services.CharacterHistoryService]::Record($testRoot, $edited, 'Imagem', 'Teste') | Out-Null
    [IO.File]::WriteAllBytes($image, [byte[]](7, 8, 9))
    [DreamMover.Services.CharacterHistoryService]::CaptureImage($testRoot, $edited, 'Retrato', $testRoot, 'portrait.png')
    $doc = [DreamMover.Services.CharacterHistoryService]::Record($testRoot, $edited, 'Imagem', 'Teste')
    $change = $doc.Eventos[$doc.Eventos.Count - 1].Campos[0]
    Assert-Equal 'portrait.png' $change.Anterior 'Filename stays unchanged.'
    Assert-Equal $oldImage $change.ImagemAnterior 'An overwrite must retain the old image.'
    if ($change.ImagemAnterior -eq $change.ImagemAtual) { throw 'Changed content must have different image IDs.' }
    $oldBytes = [IO.File]::ReadAllBytes([DreamMover.Services.CharacterHistoryService]::ImagePath($testRoot, 42, $oldImage))
    Assert-Equal 1 ([int]$oldBytes[0]) 'The archived image must retain its original bytes.'
    $beforeCount = $doc.Eventos.Count
    $unknown = New-Snapshot
    $unknown.Valores.Remove('Vida') | Out-Null
    [DreamMover.Services.CharacterHistoryService]::Set($unknown, 'Autor', 'Novo autor')
    $doc = [DreamMover.Services.CharacterHistoryService]::Record($testRoot, $unknown, 'Consulta', 'Teste')
    Assert-Equal $beforeCount $doc.Eventos.Count 'Unavailable attributes/images must not erase previous values.'

    $character = Join-Path $testRoot 'fighter'
    [IO.Directory]::CreateDirectory($character) | Out-Null
    $def = Join-Path $character 'fighter.def'
    $cns = Join-Path $character 'fighter.cns'
    [IO.File]::WriteAllText($def, "[Info]`r`nauthor = Team Z2`r`n[Files]`r`ncns = fighter.cns")
    [IO.File]::WriteAllText($cns, "[Data]`r`nlife = 950`r`nattack = 90`r`ndefence = 110")
    $fileSnapshot = New-Snapshot 44
    [DreamMover.Services.CharacterHistoryService]::CaptureDefinition($fileSnapshot, $def)
    Assert-Equal '950' $fileSnapshot.Valores['Vida'].Valor 'Life must come from the real CNS.'
    Assert-Equal '90' $fileSnapshot.Valores['Ataque'].Valor 'Attack must come from the real CNS.'
    Assert-Equal '110' $fileSnapshot.Valores['Defesa'].Valor 'Defense must come from the real CNS.'
    [DreamMover.Services.CharacterHistoryService]::Record($testRoot, $fileSnapshot, 'Cadastro', 'Teste') | Out-Null
    [IO.File]::AppendAllText($cns, "`r`n; Updated behavior")
    [DreamMover.Services.CharacterHistoryService]::CaptureDefinition($fileSnapshot, $def)
    $doc = [DreamMover.Services.CharacterHistoryService]::Record($testRoot, $fileSnapshot, 'Arquivo externo', 'Teste')
    Assert-Equal 1 $doc.Eventos[1].Campos.Count 'File changes without stat changes must still be visible.'

    [IO.File]::WriteAllText($def, "[Info]`r`nauthor = Team Z2`r`n[Files]`r`ncns = fighter.cns`r`nst1 = missing.cns")
    $fileSnapshot = New-Snapshot 44
    [DreamMover.Services.CharacterHistoryService]::CaptureDefinition($fileSnapshot, $def)
    $doc = [DreamMover.Services.CharacterHistoryService]::Record($testRoot, $fileSnapshot, 'Arquivo externo', 'Teste')
    Assert-Equal 'Arquivo ausente' $doc.Atual.Valores['Arquivo: missing.cns'].Valor 'Missing referenced files must be explicit.'
    [IO.File]::WriteAllText($def, "[Info]`r`nauthor = Team Z2`r`n[Files]`r`ncns = fighter.cns")
    $fileSnapshot = New-Snapshot 44
    [DreamMover.Services.CharacterHistoryService]::CaptureDefinition($fileSnapshot, $def)
    $doc = [DreamMover.Services.CharacterHistoryService]::Record($testRoot, $fileSnapshot, 'Arquivo externo', 'Teste')
    $removed = $doc.Eventos[$doc.Eventos.Count - 1].Campos | Where-Object Campo -eq 'Arquivo: missing.cns'
    Assert-Equal 'Arquivo ausente' $removed.Anterior 'Removed references must preserve their previous status.'
    Assert-Equal '' $removed.Atual 'Removed references must be recorded.'
    $beforeCount = $doc.Eventos.Count
    $fileSnapshot = New-Snapshot 44
    [DreamMover.Services.CharacterHistoryService]::CaptureDefinition($fileSnapshot, $def)
    $doc = [DreamMover.Services.CharacterHistoryService]::Record($testRoot, $fileSnapshot, 'Consulta', 'Teste')
    Assert-Equal $beforeCount $doc.Eventos.Count 'A removed reference must not create repeated events.'

    Assert-Equal 'atributos' ([DreamMover.Services.CharacterHistoryService]::Category('Vida')) 'Stats category.'
    Assert-Equal 'imagens' ([DreamMover.Services.CharacterHistoryService]::Category('Retrato')) 'Image category.'
    Assert-Equal 'cadastro' ([DreamMover.Services.CharacterHistoryService]::Category('Autor')) 'Author category.'
    Assert-Equal 'arquivos' ([DreamMover.Services.CharacterHistoryService]::Category('Arquivo: fighter.cns')) 'File category.'
    $filtered = [DreamMover.Services.CharacterHistoryService]::FilterEvents($doc, 'arquivos', ' MISSING.CNS ', $null)
    Assert-Equal 2 $filtered.Count 'Search must match removed references, ignore case and trim whitespace.'
    Assert-Equal 0 ([DreamMover.Services.CharacterHistoryService]::FilterEvents($doc, 'imagens', 'missing.cns', $null)).Count 'Category and search must combine.'
    $authorEvents = [DreamMover.Services.CharacterHistoryService]::FilterEvents($reloaded, 'cadastro', 'Team Z2', $null)
    Assert-Equal 2 $authorEvents.Count 'Search must include previous field values.'

    $series = New-Object DreamMover.Services.CharacterHistoryDocument
    for ($index = 1; $index -le 26; $index++) {
        $entry = New-Object DreamMover.Services.CharacterHistoryEvent
        $entry.Id = [string]$index
        $entry.Origem = 'Edi' + [char]0xe7 + [char]0xe3 + 'o'
        $entry.Responsavel = 'Teste'
        $entry.Campos = New-Object 'System.Collections.Generic.List[DreamMover.Services.CharacterHistoryChange]'
        $series.Eventos.Add($entry)
    }
    $page = [DreamMover.Services.CharacterHistoryService]::FilterEvents($series, 'todos', 'edicao', '25')
    Assert-Equal 25 $page.Count 'An anchor excludes events appended after the current consultation; accent-insensitive search must work.'
    Assert-Equal '25' $page[0].Id 'Newest anchored event comes first.'
    Assert-Equal '5' $page[20].Id 'The second page stays stable after a new event.'
    Assert-Equal '1' $page[24].Id 'Oldest entry is preserved.'
    Assert-Equal 26 $series.Eventos.Count 'Filtering never mutates the journal.'
    $rejected = $false
    try { [DreamMover.Services.CharacterHistoryService]::FilterEvents($series, 'todos', '', 'unknown') | Out-Null } catch { $rejected = $true }
    Assert-Equal $true $rejected 'An unknown anchor must not silently skip events.'

    $rejected = $false
    try { [DreamMover.Services.CharacterHistoryService]::ImagePath($testRoot, 42, '../../Web.config') | Out-Null } catch { $rejected = $true }
    Assert-Equal $true $rejected 'Image endpoints must reject path traversal.'
    $corrupt = Join-Path $testRoot '43/history.json'
    [IO.File]::WriteAllText($corrupt, 'broken-json')
    $rejected = $false
    try { [DreamMover.Services.CharacterHistoryService]::Record($testRoot, (New-Snapshot 43), 'Editar', 'Teste') | Out-Null } catch { $rejected = $true }
    Assert-Equal $true $rejected 'Corrupt history must not be silently reset.'
    Assert-Equal 'broken-json' ([IO.File]::ReadAllText($corrupt)) 'Corrupt history must be preserved for recovery.'
    Write-Output 'PASS: baseline, no-op without disk rewrite, before/after, persistence, isolation, image archive, missing data, CNS values, file changes, filters, search, stable pagination, traversal and corrupt-history preservation.'
} finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($resolved.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path $resolved -Leaf).StartsWith('DreamMover-History-')) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
