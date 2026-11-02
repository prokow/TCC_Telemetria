$ErrorActionPreference = 'Stop'
$workspacePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$reportDirectory = Join-Path $workspacePath 'Temp/SampleSceneChecks'
$null = New-Item -ItemType Directory -Path $reportDirectory -Force
$script:checkCount = 0
$script:failures = [Collections.Generic.List[string]]::new()
$script:warnings = [Collections.Generic.List[string]]::new()
function Check-Asset([bool]$condition, [string]$description) {
    $script:checkCount++
    if (-not $condition) { $script:failures.Add($description) }
}
$scenePath = Join-Path $workspacePath 'Assets/Scenes/SampleScene.unity'
$sceneText = [IO.File]::ReadAllText($scenePath).Replace("`r`n", "`n")
$blocks = @{}
foreach ($match in [regex]::Matches($sceneText, '(?ms)^--- !u!(\d+) &(-?\d+)(?: stripped)?\n(.*?)(?=^--- !u!|\z)')) {
    $id = $match.Groups[2].Value
    Check-Asset (-not $blocks.ContainsKey($id)) ('fileID repetido: ' + $id)
    $blocks[$id] = @{ Kind=$match.Groups[1].Value; Body=$match.Groups[3].Value }
}
foreach ($reference in [regex]::Matches($sceneText, '\{fileID: (-?\d+)\}')) {
    $id = $reference.Groups[1].Value
    Check-Asset ($id -eq '0' -or $blocks.ContainsKey($id)) ('Referencia local ausente: ' + $id)
}
$guidPaths = @{}
$metaLines = & rg -n --glob '*.meta' '^guid: [a-f0-9]{32}$' (Join-Path $workspacePath 'Assets')
foreach ($line in $metaLines) {
    if ($line -match '^(.+)\.meta:\d+:guid: ([a-f0-9]{32})$') { $guidPaths[$Matches[2]] = $Matches[1] }
}
$modelGuid = [regex]::Match([IO.File]::ReadAllText((Join-Path $workspacePath 'Assets/MainProjeto/Core/Scripts/Model/QuestSystemModel.cs.meta')), 'guid: ([a-f0-9]{32})').Groups[1].Value
$models = @($blocks.Values | Where-Object { $_.Kind -eq '114' -and $_.Body.Contains('guid: ' + $modelGuid) })
Check-Asset ($models.Count -eq 1) 'QuestSystemModel deve resolver exatamente uma vez na cena.'
if ($models.Count -ne 1) { throw 'Nao foi possivel localizar o modelo real de quests.' }
$model = $models[0].Body
$sequenceText = [regex]::Match($model, '(?ms)^  sequenceOfQuests:\n(.*?)(?=^  \w+:|\z)').Groups[1].Value
$sequence = @([regex]::Matches($sequenceText, 'guid: ([a-f0-9]{32})') | ForEach-Object { $_.Groups[1].Value })
$circuitsText = [regex]::Match($model, '(?ms)^  sceneCircuits:\n(.*?)(?=^  \w+:|\z)').Groups[1].Value
$circuits = @([regex]::Matches($circuitsText, 'fileID: (-?\d+)') | ForEach-Object { $_.Groups[1].Value })
Check-Asset ($sequence.Count -eq 7) 'Sequencia deve conter sete quests.'
Check-Asset ($circuits.Count -eq $sequence.Count) 'Circuitos devem corresponder a sequencia.'
foreach ($id in $circuits) { Check-Asset ($id -ne '0' -and $blocks.ContainsKey($id)) ('Circuito ausente: ' + $id) }

