param([string]$UnityEditor = 'C:/Program Files/Unity/Hub/Editor/6000.3.14f1/Editor')
$ErrorActionPreference = 'Stop'
$workspacePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$checksPath = Join-Path $workspacePath 'Temp/ParticipantIntegrationChecks'
$null = New-Item -ItemType Directory -Path $checksPath -Force
$enginePath = Join-Path $UnityEditor 'Data/Managed/UnityEngine'
$jsonPackage = Get-ChildItem -LiteralPath (Join-Path $workspacePath 'Library/PackageCache') -Directory -Filter 'com.unity.nuget.newtonsoft-json@*' | Select-Object -First 1
if (-not $jsonPackage) { throw 'Pacote Newtonsoft instalado pelo Unity não encontrado.' }
$sourceFiles = @(
    'Assets/MainProjeto/Telemetria/Supabase/SupabaseConnection.cs',
    'Assets/MainProjeto/Telemetria/Supabase/SupabaseConnectionSettings.cs',
    'Assets/MainProjeto/Telemetria/Supabase/ISupabaseReadClient.cs',
    'Assets/MainProjeto/Telemetria/Supabase/SupabaseService.cs',
    'Assets/MainProjeto/Telemetria/Supabase/SupabaseAuthService.cs',
    'Assets/MainProjeto/Telemetria/Supabase/SupabaseAuthSession.cs',
    'Assets/MainProjeto/Telemetria/Supabase/WindowsSupabaseSessionStore.cs',
    'Assets/MainProjeto/Telemetria/Supabase/RememberedOperatorLogin.cs',
    'Assets/MainProjeto/Telemetria/Supabase/SharedSupabaseSession.cs',
    'Assets/MainProjeto/Telemetria/Telemetry/Persistence/TelemetryCsvEncoder.cs',
    'Assets/MainProjeto/Telemetria/Telemetry/Upload/TelemetryUploadPackage.cs',
    'Assets/MainProjeto/Telemetria/Telemetry/Upload/TelemetryUploadStore.cs',
    'Assets/MainProjeto/Telemetria/Telemetry/Upload/TelemetryUploadClient.cs',
    'Assets/MainProjeto/Telemetria/Telemetry/Upload/TelemetryUploadProcessor.cs',
    'Assets/MainProjeto/Telemetria/Telemetry/Upload/UnityTelemetryUploadTransport.cs',
    'Assets/MainProjeto/Telemetria/Telemetry/Upload/TelemetrySyncController.cs',
    'Assets/MainProjeto/MenuPrincipal/Scripts/AutomaticOperatorLogin.cs',
    'Assets/MainProjeto/MenuPrincipal/Editor/AutomaticOperatorLoginEditor.cs',
    'Assets/MainProjeto/MenuPrincipal/Editor/MenuCanvasFlowChecks.cs',
    'Assets/MainProjeto/MenuPrincipal/Scripts/OperatorLoginUI.cs',
    'Assets/MainProjeto/Telemetria/Participants/Participant.cs',
    'Assets/MainProjeto/Telemetria/Participants/ParticipantRepository.cs',
    'Assets/MainProjeto/Telemetria/Participants/ParticipantRosterSync.cs',
    'Assets/MainProjeto/Telemetria/Participants/ParticipantQueryProbe.cs',
    'Assets/MainProjeto/Telemetria/Participants/ParticipantManager.cs',
    'Assets/MainProjeto/Telemetria/Sessions/SessionContext.cs',
    'Assets/MainProjeto/Telemetria/Sessions/ApplicationSessionContext.cs',
    'Assets/MainProjeto/MenuPrincipal/Scripts/ParticipantListItemUI.cs',
    'Assets/MainProjeto/MenuPrincipal/Scripts/ParticipantSelectionUI.cs',
    'Assets/MainProjeto/MenuPrincipal/Scripts/ParticipantAutoRefresh.cs',
    'Assets/MainProjeto/MenuPrincipal/Scripts/MenuIntegrationController.cs',
    'Assets/MainProjeto/MenuPrincipal/Scripts/MainMenu.cs',
    'Assets/MainProjeto/Telemetria/Telemetry/Core/TelemetrySessionManager.cs',
    'Assets/MainProjeto/Telemetria/Telemetry/Recorders/TelemetryHeadRecorder.cs',
    'Assets/MainProjeto/Telemetria/Telemetry/Recorders/TelemetryControllerRecorder.cs',
    'Assets/MainProjeto/Telemetria/Telemetry/Data/TelemetrySessionPayload.cs',
    'Assets/MainProjeto/Telemetria/Telemetry/Data/HeadTelemetryFrame.cs',
    'Assets/MainProjeto/Telemetria/Telemetry/Data/ControllerTelemetryFrame.cs',
    'Assets/MainProjeto/Telemetria/Telemetry/Data/TelemetryEventFrame.cs',
    'Assets/MainProjeto/Telemetria/Telemetry/Persistence/TelemetryLocalStorage.cs',
    'Assets/MainProjeto/Core/Scripts/Model/QuestCompletionProgress.cs',
    'Assets/MainProjeto/Core/Scripts/Model/QuestAdvanceRules.cs',
    'Assets/MainProjeto/Core/Scripts/Model/QuestSystemModel.cs',
    'Assets/MainProjeto/Core/Scripts/ScriptableObjects/Quest.cs',
    'Assets/MainProjeto/Core/Scripts/ScriptableObjects/Task.cs',
    'Assets/MainProjeto/Core/Scripts/NextQuest/NextQuestButton.cs',
    'Assets/QuickOutline/Scripts/Outline.cs',
    'Assets/MainProjeto/UI/Scripts/SessionMetricsManager.cs',
    'Assets/MainProjeto/Telemetria/Telemetry/Core/AutoTelemetrySession.cs',
    'Tools/ParticipantIntegrationChecks/Program.cs',
    'Tools/ParticipantIntegrationChecks/TelemetryUploadChecks.cs'
)
$compileItems = foreach ($relativePath in $sourceFiles) {
    '<Compile Include="' + [Security.SecurityElement]::Escape((Join-Path $workspacePath $relativePath)) + '" />'
}
$referencePaths = @(
    (Join-Path $enginePath 'UnityEngine.CoreModule.dll'),
    (Join-Path $enginePath 'UnityEditor.CoreModule.dll'),
    (Join-Path $enginePath 'UnityEngine.IMGUIModule.dll'),
    (Join-Path $enginePath 'UnityEngine.UnityWebRequestModule.dll'),
    (Join-Path $enginePath 'UnityEngine.UIModule.dll'),
    (Join-Path $enginePath 'UnityEngine.TextRenderingModule.dll'),
    (Join-Path $enginePath 'UnityEngine.JSONSerializeModule.dll'),
    (Join-Path $enginePath 'UnityEngine.AnimationModule.dll'),
    (Join-Path $enginePath 'UnityEngine.PhysicsModule.dll'),
    (Join-Path $workspacePath 'Library/ScriptAssemblies/Unity.XR.Interaction.Toolkit.dll'),
    (Join-Path $workspacePath 'Library/ScriptAssemblies/UnityEngine.UI.dll'),
    (Join-Path $workspacePath 'Library/ScriptAssemblies/Unity.TextMeshPro.dll'),
    (Join-Path $workspacePath 'Library/ScriptAssemblies/Unity.InputSystem.dll'),
    (Join-Path $jsonPackage.FullName 'Runtime/Newtonsoft.Json.dll')
)
$referenceItems = foreach ($referencePath in $referencePaths) {
    if (-not (Test-Path -LiteralPath $referencePath)) { throw "Biblioteca não encontrada: $referencePath" }
    '<Reference Include="' + [IO.Path]::GetFileNameWithoutExtension($referencePath) + '"><HintPath>' + [Security.SecurityElement]::Escape($referencePath) + '</HintPath></Reference>'
}
$projectText = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><UseAppHost>false</UseAppHost>
    <LangVersion>9.0</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <Nullable>disable</Nullable><NoWarn>0649</NoWarn>
    <RestoreSources></RestoreSources><NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup>
    $($compileItems -join [Environment]::NewLine)
    $($referenceItems -join [Environment]::NewLine)
  </ItemGroup>
</Project>
"@
$projectPath = Join-Path $checksPath 'ParticipantIntegrationChecks.csproj'
[IO.File]::WriteAllText($projectPath, $projectText, [Text.UTF8Encoding]::new($false))
& dotnet run --project $projectPath --configuration Release --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Verificações de participantes falharam.' }
