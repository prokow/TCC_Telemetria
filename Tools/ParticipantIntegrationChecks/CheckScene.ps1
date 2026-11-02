$ErrorActionPreference = 'Stop'
$workspacePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$scenePath = Join-Path $workspacePath 'Assets/Scenes/MainMenu.unity'
$scene = [IO.File]::ReadAllText($scenePath).Replace("`r`n", "`n")
$script:checks = 0
function Assert-Scene($condition, $message) {
    if (-not $condition) { throw "FAIL: $message" }
    $script:checks++
}
function Read-Blocks($text) {
    $result = @{}
    foreach ($match in [regex]::Matches($text, '(?ms)^--- !u!(\d+) &(-?\d+)(?: stripped)?\n(.*?)(?=^--- !u!|\z)')) {
        $id = $match.Groups[2].Value
        Assert-Scene (-not $result.ContainsKey($id)) "fileID repetido: $id"
        $result[$id] = @{ Kind = $match.Groups[1].Value; Body = $match.Groups[3].Value; Text = $match.Value }
    }
    return $result
}
$blocks = Read-Blocks $scene
$yamlPackage = Get-ChildItem -LiteralPath (Join-Path $workspacePath 'Library/PackageCache') -Directory -Filter 'com.unity.visualscripting@*' | Select-Object -First 1
$yamlDll = Join-Path $yamlPackage.FullName 'Editor/VisualScripting.Core/Dependencies/YamlDotNet/Unity.VisualScripting.YamlDotNet.dll'
$yamlAssembly = [Reflection.Assembly]::LoadFrom($yamlDll)
$yamlStream = [Activator]::CreateInstance($yamlAssembly.GetType('Unity.VisualScripting.YamlDotNet.RepresentationModel.YamlStream'))
$plainYaml = [regex]::Replace($scene, '(?m)^%.*\n', '')
$plainYaml = [regex]::Replace($plainYaml, '(?m)^--- !u!\d+ &-?\d+(?: stripped)?$', '---')
$reader = [IO.StringReader]::new($plainYaml)
try { $yamlStream.Load($reader) } finally { $reader.Dispose() }
Assert-Scene ($yamlStream.Documents.Count -eq $blocks.Count) 'Todos os objetos devem ser YAML valido.'

