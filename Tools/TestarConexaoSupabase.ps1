param([switch]$ConfigurationOnly)
$ErrorActionPreference = 'Stop'
$workspacePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$configPath = Join-Path $workspacePath 'Web/participantes-vr/dist/config.js'

Write-Host '=== Teste Supabase: sem email, senha ou alteracao de dados ==='
try {
    $source = [IO.File]::ReadAllText($configPath)
    $json = [regex]::Match($source, 'Object\.freeze\((.+)\);').Groups[1].Value
    $config = $json | ConvertFrom-Json
    $projectUri = [Uri]::new([string]$config.supabaseUrl)
    if ($projectUri.Scheme -ne 'https' -or $projectUri.UserInfo -or $projectUri.Query -or $projectUri.Fragment -or
        $projectUri.AbsolutePath.TrimEnd('/') -notin @('', '/rest/v1')) { throw 'Invalid URL' }
    $publicKey = [string]$config.publicKey
    $legacy = $false
    if ($publicKey -match '^sb_publishable_[A-Za-z0-9_-]+$') {
        $keyType = 'Publishable'
    } else {
        $parts = $publicKey.Split('.')
        if ($parts.Length -ne 3) { throw 'Invalid key' }
        $payload = $parts[1].Replace('-', '+').Replace('_', '/')
        $payload = $payload.PadRight([int]([math]::Ceiling($payload.Length / 4)) * 4, '=')
        $claims = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload)) | ConvertFrom-Json
        if ($claims.role -ne 'anon') { throw 'Invalid key' }
        $legacy = $true
        $keyType = 'anon'
    }
    $projectUrl = $projectUri.GetLeftPart([UriPartial]::Authority)
} catch {
    Write-Host 'CONFIG: FALHOU - nao foi possivel ler uma URL HTTPS e uma chave publica valida.'
    exit 1
}
Write-Host ('Projeto: ' + $projectUrl)
Write-Host ('Chave: ' + $keyType + ' configurada (valor oculto)')
if ($ConfigurationOnly) {
    Write-Host 'CONFIG: OK - nenhum pedido de rede foi realizado.'
    exit 0
}

try {
    $null = Resolve-DnsName -Name $projectUri.DnsSafeHost -Type A -DnsOnly -QuickTimeout -ErrorAction Stop
    Write-Host 'DNS: OK'
} catch {
    Write-Host 'DNS: FALHOU - o endereco nao foi encontrado nesta rede.'
    Write-Host 'Confira a Project URL e se o projeto esta ativo no painel Supabase.'
    Write-Host 'Copie este resultado para o chat. A chave nao precisa ser digitada novamente.'
    exit 1
}

Add-Type -AssemblyName System.Net.Http
$handler = [Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$handler.UseCookies = $false
# Preservar validacao normal de certificados TLS. Nunca ignorar erros de certificado.
$http = [Net.Http.HttpClient]::new($handler)
$http.Timeout = [TimeSpan]::FromSeconds(12)

function Invoke-ConnectionProbe([switch]$Preflight) {
    $request = $null
    $response = $null
    try {
        if ($Preflight) {
            $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Options, ($projectUrl + '/auth/v1/token?grant_type=password'))
            $request.Headers.Add('Origin', 'http://127.0.0.1:4173')
            $request.Headers.Add('Access-Control-Request-Method', 'POST')
            $request.Headers.Add('Access-Control-Request-Headers', 'apikey,content-type')
        } else {
            $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, ($projectUrl + '/auth/v1/health'))
            $request.Headers.Add('apikey', $publicKey)
            if ($legacy) { $request.Headers.Add('Authorization', ('Bearer ' + $publicKey)) }
        }
        $response = $http.SendAsync($request).GetAwaiter().GetResult()
        $originAllowed = $false
        $headersAllowed = $false
        if ($response.Headers.Contains('Access-Control-Allow-Origin')) {
            $origin = [string]::Join(',', $response.Headers.GetValues('Access-Control-Allow-Origin'))
            $originAllowed = $origin -in @('*', 'http://127.0.0.1:4173')
        }
        if ($response.Headers.Contains('Access-Control-Allow-Headers')) {
            $allowed = [string]::Join(',', $response.Headers.GetValues('Access-Control-Allow-Headers')).ToLowerInvariant()
            $headersAllowed = $allowed -eq '*' -or ($allowed.Contains('apikey') -and $allowed.Contains('content-type'))
        }
        return [pscustomobject]@{Status=[int]$response.StatusCode; OriginAllowed=$originAllowed; HeadersAllowed=$headersAllowed; ErrorCode=$null}
    } catch {
        $exception = $_.Exception
        $safeCode = 'NETWORK_OR_TLS_ERROR'
        while ($null -ne $exception) {
            if ($exception -is [Net.Sockets.SocketException]) { $safeCode = 'SOCKET_' + $exception.SocketErrorCode.ToString(); break }
            if ($exception -is [Net.WebException]) { $safeCode = 'WEB_' + $exception.Status.ToString(); break }
            if ($exception -is [Security.Authentication.AuthenticationException]) { $safeCode = 'TLS_CERTIFICATE_ERROR'; break }
            if ($exception -is [Threading.Tasks.TaskCanceledException]) { $safeCode = 'TIMEOUT'; break }
            $exception = $exception.InnerException
        }
        return [pscustomobject]@{Status=0; OriginAllowed=$false; HeadersAllowed=$false; ErrorCode=$safeCode}
    } finally {
        if ($null -ne $response) { $response.Dispose() }
        if ($null -ne $request) { $request.Dispose() }
    }
}

try {
    $health = Invoke-ConnectionProbe
    if ($health.Status -eq 0) {
        Write-Host ('HTTPS_AUTH: FALHOU - ' + $health.ErrorCode)
        Write-Host 'A conexao falhou antes de verificar usuario e senha. Ainda nao e possivel validar a chave.'
        Write-Host 'Copie este resultado para o chat.'
        exit 1
    }
    Write-Host ('HTTPS_AUTH: HTTP ' + $health.Status)
    if ($health.Status -in @(401,403)) {
        Write-Host 'Resultado: o servidor foi alcancado, mas recusou a chave ou o acesso ao projeto.'
        Write-Host 'Confirme a Publishable key nas API Keys do mesmo projeto. Nao envie a chave no chat.'
        exit 1
    }
    if ($health.Status -lt 200 -or $health.Status -ge 300) {
        Write-Host 'Resultado: o servidor respondeu com erro. Confira o estado do projeto no painel Supabase.'
        exit 1
    }
    $preflight = Invoke-ConnectionProbe -Preflight
    if ($preflight.Status -eq 0) {
        Write-Host ('PREFLIGHT_CORS: FALHOU - ' + $preflight.ErrorCode)
    } else {
        Write-Host ('PREFLIGHT_CORS: HTTP ' + $preflight.Status)
        if ($preflight.Status -ge 200 -and $preflight.Status -lt 300 -and $preflight.OriginAllowed -and $preflight.HeadersAllowed) {
            Write-Host 'CORS_ORIGIN_AND_HEADERS: OK'
            Write-Host 'Resultado: o servico responde neste PowerShell. Se o navegador falhar, precisamos investigar o erro no Console dele.'
        } else {
            Write-Host 'CORS_ORIGIN_AND_HEADERS: NAO_CONFIRMADO'
            Write-Host 'Resultado: o teste de permissao de acesso pelo navegador nao foi confirmado.'
        }
    }
    Write-Host 'Copie este resultado para o chat. Nenhuma senha ou valor de chave foi exibido.'
} finally {
    $publicKey = $null
    $http.Dispose()
}
