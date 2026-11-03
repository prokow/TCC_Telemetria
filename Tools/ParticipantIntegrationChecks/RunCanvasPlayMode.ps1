param([string]$UnityEditor = 'C:/Program Files/Unity/Hub/Editor/6000.3.14f1/Editor')
$ErrorActionPreference = 'Stop'
$workspacePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$testProject = Join-Path $workspacePath 'Temp/MenuCanvasPlayModeProject'
$null = New-Item -ItemType Directory -Path $testProject -Force
function Copy-FixtureFile([string]$relative) {
    $source = Join-Path $workspacePath $relative
    $destination = Join-Path $testProject $relative
    $null = New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force
    Copy-Item -LiteralPath $source -Destination $destination -Force
    if (Test-Path -LiteralPath ($source + '.meta')) { Copy-Item -LiteralPath ($source + '.meta') -Destination ($destination + '.meta') -Force }
}
$menuScripts = @('MenuIntegrationController','MainMenu','ParticipantSelectionUI','ParticipantListItemUI','ParticipantAutoRefresh','AutomaticOperatorLogin','OperatorLoginUI')
foreach ($name in $menuScripts) { Copy-FixtureFile ('Assets/MainProjeto/MenuPrincipal/Scripts/' + $name + '.cs') }
foreach ($folder in @('Participants','Sessions')) {
    Get-ChildItem -LiteralPath (Join-Path $workspacePath ('Assets/MainProjeto/Telemetria/' + $folder)) -Filter '*.cs' | ForEach-Object {
        Copy-FixtureFile ('Assets/MainProjeto/Telemetria/' + $folder + '/' + $_.Name)
    }
}
foreach ($name in @('SupabaseConnection','SupabaseConnectionSettings','ISupabaseReadClient','SupabaseService','SupabaseAuthService','SupabaseAuthSession','WindowsSupabaseSessionStore','RememberedOperatorLogin','SharedSupabaseSession')) {
    Copy-FixtureFile ('Assets/MainProjeto/Telemetria/Supabase/' + $name + '.cs')
}
foreach ($folder in @('Data','Upload','Persistence')) {
    Get-ChildItem -LiteralPath (Join-Path $workspacePath ('Assets/MainProjeto/Telemetria/Telemetry/' + $folder)) -Filter '*.cs' | ForEach-Object { Copy-FixtureFile ('Assets/MainProjeto/Telemetria/Telemetry/' + $folder + '/' + $_.Name) }
}

foreach ($relative in @('Assets/Scenes/MainMenu.unity','Assets/MainProjeto/MenuPrincipal/Editor/MenuCanvasFlowChecks.cs','Assets/MainProjeto/Telemetria/Supabase/Settings/MenuSupabaseConnection.asset','ProjectSettings/ProjectVersion.txt')) { Copy-FixtureFile $relative }

# Nunca copiar as credenciais que o responsavel possa ter preenchido no codigo.
$autoPath = Join-Path $testProject 'Assets/MainProjeto/MenuPrincipal/Scripts/AutomaticOperatorLogin.cs'
$auto = [IO.File]::ReadAllText($autoPath)
$auto = [regex]::Replace($auto, '(?m)^(\s*private static readonly string Operator(?:Email|Password)\s*=).*$', '$1 "";')
[IO.File]::WriteAllText($autoPath, $auto, [Text.UTF8Encoding]::new($false))

# Usar os pacotes ja instalados, sem baixar pacotes nem reutilizar a Library do projeto aberto.
$packages = [ordered]@{}
foreach ($package in @('com.unity.ugui','com.unity.nuget.newtonsoft-json')) {
    $installed = Get-ChildItem -LiteralPath (Join-Path $workspacePath 'Library/PackageCache') -Directory -Filter ($package + '@*') | Select-Object -First 1
    if (-not $installed) { throw "Pacote local ausente: $package" }
    $packages[$package] = 'file:' + $installed.FullName.Replace('\','/')
}
$packagesPath = Join-Path $testProject 'Packages'
$null = New-Item -ItemType Directory -Path $packagesPath -Force
[IO.File]::WriteAllText((Join-Path $packagesPath 'manifest.json'), (@{ dependencies=$packages } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))

# Fontes e materiais TMP usados pela cena real (as demais dependencias XR sao verificadas no YAML original).
$fontGuid = '360c747cb61f0c84485b001e3e1d31c0'
$fontMeta = & rg -l --glob '*.meta' ('guid: ' + $fontGuid) (Join-Path $workspacePath 'Assets')
foreach ($metaPath in $fontMeta) {
    $fontPath = $metaPath.Substring(0, $metaPath.Length - 5)
    $relative = $fontPath.Substring($workspacePath.Length + 1)
    Copy-FixtureFile $relative
}
$tmpResources = Join-Path $workspacePath 'Assets/TextMesh Pro/Resources'
if (Test-Path -LiteralPath $tmpResources) {
    Get-ChildItem -LiteralPath $tmpResources -Recurse -File | Where-Object { $_.Extension -ne '.meta' } | ForEach-Object {
        Copy-FixtureFile $_.FullName.Substring($workspacePath.Length + 1)
    }
}
$reportPath = Join-Path $testProject 'MenuCanvasFlowChecks.log'
[IO.File]::WriteAllText($reportPath, '', [Text.UTF8Encoding]::new($false))
Write-Output 'Executando Unity em projeto de teste isolado, sem login real ou rede Supabase.'
$arguments = @('-batchmode','-nographics','-projectPath',('"' + $testProject + '"'),'-executeMethod','MainProjeto.MenuPrincipal.Editor.MenuCanvasFlowChecks.RunBatch','-logFile',('"' + $reportPath + '"'))
$process = Start-Process -FilePath (Join-Path $UnityEditor 'Unity.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru
$deadline = [DateTime]::UtcNow.AddMinutes(4)
while (-not $process.WaitForExit(1000)) {
    $licenseFailed = $false
    if (Test-Path -LiteralPath $reportPath) {
        $licenseFailed = [IO.File]::ReadAllText($reportPath).Contains('Licensing initialization failed')
    }
    if ([DateTime]::UtcNow -ge $deadline -or $licenseFailed) {
        # Processo criado por este script; nunca encerrar o Editor original do responsavel.
        if (-not $process.HasExited) { Stop-Process -Id $process.Id }
        throw ('Teste Play Mode interrompido por prazo ou licenciamento. Consulte: ' + $reportPath)
    }
}
$log = [IO.File]::ReadAllText($reportPath)
$result = [regex]::Match($log, 'MENU_CANVAS_CHECKS: PASS \d+[^\r\n]*')
if (-not $result.Success) { throw ('Unity nao concluiu o teste com sucesso. Consulte: ' + $reportPath) }
Write-Output $result.Value
Write-Output ('Log: ' + $reportPath)
