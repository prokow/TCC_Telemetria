$ErrorActionPreference = 'Stop'
$workspacePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$reportDirectory = Join-Path $workspacePath 'Temp/SampleSceneChecks'
$null = New-Item -ItemType Directory -Path $reportDirectory -Force
$scripts = @(
    'Tools/SampleSceneChecks/CheckSceneAssets.ps1',
    'Tools/SampleSceneChecks/CheckNarratives.ps1',
    'Tools/ComponentBoundaryChecks/Run.ps1',
    'Tools/ParticipantIntegrationChecks/CheckSimulation.ps1',
    'Tools/ParticipantIntegrationChecks/Run.ps1'
)
foreach ($relativePath in $scripts) {
    $output = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $workspacePath $relativePath) 2>&1
    $result = $LASTEXITCODE
    $logName = $relativePath.Replace('/','_').Replace('.ps1','.log')
    [IO.File]::WriteAllLines((Join-Path $reportDirectory $logName), [string[]]$output, [Text.UTF8Encoding]::new($false))
    if ($result -ne 0) { $output | Write-Output; throw ('Teste falhou: ' + $relativePath) }
    $output | Where-Object { $_ -match '^PASS:|^SampleScene:|^Narrativas:|^AVISO:' } | Write-Output
}
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $workspacePath 'Tools/SampleSceneChecks/CheckMovementCsv.ps1')
Write-Output 'PASS: verificacoes locais concluidas. Para testar a API XR no Unity, use Tools > Circuitos VR > Testar SampleScene sem Play Mode. Fisica/headset nao sao executados por este script.'