$questReports = @()
$allTaskGuids = [Collections.Generic.HashSet[string]]::new()
$tasksByQuest = @{}
foreach ($questGuid in $sequence) {
    Check-Asset ($guidPaths.ContainsKey($questGuid)) ('Asset da quest ausente: ' + $questGuid)
    if (-not $guidPaths.ContainsKey($questGuid)) { continue }
    $questPath = $guidPaths[$questGuid]
    $questText = [IO.File]::ReadAllText($questPath).Replace("`r`n", "`n")
    $taskText = [regex]::Match($questText, '(?ms)^  tasks:\n(.*?)(?=^  \w+:|\z)').Groups[1].Value
    $tasks = @([regex]::Matches($taskText, 'guid: ([a-f0-9]{32})') | ForEach-Object { $_.Groups[1].Value })
    $tasksByQuest[$questGuid] = $tasks
    $name = [IO.Path]::GetFileNameWithoutExtension($questPath)
    Check-Asset ($tasks.Count -gt 0) ('Quest vazia: ' + $name)
    Check-Asset (@($tasks | Sort-Object -Unique).Count -eq $tasks.Count) ('Tarefas repetidas: ' + $name)
    $dependencies = @{}
    foreach ($task in $tasks) {
        $null = $allTaskGuids.Add($task)
        Check-Asset ($guidPaths.ContainsKey($task)) ('Tarefa ausente: ' + $name + '/' + $task)
        if (-not $guidPaths.ContainsKey($task)) { $dependencies[$task] = @(); continue }
        $data = [IO.File]::ReadAllText($guidPaths[$task]).Replace("`r`n", "`n")
        $dependencyText = [regex]::Match($data, '(?ms)^  dependencies:(.*?)(?=^  \w+:|\z)').Groups[1].Value
        $dependencies[$task] = @([regex]::Matches($dependencyText, 'guid: ([a-f0-9]{32})') | ForEach-Object { $_.Groups[1].Value })
        foreach ($dependency in $dependencies[$task]) {
            Check-Asset ($tasks -contains $dependency) ('Dependencia fora da quest: ' + $name + '/' + [IO.Path]::GetFileNameWithoutExtension($guidPaths[$task]))
        }
    }
    $completed = [Collections.Generic.HashSet[string]]::new()
    $order = [Collections.Generic.List[string]]::new()
    do {
        $progress = $false
        foreach ($task in $tasks) {
            if ($completed.Contains($task)) { continue }
            $blocked = @($dependencies[$task] | Where-Object { -not $completed.Contains($_) }).Count -gt 0
            if (-not $blocked) { $null = $completed.Add($task); $order.Add([IO.Path]::GetFileNameWithoutExtension($guidPaths[$task])); $progress = $true }
        }
    } while ($progress)
    Check-Asset ($completed.Count -eq $tasks.Count) ('Ciclo ou dependencia impossivel: ' + $name)
    $graph = @($tasks | ForEach-Object { [ordered]@{ id=$_; name=[IO.Path]::GetFileNameWithoutExtension($guidPaths[$_]); dependencies=@($dependencies[$_]) } })
    $questReports += [ordered]@{ quest=$name; tasks=$tasks.Count; reachable=$completed.Count; validOrder=$order.ToArray(); taskGraph=$graph }
}

