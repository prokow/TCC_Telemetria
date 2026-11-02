$ErrorActionPreference='Stop'
$workspacePath=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$reportDirectory=Join-Path $workspacePath 'Temp/SampleSceneChecks'
$scene=[IO.File]::ReadAllText((Join-Path $workspacePath 'Assets/Scenes/SampleScene.unity')).Replace("`r`n","`n")
function Blocks([string]$text) {
    $result=@{}
    foreach($m in [regex]::Matches($text,'(?ms)^--- !u!(\d+) &(-?\d+)(?: stripped)?\n(.*?)(?=^--- !u!|\z)')) { $result[$m.Groups[2].Value]=@{Kind=$m.Groups[1].Value;Body=$m.Groups[3].Value} }
    return $result
}
$blocks=Blocks $scene
$guidPaths=@{}
foreach($line in (& rg -n --glob '*.meta' '^guid: [a-f0-9]{32}$' (Join-Path $workspacePath 'Assets'))) {
    if($line -match '^(.+)\.meta:\d+:guid: ([a-f0-9]{32})$') { $guidPaths[$Matches[2]]=$Matches[1] }
}
$narrativeGuid=[regex]::Match([IO.File]::ReadAllText((Join-Path $workspacePath 'Assets/MainProjeto/Robot/Scripts/SequentialRobotNarrative.cs.meta')),'guid: ([a-f0-9]{32})').Groups[1].Value
$modelGuid=[regex]::Match([IO.File]::ReadAllText((Join-Path $workspacePath 'Assets/MainProjeto/Core/Scripts/Model/QuestSystemModel.cs.meta')),'guid: ([a-f0-9]{32})').Groups[1].Value
$model=@($blocks.Values | Where-Object {$_.Body -match ('m_Script:.*guid: '+$modelGuid)})[0].Body
$sequence=@([regex]::Matches([regex]::Match($model,'(?ms)^  sequenceOfQuests:\n(.*?)(?=^  \w+:|\z)').Groups[1].Value,'guid: ([a-f0-9]{32})') | ForEach-Object {$_.Groups[1].Value})
$circuits=@([regex]::Matches([regex]::Match($model,'(?ms)^  sceneCircuits:\n(.*?)(?=^  \w+:|\z)').Groups[1].Value,'fileID: (-?\d+)') | ForEach-Object {$_.Groups[1].Value})
$checks=0; $failures=[Collections.Generic.List[string]]::new(); $rows=@()
function Check([bool]$condition,[string]$message) { $script:checks++; if(-not $condition) { $script:failures.Add($message) } }
for($index=0; $index -lt $sequence.Count; $index++) {
    $questText=[IO.File]::ReadAllText($guidPaths[$sequence[$index]])
    $questName=[IO.Path]::GetFileNameWithoutExtension($guidPaths[$sequence[$index]])
    $questTasks=@([regex]::Matches([regex]::Match($questText,'(?s)  tasks:.*').Value,'guid: ([a-f0-9]{32})') | ForEach-Object {$_.Groups[1].Value})
    $circuit=$blocks[$circuits[$index]].Body
    $sourceGuid=[regex]::Match($circuit,'m_CorrespondingSourceObject: \{fileID: -?\d+, guid: ([a-f0-9]{32})').Groups[1].Value
    $instanceId=[regex]::Match($circuit,'m_PrefabInstance: \{fileID: (-?\d+)').Groups[1].Value
    $prefab=Blocks ([IO.File]::ReadAllText($guidPaths[$sourceGuid]).Replace("`r`n","`n"))
    $overrides=[regex]::Matches($blocks[$instanceId].Body,'(?m)^    - target: \{fileID: (-?\d+), guid: ([a-f0-9]{32}), type: \d+\}\n      propertyPath: ([^\n]+)\n      value: ([^\n]*)\n      objectReference: \{fileID: (-?\d+)(?:, guid: ([a-f0-9]{32}), type: \d+)?\}')
    $narratives=@($prefab.Keys | Where-Object {$prefab[$_].Body -match ('m_Script:.*guid: '+$narrativeGuid)})
    Check ($narratives.Count -gt 0) ('Nenhuma narrativa encontrada no prefab da quest: '+$questName)
    foreach($id in $narratives) {
        $body=$prefab[$id].Body
        $go=[regex]::Match($body,'m_GameObject: \{fileID: (-?\d+)').Groups[1].Value
        $name=[regex]::Match($prefab[$go].Body,'m_Name: (.+)').Groups[1].Value
        $task=[regex]::Match($body,'falaRoboTask: \{fileID: -?\d+, guid: ([a-f0-9]{32})').Groups[1].Value
        $auto=[regex]::Match($body,'autoConcluir: (\d)').Groups[1].Value -eq '1'
        $button=[regex]::Match($body,'botaoProximaQuest: \{fileID: (-?\d+)').Groups[1].Value
        $speechText=[regex]::Match($body,'(?ms)^  falas:\n(.*?)(?=^  \w+:|\z)').Groups[1].Value
        $speeches=[Collections.Generic.List[string]]::new()
        foreach($m in [regex]::Matches($speechText,'\{fileID: -?\d+(?:, guid: ([a-f0-9]{32}), type: \d+)?\}')) { $speeches.Add($m.Groups[1].Value) }
        foreach($override in $overrides) {
            if($override.Groups[1].Value -eq $id -and $override.Groups[2].Value -eq $sourceGuid -and $override.Groups[3].Value -eq 'falas.Array.size') {
                $size=[int]$override.Groups[4].Value
                while($speeches.Count -lt $size){$speeches.Add('')}
                while($speeches.Count -gt $size){$speeches.RemoveAt($speeches.Count-1)}
            }
        }
        foreach($override in $overrides) {
            if($override.Groups[1].Value -ne $id -or $override.Groups[2].Value -ne $sourceGuid) {continue}
            $property=$override.Groups[3].Value.Trim([char]39).Trim([char]34)
            switch($property) {
                'falaRoboTask' {$task=$override.Groups[6].Value}
                'autoConcluir' {$auto=$override.Groups[4].Value -eq '1'}
                'botaoProximaQuest' {$button=$override.Groups[5].Value}
            }
            if($property -match '^falas\.Array\.data\[(\d+)\]$') {
                $speechIndex=[int]$Matches[1]
                # Overrides antigos alem de Array.size nao pertencem mais a lista efetiva.
                if($speechIndex -lt $speeches.Count){$speeches[$speechIndex]=$override.Groups[6].Value}
            }
        }
        Check ($questTasks -contains $task) ('Narrativa aponta para tarefa fora da quest: '+$questName+'/'+$name)
        if(-not $auto) {
            Check ($button -ne '0' -and $button -ne '') ('Narrativa final sem botao atribuido na cena: '+$questName+'/'+$name)
            Check ($task -eq $questTasks[-1]) ('Narrativa que libera botao nao aponta para ultima task: '+$questName+'/'+$name)
        }
        Check ($speeches.Count -gt 0) ('Narrativa sem falas: '+$questName+'/'+$name)
        foreach($speech in $speeches) {
            Check ($guidPaths.ContainsKey($speech)) ('Asset de fala ausente: '+$questName+'/'+$name)
            if(-not $guidPaths.ContainsKey($speech)){continue}
            $speechData=[IO.File]::ReadAllText($guidPaths[$speech])
            $audio=[regex]::Match($speechData,'sound: \{fileID: -?\d+, guid: ([a-f0-9]{32})').Groups[1].Value
            Check ($audio -ne '' -and $guidPaths.ContainsKey($audio)) ('Audio ausente: '+[IO.Path]::GetFileNameWithoutExtension($guidPaths[$speech]))
        }
        $rows += [ordered]@{quest=$questName;narrative=$name;task=if($guidPaths.ContainsKey($task)){[IO.Path]::GetFileNameWithoutExtension($guidPaths[$task])}else{$task};autoComplete=$auto;buttonFileId=$button;speeches=$speeches.Count}
    }
}
$null=New-Item -ItemType Directory -Path $reportDirectory -Force
[IO.File]::WriteAllText((Join-Path $reportDirectory 'narratives-report.json'),(@{checks=$checks;failures=$failures.ToArray();narratives=$rows;mode='Referencias de prefabs com overrides da SampleScene; sem reproducao de audio'}|ConvertTo-Json -Depth 7),[Text.UTF8Encoding]::new($false))
Write-Output ("Narrativas: $checks verificacoes, $($rows.Count) componentes em $($sequence.Count) quests, $($failures.Count) falhas.")
foreach($failure in $failures){Write-Output ('FAIL: '+$failure)}
if($failures.Count -gt 0){exit 1}
