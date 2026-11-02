param([string]$ProjectUrl = 'https://qvszuzmwyfuyydfjjvxb.supabase.co')
$ErrorActionPreference = 'Stop'
$workspacePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$uri = $null
if (-not [Uri]::TryCreate($ProjectUrl, [UriKind]::Absolute, [ref]$uri) -or
    $uri.Scheme -ne 'https' -or $uri.UserInfo -or $uri.Query -or $uri.Fragment -or
    $uri.AbsolutePath.TrimEnd('/') -notin @('', '/rest/v1')) {
    throw 'Informe uma Project URL HTTPS válida, sem credenciais ou parâmetros.'
}
$normalizedUrl = $uri.GetLeftPart([UriPartial]::Authority)
Write-Host ('Projeto: ' + $normalizedUrl)
Write-Host 'Cole a Publishable key do mesmo projeto (sb_publishable_...) ou a chave anon legada.'
Write-Host 'Não utilize Secret, service_role ou senha de banco. A chave não será exibida.'
$secureKey = Read-Host 'Chave pública' -AsSecureString
$publicKey = [Net.NetworkCredential]::new('', $secureKey).Password.Trim()
try {
    $valid = $publicKey -match '^sb_publishable_[A-Za-z0-9_-]+$'
    if (-not $valid) {
        try {
            $parts = $publicKey.Split('.')
            if ($parts.Length -ne 3) { throw 'Formato inválido.' }
            $base64 = $parts[1].Replace('-', '+').Replace('_', '/')
            $base64 = $base64.PadRight([int]([math]::Ceiling($base64.Length / 4)) * 4, '=')
            $payload = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($base64)) | ConvertFrom-Json
            $valid = $payload.role -eq 'anon'
            if ($valid -and $uri.DnsSafeHost.EndsWith('.supabase.co')) {
                $valid = $payload.ref -eq $uri.DnsSafeHost.Split('.')[0]
            }
        } catch { $valid = $false }
    }
    if (-not $valid) { throw 'Chave recusada. Use Publishable ou anon do projeto indicado, nunca uma chave administrativa.' }

    $frontendPath = Join-Path $workspacePath 'Web/participantes-vr/dist/config.js'
    $assetPath = Join-Path $workspacePath 'Assets/MainProjeto/Telemetria/Supabase/Settings/MenuSupabaseConnection.asset'
    $legacyPath = Join-Path $workspacePath 'Assets/MainProjeto/Telemetria/Supabase/SupabaseManager.cs'
    foreach ($path in @($frontendPath,$assetPath,$legacyPath)) {
        if (-not (Test-Path -LiteralPath $path)) { throw 'Execute este script na cópia completa do projeto Unity.' }
    }
    $asset = [IO.File]::ReadAllText($assetPath)
    $legacy = [IO.File]::ReadAllText($legacyPath)
    if ($asset -notmatch '(?m)^  publicApiKey:' -or $asset -notmatch '(?m)^  projectUrl:' -or
        $legacy -notmatch 'public string supabaseUrl\s*=' -or $legacy -notmatch 'public string supabaseAnonKey\s*=') {
        throw 'Os arquivos de configuração não possuem os campos esperados. Nenhuma alteração foi aplicada.'
    }
    $asset = [regex]::Replace($asset,'(?m)^  projectUrl:.*$',('  projectUrl: ' + $normalizedUrl))
    $asset = [regex]::Replace($asset,'(?m)^  publicApiKey:.*$',('  publicApiKey: ' + $publicKey))
    $legacy = [regex]::Replace($legacy,'(public string supabaseUrl\s*=\s*)"[^"]*"',('${1}"' + $normalizedUrl + '"'))
    $legacy = [regex]::Replace($legacy,'(public string supabaseAnonKey\s*=\s*)"[^"]*"',('${1}"' + $publicKey + '"'))
    $config = [ordered]@{supabaseUrl=$normalizedUrl;publicKey=$publicKey} | ConvertTo-Json -Compress
    $utf8 = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText($frontendPath,('// Somente URL e chave pública. Permissões verificadas pelo Supabase.' + "`nexport const config = Object.freeze(" + $config + ");`n"),$utf8)
    [IO.File]::WriteAllText($assetPath,$asset,$utf8)
    [IO.File]::WriteAllText($legacyPath,$legacy,$utf8)
    Write-Host 'Frontend e Unity configurados. Recarregue o navegador com Ctrl+F5 e teste o login.'
    Write-Host 'O servidor ainda verificará se a chave pública é válida para este projeto.'
} finally {
    $publicKey = $null
    $secureKey.Dispose()
}