# Percorre somente prefabs que a cena realmente referencia, inclusive variantes e prefabs aninhados.
$visited = [Collections.Generic.HashSet[string]]::new()
$pending = [Collections.Generic.Queue[string]]::new()
$pending.Enqueue($scenePath)
$assetTexts = [Collections.Generic.List[string]]::new()
while ($pending.Count -gt 0) {
    $path = $pending.Dequeue()
    if (-not $visited.Add($path)) { continue }
    $text = [IO.File]::ReadAllText($path)
    $assetTexts.Add($text)
    foreach ($reference in [regex]::Matches($text, 'm_SourcePrefab: \{fileID: -?\d+, guid: ([a-f0-9]{32})')) {
        $guid = $reference.Groups[1].Value
        Check-Asset ($guidPaths.ContainsKey($guid)) ('Prefab ausente: ' + $guid)
        if ($guidPaths.ContainsKey($guid)) { $pending.Enqueue($guidPaths[$guid]) }
    }
}
foreach ($task in $allTaskGuids) {
    $referenceCount = @($assetTexts | Where-Object { $_.Contains('guid: ' + $task) }).Count
    Check-Asset ($referenceCount -gt 0) ('Tarefa sem referencia na cena ou nos prefabs: ' + [IO.Path]::GetFileNameWithoutExtension($guidPaths[$task]))
}
$circuitReports = @()
for ($index=0; $index -lt $circuits.Count; $index++) {
    $body = $blocks[$circuits[$index]].Body
    $sourceGuid = [regex]::Match($body, 'm_CorrespondingSourceObject: \{fileID: -?\d+, guid: ([a-f0-9]{32})').Groups[1].Value
    if (-not $sourceGuid -or -not $guidPaths.ContainsKey($sourceGuid)) { $script:warnings.Add('Circuito local sem prefab: analise das tarefas por fase exige auditoria no Editor.'); continue }
    $sources = [Collections.Generic.List[string]]::new()
    $circuitVisited = [Collections.Generic.HashSet[string]]::new()
    $circuitQueue = [Collections.Generic.Queue[string]]::new()
    $circuitQueue.Enqueue($guidPaths[$sourceGuid])
    while ($circuitQueue.Count -gt 0) {
        $path = $circuitQueue.Dequeue()
        if (-not $circuitVisited.Add($path)) { continue }
        $data = [IO.File]::ReadAllText($path)
        $sources.Add($data)
        foreach ($reference in [regex]::Matches($data, 'm_SourcePrefab: \{fileID: -?\d+, guid: ([a-f0-9]{32})')) {
            $nextGuid=$reference.Groups[1].Value
            if ($guidPaths.ContainsKey($nextGuid)) { $circuitQueue.Enqueue($guidPaths[$nextGuid]) }
        }
    }
    # Overrides desta instancia tambem podem atribuir tarefas especificas da fase.
    $instanceId=[regex]::Match($body,'m_PrefabInstance: \{fileID: (-?\d+)').Groups[1].Value
    if ($blocks.ContainsKey($instanceId)) { $sources.Add($blocks[$instanceId].Body) }
    foreach ($task in $tasksByQuest[$sequence[$index]]) {
        Check-Asset (@($sources | Where-Object { $_.Contains('guid: ' + $task) }).Count -gt 0) ('Tarefa sem referencia no circuito da propria quest: ' + [IO.Path]::GetFileNameWithoutExtension($guidPaths[$sequence[$index]]) + '/' + [IO.Path]::GetFileNameWithoutExtension($guidPaths[$task]))
    }
    $circuitReports += [ordered]@{ quest=[IO.Path]::GetFileNameWithoutExtension($guidPaths[$sequence[$index]]); prefab=[IO.Path]::GetFileNameWithoutExtension($guidPaths[$sourceGuid]) }
}
$assignedArea = @($assetTexts | Where-Object { $_ -match 'movementArea: \{fileID: [1-9]' -or $_ -match '(?s)propertyPath: movementArea\r?\n.*?objectReference: \{fileID: [1-9]' }).Count
if ($assignedArea -eq 0) { $script:warnings.Add('Nenhuma movementArea atribuida nos arquivos da SampleScene/prefabs referenciados. O novo limite depende de configurar a caixa no Inspector/comando Tools.') }
$report = [ordered]@{
    utc=[DateTime]::UtcNow.ToString('O'); mode='Analise estatica dos arquivos reais; nao executa fisica/VR';
    checks=$script:checkCount; failures=$script:failures.ToArray(); warnings=$script:warnings.ToArray();
    quests=$questReports; circuits=$circuitReports; uniqueTasks=$allTaskGuids.Count; referencedSceneAndPrefabs=$visited.Count
}
$reportPath = Join-Path $reportDirectory 'scene-assets-report.json'
[IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
Write-Output ("SampleScene: $script:checkCount verificacoes, $($script:failures.Count) falhas, $($script:warnings.Count) avisos; $($allTaskGuids.Count) tarefas em $($sequence.Count) quests.")
foreach ($failure in $script:failures) { Write-Output ('FAIL: ' + $failure) }
foreach ($warning in $script:warnings) { Write-Output ('AVISO: ' + $warning) }
if ($script:failures.Count -gt 0) { exit 1 }
