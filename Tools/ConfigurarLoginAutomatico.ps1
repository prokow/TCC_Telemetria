param([switch]$Remover, [switch]$ConfigurationOnly)
$ErrorActionPreference = 'Stop'
$workspacePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$securePassword = $null
$request = $null
$response = $null
$http = $null
$handler = $null
try {
    $assetPath = Join-Path $workspacePath 'Assets/MainProjeto/Telemetria/Supabase/Settings/MenuSupabaseConnection.asset'
    $asset = [IO.File]::ReadAllText($assetPath)
    $projectUrl = [regex]::Match($asset, '(?m)^  projectUrl: ([^\r\n]+)').Groups[1].Value.Trim()
    $publicKey = [regex]::Match($asset, '(?m)^  publicApiKey: ([^\r\n]+)').Groups[1].Value.Trim()
    $storeSource = Join-Path $workspacePath 'Assets/MainProjeto/Telemetria/Supabase/WindowsSupabaseSessionStore.cs'
    if (-not ('MainProjeto.Telemetria.Supabase.WindowsSupabaseSessionStore' -as [type])) {
        Add-Type -TypeDefinition ([IO.File]::ReadAllText($storeSource))
    }
    $store = New-Object MainProjeto.Telemetria.Supabase.WindowsSupabaseSessionStore -ArgumentList $projectUrl
    $projectUri = [Uri]::new($projectUrl)
    $legacy = $false
    if ($publicKey -notmatch '^sb_publishable_[A-Za-z0-9_-]+$') {
        $parts = $publicKey.Split('.')
        if ($parts.Length -ne 3) { throw 'Chave publica invalida.' }
        $payload = $parts[1].Replace('-', '+').Replace('_', '/')
        $payload = $payload.PadRight([int]([math]::Ceiling($payload.Length / 4)) * 4, '=')
        $claims = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload)) | ConvertFrom-Json
        if ($claims.role -ne 'anon') { throw 'Use somente a chave publica, nunca service_role ou secret.' }
        $legacy = $true
    }
    if ($ConfigurationOnly) {
        Write-Host 'CONFIG: OK - configuracao e provedor Windows compilados; nenhuma rede ou credencial acessada.'
        return
    }
    if ($Remover) {
        $store.Clear()
        Write-Host 'Acesso automatico removido deste perfil Windows para este projeto.'
        return
    }
    Write-Host 'Feche o Play Mode e outras copias do simulador antes de continuar.'
    Write-Host 'Autorize uma vez com a conta do responsavel que ja consegue cadastrar participantes.'
    Write-Host 'A senha nao sera gravada no projeto. Nao envie senha ou tokens pelo chat.'
    $email = (Read-Host 'Email da conta do responsavel').Trim()
    $securePassword = Read-Host 'Senha' -AsSecureString
    $password = [Net.NetworkCredential]::new('', $securePassword).Password
    if ($email.IndexOf('@') -le 0 -or [string]::IsNullOrEmpty($password)) { throw 'Preencha email e senha.' }
    $body = @{ email = $email; password = $password } | ConvertTo-Json -Compress
    $password = $null
    Add-Type -AssemblyName System.Net.Http
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $handler.UseCookies = $false
    $http = [Net.Http.HttpClient]::new($handler)
    $http.Timeout = [TimeSpan]::FromSeconds(20)
    $url = $projectUri.GetLeftPart([UriPartial]::Authority) + '/auth/v1/token?grant_type=password'
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, $url)
    $request.Headers.Add('apikey', $publicKey)
    if ($legacy) { $request.Headers.Add('Authorization', ('Bearer ' + $publicKey)) }
    $request.Content = [Net.Http.StringContent]::new($body, [Text.Encoding]::UTF8, 'application/json')
    $body = $null
    $response = $http.SendAsync($request).GetAwaiter().GetResult()
    if (-not $response.IsSuccessStatusCode) {
        $status = [int]$response.StatusCode
        if ($status -eq 400 -or $status -eq 401 -or $status -eq 403) {
            Write-Host 'Login recusado. Confira email, senha, confirmacao de email e a chave publica do mesmo projeto.'
        } elseif ($status -eq 429) { Write-Host 'Muitas tentativas. Aguarde antes de tentar novamente.' }
        else { Write-Host ('Nao foi possivel autorizar. HTTP ' + $status + '. Tente novamente depois.') }
        exit 1
    }
    $session = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace([string]$session.refresh_token)) { throw 'Resposta de autorizacao invalida.' }
    $store.SaveRefreshToken([string]$session.refresh_token)
    Write-Host 'Pronto! Autorizacao salva no Gerenciador de Credenciais deste perfil Windows.'
    Write-Host 'Abra MainMenu no Unity e pressione Play. Escolha o participante e clique em Iniciar.'
} catch {
    # Nao imprimir excecoes HTTP ou respostas que possam conter dados de autenticacao.
    Write-Host 'Nao foi possivel configurar o acesso. Confira a conexao, a configuracao publica e tente novamente.'
    exit 1
} finally {
    $password = $null
    $body = $null
    $session = $null
    if ($request) { $request.Dispose() }
    if ($response) { $response.Dispose() }
    if ($http) { $http.Dispose() }
    if ($handler) { $handler.Dispose() }
    if ($securePassword) { $securePassword.Dispose() }
}