foreach ($reference in [regex]::Matches($scene, '\{fileID: (-?\d+)\}')) {
    $id = $reference.Groups[1].Value
    Assert-Scene ($id -eq '0' -or $blocks.ContainsKey($id)) "Referencia local ausente: $id"
}
$names = @{}
foreach ($id in $blocks.Keys) {
    if ($blocks[$id].Kind -ne '1') { continue }
    $name = [regex]::Match($blocks[$id].Body, '(?m)^  m_Name: (.+)$').Groups[1].Value
    $names[$name] = $id
}
foreach ($requiredName in @('MenuScript','Menu','Sobre','EventSystem','ButtonIniciar','ButtonSair','ButtonParticipantes','ParticipantSelectionPanel','ParticipantItemTemplate','RefreshParticipantsButton','BackToMainMenuButton','ParticipantSummary','ApplicationSessionContext','OperatorLoginPanel','OperatorEmailInput','OperatorPasswordInput','OperatorSignInButton','OperatorLoginBackButton','ChangeOperatorButton')) {
    Assert-Scene ($names.ContainsKey($requiredName)) "GameObject ausente: $requiredName"
}
Assert-Scene ($names.ContainsKey('MainMenuPanel')) 'Menu principal precisa de um painel proprio no Canvas existente.'
Assert-Scene ($blocks[$names['MainMenuPanel']].Body.Contains('m_IsActive: 1')) 'Menu principal deve ser a entrada.'
Assert-Scene ($blocks[$names['ParticipantSelectionPanel']].Body.Contains('m_IsActive: 0')) 'Lista de participantes inicia fechada.'
Assert-Scene ($blocks[$names['OperatorLoginPanel']].Body.Contains('m_IsActive: 0')) 'Login manual inicia oculto.'
Assert-Scene ($blocks[$names['ChangeOperatorButton']].Body.Contains('m_IsActive: 0')) 'Troca manual de conta permanece oculta.'
Assert-Scene ($blocks[$names['BackToMainMenuButton']].Body.Contains('m_IsActive: 1')) 'Voltar ao menu precisa estar disponivel na lista.'
foreach ($id in @('1046524822','2113470907','900000000010063')) {
    Assert-Scene ($blocks[$id].Body.Contains('m_Father: {fileID: 1580990371}')) 'Tres botoes pertencem ao menu principal.'
    Assert-Scene ($blocks['1580990371'].Body.Contains('- {fileID: ' + $id + '}')) 'Grupo vertical contem os tres botoes.'
    Assert-Scene (-not $blocks['900000000010002'].Body.Contains('- {fileID: ' + $id + '}')) 'Lista nao pode conter Iniciar, Sair ou abrir lista.'
    Assert-Scene ($blocks[$id].Body.Contains('m_LocalScale: {x: 1, y: 1, z: 1}')) 'Botoes nao devem aplicar escala duplicada.'
}
foreach ($id in @('408327064','540048368','900000000010072')) {
    Assert-Scene ($blocks[$id].Body.Contains('m_Father: {fileID: 900000000050002}')) 'Titulo, botoes e resumo pertencem ao painel principal.'
    Assert-Scene ($blocks['900000000050002'].Body.Contains('- {fileID: ' + $id + '}')) 'Painel principal deve listar seus filhos.'
}
foreach ($id in @('900000000010045','900000000010054')) {
    Assert-Scene ($blocks[$id].Body.Contains('m_Father: {fileID: 900000000010002}')) 'Atualizar e Voltar pertencem a lista.'
}
Assert-Scene ($blocks['900000000010076'].Body.Contains('mainPanel: {fileID: 900000000050001}')) 'Controller deve apontar para o painel principal.'
Assert-Scene ($blocks['1407570484'].Body.Contains('m_RenderMode: 2')) 'Canvas deve preservar World Space.'
Assert-Scene ($blocks['1580990372'].Body.Contains('m_Spacing: 12')) 'Botoes do menu devem ter espaco para leitura e apontamento.'
Assert-Scene ($blocks['1580990372'].Body.Contains('m_ChildForceExpandHeight: 0')) 'Layout deve preservar a altura dos botoes.'
Assert-Scene ($blocks['900000000010061'].Body.Contains('m_text: Voltar ao menu principal')) 'Voltar deve identificar o destino.'
Assert-Scene ($blocks['900000000010070'].Body.Contains('m_text: Selecionar participantes')) 'Botao abre a selecao de participantes.'
Assert-Scene ($blocks['2106578369'].Body.Contains('m_text: Iniciar simulador')) 'Botao Iniciar deve estar rotulado.'
Assert-Scene ($blocks['1211630508'].Body.Contains('m_text: Sair do simulador')) 'Botao Sair deve estar rotulado.'

