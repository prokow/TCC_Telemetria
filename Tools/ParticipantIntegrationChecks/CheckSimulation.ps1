$ErrorActionPreference = 'Stop'
$workspacePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sampleText = [IO.File]::ReadAllText((Join-Path $workspacePath 'Assets/Scenes/SampleScene.unity')).Replace("`r`n", "`n")
$blocks = @{}
foreach ($match in [regex]::Matches($sampleText, '(?ms)^--- !u!(\d+) &(-?\d+)(?: stripped)?\n(.*?)(?=^--- !u!|\z)')) {
    $blocks[$match.Groups[2].Value] = @{ Kind=$match.Groups[1].Value; Body=$match.Groups[3].Value }
}
$checks = 0
function Assert-Simulation($condition, $description) {
    if (-not $condition) { throw "FAIL: $description" }
    $script:checks++
}
$auto = $blocks['1041700997'].Body
Assert-Simulation ($auto.Contains('guid: bf021eaefd3a9c04ab3e86e217dcd801')) 'AutoTelemetrySession existente preservado.'
Assert-Simulation ($auto.Contains('sessionManager: {fileID: 1041700995}')) 'Gravador associado.'
Assert-Simulation ($auto.Contains('questSystem: {fileID: 1836445051}')) 'Fim da coleta associado ao sistema de quests real.'
$model = $blocks['1836445051'].Body
Assert-Simulation ($model.Contains('guid: df612dbc6cd4ff14a88036563cea9ba7')) 'QuestSystemModel deve resolver.'
$sequence = [regex]::Match($model,'(?s)  sequenceOfQuests:\n(.*?)(?=  _?\w+:|\z)').Groups[1].Value
$quests = [regex]::Matches($sequence,'guid: ([a-f0-9]{32})')
Assert-Simulation ($quests.Count -eq 7) 'Sequencia existente tem sete quests.'
Assert-Simulation ($quests[$quests.Count-1].Groups[1].Value -eq '55b386455826dbd478f4e0d726b6edf9') 'Última quest é a narrativa final existente.'
Assert-Simulation ($model.Contains('m_MethodName: TurnOnButton')) 'Avanço visual existente preservado.'
$source = [IO.File]::ReadAllText((Join-Path $workspacePath 'Assets/MainProjeto/Telemetria/Telemetry/Core/AutoTelemetrySession.cs'))
Assert-Simulation ($source.Contains('onQuestSequenceComplete.AddListener(OnExperienceCompleted)')) 'Coleta encerra por conclusão da sequência.'
Assert-Simulation ($source.Contains('onQuestSequenceComplete.RemoveListener(OnExperienceCompleted)')) 'Listener removido no ciclo de vida.'
$manager = [IO.File]::ReadAllText((Join-Path $workspacePath 'Assets/MainProjeto/Telemetria/Telemetry/Core/TelemetrySessionManager.cs'))
Assert-Simulation ($manager.Contains('if (paused && finishOnApplicationPause) EndSession();')) 'Pausa não encerra sessão por padrão.'
Assert-Simulation (-not $manager.Contains('if (paused && recording) EndSession();')) 'Removido encerramento antecipado incondicional.'
$questSource = [IO.File]::ReadAllText((Join-Path $workspacePath 'Assets/MainProjeto/Core/Scripts/Model/QuestSystemModel.cs'))
Assert-Simulation (-not $questSource.Contains('DontDestroyOnLoad(gameObject)')) 'Nova experiência não herda quests e circuitos da cena anterior.'
$yamlPackage = Get-ChildItem -LiteralPath (Join-Path $workspacePath 'Library/PackageCache') -Directory -Filter 'com.unity.visualscripting@*' | Select-Object -First 1
$yamlAssembly = [Reflection.Assembly]::LoadFrom((Join-Path $yamlPackage.FullName 'Editor/VisualScripting.Core/Dependencies/YamlDotNet/Unity.VisualScripting.YamlDotNet.dll'))
$yaml = [Activator]::CreateInstance($yamlAssembly.GetType('Unity.VisualScripting.YamlDotNet.RepresentationModel.YamlStream'))
$plain = [regex]::Replace($sampleText,'(?m)^%.*\n','')
$plain = [regex]::Replace($plain,'(?m)^--- !u!\d+ &-?\d+(?: stripped)?$','---')
$reader = [IO.StringReader]::new($plain)
try { $yaml.Load($reader) } finally { $reader.Dispose() }
Assert-Simulation ($yaml.Documents.Count -eq $blocks.Count) 'SampleScene contém YAML válido.'
"PASS: $checks verificações da associação entre quests e coleta."
