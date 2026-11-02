$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$workspacePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$webPath = Join-Path $workspacePath 'Web/participantes-vr'
$outputDirectory = Join-Path $workspacePath 'Deliverables'
$null = New-Item -ItemType Directory -Path $outputDirectory -Force
$zipPath = Join-Path $outputDirectory 'frontend-participantes-vr.zip'
$temporaryZip = $zipPath + '.tmp'
$sources = @()
foreach ($folder in @('dist','tests')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $webPath $folder) -Recurse -File) {
        $sources += @{ Source=$file.FullName; Entry=$file.FullName.Substring($webPath.Length+1).Replace('\','/') }
    }
}
foreach ($file in @('package.json','README.md','serve.mjs','.openai/hosting.json')) {
    $sources += @{ Source=(Join-Path $webPath $file); Entry=$file }
}
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $workspacePath 'Supabase') -Filter '*.sql' -Recurse -File) {
    $sources += @{ Source=$file.FullName; Entry=$file.FullName.Substring($workspacePath.Length+1).Replace('\','/') }
}
foreach ($guide in @(
    @{ Path='Docs/AdministradorSupabaseFrontend.md'; Name='GUIA_ADMINISTRADOR.md' },
    @{ Path='Docs/EnvioTelemetriaSupabase.md'; Name='GUIA_ENVIO_SUPABASE.md' },
    @{ Path='Docs/AnaliseTelemetriaFrontend.md'; Name='GUIA_TELEMETRIA.md' }
)) { $sources += @{ Source=(Join-Path $workspacePath $guide.Path); Entry=$guide.Name } }
if (Test-Path -LiteralPath $temporaryZip) { Remove-Item -LiteralPath $temporaryZip }
$archive = [IO.Compression.ZipFile]::Open($temporaryZip,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($source in $sources) { $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$source.Source,$source.Entry,[IO.Compression.CompressionLevel]::Optimal) }
} finally { $archive.Dispose() }
$archive = [IO.Compression.ZipFile]::OpenRead($temporaryZip)
try {
    foreach ($source in $sources) {
        $entry = $archive.GetEntry($source.Entry)
        if (-not $entry) { throw ('Arquivo ausente no ZIP: '+$source.Entry) }
        $stored = $entry.Open(); $original = [IO.File]::OpenRead($source.Source)
        $hasher = [Security.Cryptography.SHA256]::Create()
        try {
            $storedHash = [Convert]::ToBase64String($hasher.ComputeHash($stored))
            $originalHash = [Convert]::ToBase64String($hasher.ComputeHash($original))
            if ($storedHash -ne $originalHash) { throw ('Conteudo divergente no ZIP: '+$source.Entry) }
        } finally { $stored.Dispose(); $original.Dispose(); $hasher.Dispose() }
    }
    if (@($archive.Entries | Where-Object { $_.FullName -match '(?i)(^|/)\.git/|AutomaticOperatorLogin|(^|/)Session\.json$' }).Count) { throw 'Arquivo de execucao ou credenciais Unity no pacote.' }
} finally { $archive.Dispose() }
Move-Item -LiteralPath $temporaryZip -Destination $zipPath -Force
Write-Output ('PASS: frontend, testes, SQL e guias empacotados; '+$sources.Count+' arquivos conferidos por SHA-256.')