# Medidas locais no painel (antes da escala World Space); conferir limites e areas sem sobreposicao.
function Read-Rect([string]$id) {
    $body = $blocks[$id].Body
    $position = [regex]::Match($body, 'm_AnchoredPosition: \{x: ([-\d.]+), y: ([-\d.]+)\}')
    $size = [regex]::Match($body, 'm_SizeDelta: \{x: ([-\d.]+), y: ([-\d.]+)\}')
    $culture = [Globalization.CultureInfo]::InvariantCulture
    $x = [double]::Parse($position.Groups[1].Value, $culture)
    $y = [double]::Parse($position.Groups[2].Value, $culture)
    $width = [double]::Parse($size.Groups[1].Value, $culture)
    $height = [double]::Parse($size.Groups[2].Value, $culture)
    return @{ Left=$x-$width/2; Right=$x+$width/2; Bottom=$y-$height/2; Top=$y+$height/2; Width=$width; Height=$height }
}
function Assert-Inside($inner, $outer, $description) {
    Assert-Scene ($inner.Left -ge $outer.Left -and $inner.Right -le $outer.Right -and $inner.Bottom -ge $outer.Bottom -and $inner.Top -le $outer.Top) $description
}
function Assert-NoOverlap($one, $two, $description) {
    Assert-Scene ($one.Right -le $two.Left -or $two.Right -le $one.Left -or $one.Top -le $two.Bottom -or $two.Top -le $one.Bottom) $description
}
$mainBounds = Read-Rect '900000000050002'
$titleBounds = Read-Rect '408327064'
$buttonsBounds = Read-Rect '540048368'
$summaryBounds = Read-Rect '900000000010072'
foreach ($rect in @($titleBounds, $buttonsBounds, $summaryBounds)) { Assert-Inside $rect $mainBounds 'Elemento do menu precisa caber dentro do painel.' }
Assert-NoOverlap $titleBounds $buttonsBounds 'Titulo nao deve se sobrepor aos botoes.'
Assert-NoOverlap $buttonsBounds $summaryBounds 'Resumo nao deve se sobrepor aos botoes.'
$buttonTotalHeight = 24 # Dois espacos de 12 entre tres botoes.
foreach ($id in @('1046524822','2113470907','900000000010063')) {
    $rect = Read-Rect $id
    Assert-Scene ($rect.Width -le $buttonsBounds.Width) 'Botao precisa caber na largura do grupo.'
    $buttonTotalHeight += $rect.Height
}
Assert-Scene ($buttonTotalHeight -le $buttonsBounds.Height) 'Altura dos tres botoes e espacos precisa caber no grupo.'
$listBounds = Read-Rect '900000000010002'
$refreshBounds = Read-Rect '900000000010045'
$backBounds = Read-Rect '900000000010054'
$scrollBounds = Read-Rect '900000000010018'
Assert-Inside $refreshBounds $listBounds 'Atualizar precisa caber no painel.'
Assert-Inside $backBounds $listBounds 'Voltar precisa caber no painel.'
Assert-NoOverlap $refreshBounds $backBounds 'Atualizar e Voltar nao devem se sobrepor.'
Assert-NoOverlap $scrollBounds $refreshBounds 'Lista nao deve cobrir Atualizar.'
Assert-NoOverlap $scrollBounds $backBounds 'Lista nao deve cobrir Voltar.'
Assert-Scene ($blocks['900000000010076'].Body.Contains('automaticLogin: {fileID: 900000000040001}')) 'Fluxo automatico deve estar atribuido no controller.'
Assert-Scene ($blocks[$names['ParticipantItemTemplate']].Body.Contains('m_IsActive: 0')) 'Modelo da linha inicia inativo.'
Assert-Scene ($blocks['1046524823'].Body.Contains('m_Interactable: 0')) 'Iniciar deve comecar bloqueado.'
Assert-Scene ($blocks['1046524823'].Body.Contains('m_MethodName: IniciarSimulador')) 'Callback Iniciar preservado.'
Assert-Scene ($blocks['2113470908'].Body.Contains('m_MethodName: Sair')) 'Callback Sair preservado.'
foreach ($id in @('1046524823','2113470908')) {
    Assert-Scene ($blocks[$id].Body.Contains('MainProjeto.MenuPrincipal.Scripts.MainMenu, Assembly-CSharp')) 'Tipo do callback deve incluir namespace.'
}

