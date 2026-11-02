$ErrorActionPreference = 'Stop'
$workspacePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$testPath = Join-Path $workspacePath 'Temp/ComponentBoundaryChecks'
$null = New-Item -ItemType Directory -Path $testPath -Force
[xml]$unityProject = [IO.File]::ReadAllText((Join-Path $workspacePath 'Assembly-CSharp.csproj'))
$sourcePaths = @($unityProject.Project.ItemGroup.Compile | ForEach-Object { $_.Include } | Where-Object { $_ })
$sourcePaths += @('Assets/MainProjeto/Scripts/ParentCoponent/ElectronicComponentMovementLimit.cs', 'Assets/MainProjeto/Scripts/ParentCoponent/ElectronicComponentBoundarySetup.cs', 'Assets/MainProjeto/Scripts/Protoboard Interactor/ProtoboardOccupancyMap.cs', 'Assets/MainProjeto/Core/Scripts/Model/QuestAdvanceRules.cs', 'Assets/MainProjeto/Editor/SampleSceneInteractionChecks.cs', 'Tools/ComponentBoundaryChecks/Program.cs', 'Tools/ComponentBoundaryChecks/QuestAdvanceChecks.cs')
$compileItems = foreach ($relativePath in ($sourcePaths | ForEach-Object { $_.Replace('\','/') } | Sort-Object -Unique)) {
    $fullPath = Join-Path $workspacePath $relativePath
    '<Compile Include="' + [Security.SecurityElement]::Escape($fullPath) + '" />'
}
# BCL do SDK de teste; Unity e pacotes reais vindos das referencias geradas pelo Editor.
$referenceItems = foreach ($reference in $unityProject.Project.ItemGroup.Reference) {
    if (-not $reference.HintPath -or $reference.Include -match '^(System|mscorlib|netstandard|Microsoft\.CSharp|Mono\.)') { continue }
    if ([IO.Path]::IsPathRooted($reference.HintPath)) { $fullPath = $reference.HintPath }
    else { $fullPath = [IO.Path]::GetFullPath((Join-Path $workspacePath $reference.HintPath)) }
    if (-not (Test-Path -LiteralPath $fullPath)) { throw ('Referencia Unity ausente: ' + $reference.Include) }
    '<Reference Include="' + [Security.SecurityElement]::Escape($reference.Include) + '"><HintPath>' + [Security.SecurityElement]::Escape($fullPath) + '</HintPath></Reference>'
}
$projectText = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><UseAppHost>false</UseAppHost>
    <LangVersion>9.0</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Nullable>disable</Nullable><NoWarn>0649;0414;0618;8981</NoWarn><DefineConstants>UNITY_EDITOR;UNITY_6000_0_OR_NEWER;UNITY_2023_3_OR_NEWER</DefineConstants>
    <RestoreSources></RestoreSources><NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup>$($compileItems -join [Environment]::NewLine)$($referenceItems -join [Environment]::NewLine)</ItemGroup>
</Project>
"@
$projectPath = Join-Path $testPath 'ComponentBoundaryChecks.csproj'
[IO.File]::WriteAllText($projectPath, $projectText, [Text.UTF8Encoding]::new($false))
$env:CIRCUITOS_VR_QUEST_REPORT = Join-Path $workspacePath 'Temp/SampleSceneChecks/scene-assets-report.json'
& dotnet run --project $projectPath --configuration Release --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Verificacoes do limite falharam.' }
