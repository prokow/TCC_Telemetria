$ErrorActionPreference = 'Stop'
$workspacePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$checksPath = Join-Path $workspacePath 'Temp/TelemetryQuestIntervalChecks'
$null = New-Item -ItemType Directory -Path $checksPath -Force
$sourcePath = [Security.SecurityElement]::Escape((Join-Path $workspacePath 'Assets/MainProjeto/Telemetria/Telemetry/Core/AutoTelemetrySession.cs'))
$testPath = [Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'Program.cs'))
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><UseAppHost>false</UseAppHost>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems><Nullable>disable</Nullable>
    <RestoreSources></RestoreSources><NuGetAudit>false</NuGetAudit><NoWarn>0649</NoWarn>
  </PropertyGroup>
  <ItemGroup><Compile Include="$sourcePath" /><Compile Include="$testPath" /></ItemGroup>
</Project>
"@
$projectPath = Join-Path $checksPath 'TelemetryQuestIntervalChecks.csproj'
[IO.File]::WriteAllText($projectPath, $project, [Text.UTF8Encoding]::new($false))
& dotnet run --project $projectPath --configuration Release --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Verificações do intervalo de telemetria falharam.' }