foreach ($typeName in @('MenuIntegrationController','ParticipantSelectionUI','ParticipantListItemUI','ApplicationSessionContext','OperatorLoginUI','ParticipantAutoRefresh','AutomaticOperatorLogin')) {
    $components = @($blocks.Values | Where-Object { $_.Kind -eq '114' -and $_.Body -match ('m_EditorClassIdentifier: .*\.' + $typeName + '\n') })
    Assert-Scene ($components.Count -eq 1) "Deve haver um componente $typeName."
    $guid = [regex]::Match($components[0].Body, 'm_Script: \{fileID: 11500000, guid: ([a-f0-9]+)').Groups[1].Value
    $scriptFile = if ($typeName -eq 'ApplicationSessionContext') { 'Assets/MainProjeto/Telemetria/Sessions/' + $typeName + '.cs.meta' } else { 'Assets/MainProjeto/MenuPrincipal/Scripts/' + $typeName + '.cs.meta' }
    $meta = [IO.File]::ReadAllText((Join-Path $workspacePath $scriptFile))
    Assert-Scene ($meta.Contains('guid: ' + $guid)) "GUID do script $typeName deve resolver."
    foreach ($field in [regex]::Matches($components[0].Body, '(?m)^  ([a-z][A-Za-z]+): \{fileID: (-?\d+)(?:, guid: ([a-f0-9]+), type: \d+)?\}$')) {
        $referenceId = $field.Groups[2].Value
        Assert-Scene ($referenceId -ne '0') "Campo $typeName.$($field.Groups[1].Value) deve estar atribuido."
        if (-not $field.Groups[3].Success) { Assert-Scene ($blocks.ContainsKey($referenceId)) 'Referencia de Inspector deve resolver.' }
    }
}

# Validar as relacoes pai/filho dos RectTransforms acrescentados.
foreach ($id in @($blocks.Keys | Where-Object { $_.StartsWith('9000000000') })) {
    $block = $blocks[$id]
    if ($block.Kind -ne '224') { continue }
    $parent = [regex]::Match($block.Body, 'm_Father: \{fileID: (-?\d+)\}').Groups[1].Value
    Assert-Scene ($blocks[$parent].Body.Contains('- {fileID: ' + $id + '}')) 'Pai deve conter o RectTransform filho.'
}
foreach ($input in @($blocks.Values | Where-Object { $_.Body.Contains('Unity.TextMeshPro::TMPro.TMP_InputField') })) {
    Assert-Scene ($input.Body.Contains('m_Text: ""')) 'Campos de login devem estar vazios na cena.'
    Assert-Scene ($input.Body.Contains('m_RestoreOriginalTextOnEscape: 0')) 'Escape nao deve restaurar uma senha apagada.'
    Assert-Scene ($input.Body.Contains('m_RichText: 0')) 'Campos nao devem interpretar rich text.'
    if ($input.Body.Contains('m_ContentType: 7')) { Assert-Scene ($input.Body.Contains('m_InputType: 2')) 'Senha deve estar mascarada.' }
}
$buildSettings = [IO.File]::ReadAllText((Join-Path $workspacePath 'ProjectSettings/EditorBuildSettings.asset'))
Assert-Scene ($buildSettings.IndexOf('Assets/Scenes/MainMenu.unity') -lt $buildSettings.IndexOf('Assets/Scenes/SampleScene.unity')) 'MainMenu deve ser a entrada do build.'

$backupPath = Join-Path $workspacePath 'Temp/MainMenuParticipants.before.unity'
if (Test-Path -LiteralPath $backupPath) {
    $original = Read-Blocks ([IO.File]::ReadAllText($backupPath).Replace("`r`n", "`n"))
    # Apenas controles do menu e layout relacionado podem mudar; XR e cenario sao preservados.
    $allowedChanges = @('20118859','20118860','1407570480','1580990371','1046524823','2113470908','9223372036854775807','540048367','408327063','1046524822','2113470907','1211630508','408327064','540048368','1580990372','588341373','2106578369')
    foreach ($id in $original.Keys) {
        Assert-Scene ($blocks.ContainsKey($id)) "Objeto original preservado: $id"
        if ($id -notin $allowedChanges) { Assert-Scene ($original[$id].Text -eq $blocks[$id].Text) "Objeto fora da integracao nao deve mudar: $id" }
    }
}
Write-Output ("PASS: $script:checks verificacoes de YAML, referencias, callbacks e preservacao da cena.")
