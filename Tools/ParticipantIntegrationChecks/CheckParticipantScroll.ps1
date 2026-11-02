param([string]$Baseline = '')
$ErrorActionPreference = 'Stop'
$workspacePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$scene = [IO.File]::ReadAllText((Join-Path $workspacePath 'Assets/Scenes/MainMenu.unity')).Replace("`r`n", "`n")
$script:checks = 0
function Assert-Scroll($condition, $message) {
    if (-not $condition) { throw "FAIL: $message" }
    $script:checks++
}
function Read-Blocks([string]$text) {
    $result = @{}
    foreach ($match in [regex]::Matches($text, '(?ms)^--- !u!(\d+) &(-?\d+)(?: stripped)?\n(.*?)(?=^--- !u!|\z)')) {
        $id = $match.Groups[2].Value
        Assert-Scroll (-not $result.ContainsKey($id)) "fileID repetido: $id"
        $result[$id] = $match.Groups[3].Value
    }
    return $result
}
$blocks = Read-Blocks $scene
$yamlPackage = Get-ChildItem -LiteralPath (Join-Path $workspacePath 'Library/PackageCache') -Directory -Filter 'com.unity.visualscripting@*' | Select-Object -First 1
$yamlDll = Join-Path $yamlPackage.FullName 'Editor/VisualScripting.Core/Dependencies/YamlDotNet/Unity.VisualScripting.YamlDotNet.dll'
$assembly = [Reflection.Assembly]::LoadFrom($yamlDll)
$yaml = [Activator]::CreateInstance($assembly.GetType('Unity.VisualScripting.YamlDotNet.RepresentationModel.YamlStream'))
$plain = [regex]::Replace($scene, '(?m)^%.*\n', '')
$plain = [regex]::Replace($plain, '(?m)^--- !u!\d+ &-?\d+(?: stripped)?$', '---')
$reader = [IO.StringReader]::new($plain)
try { $yaml.Load($reader) } finally { $reader.Dispose() }
Assert-Scroll ($yaml.Documents.Count -eq $blocks.Count) 'Cena deve ser YAML valido.'
foreach ($reference in [regex]::Matches($scene, '\{fileID: (-?\d+)\}')) {
    $id = $reference.Groups[1].Value
    Assert-Scroll ($id -eq '0' -or $blocks.ContainsKey($id)) "Referencia local ausente: $id"
}
function Has([string]$id, [string]$field) { Assert-Scroll ($blocks[$id].Contains($field)) "$id precisa de $field" }
Has '900000000010028' 'm_Horizontal: 0'
Has '900000000010028' 'm_Vertical: 1'
Has '900000000010028' 'm_MovementType: 2'
Has '900000000010028' 'm_Content: {fileID: 900000000010025}'
Has '900000000010028' 'm_Viewport: {fileID: 900000000010022}'
Has '900000000010028' 'm_VerticalScrollbar: {fileID: 900000000060005}'
Has '900000000010028' 'm_VerticalScrollbarVisibility: 1'
Has '900000000010018' '- {fileID: 900000000060002}'
Has '900000000010022' 'm_SizeDelta: {x: -32, y: 0}'
Has '900000000010023' 'guid: 3312d7739989d2b4e91e6319e9a96d76'
Has '900000000010025' 'm_Pivot: {x: 0.5, y: 1}'
Has '900000000010027' 'm_VerticalFit: 2'
Has '900000000060005' 'guid: 2a4db7a114972834c8e4117be1d82ba3'
Has '900000000060005' 'm_HandleRect: {fileID: 900000000060009}'
Has '900000000060005' 'm_TargetGraphic: {fileID: 900000000060011}'
Has '900000000060005' 'm_Direction: 2'
Has '900000000060005' 'm_Interactable: 1'
Has '900000000060002' 'm_Father: {fileID: 900000000010018}'
Has '900000000060007' 'm_Father: {fileID: 900000000060002}'
Has '900000000060009' 'm_Father: {fileID: 900000000060007}'
foreach ($id in @('900000000060004','900000000060011')) { Has $id 'm_RaycastTarget: 1' }
Has '1407570484' 'm_RenderMode: 2'
Has '900000000010075' 'scrollRect: {fileID: 900000000010028}'
$height = [double][regex]::Match($blocks['900000000010018'], 'm_SizeDelta: \{x: [^,]+, y: ([\d.]+)\}').Groups[1].Value
$rowHeight = [double][regex]::Match($blocks['900000000010034'], 'm_PreferredHeight: ([\d.]+)').Groups[1].Value
$spacing = [double][regex]::Match($blocks['900000000010026'], 'm_Spacing: ([\d.]+)').Groups[1].Value
$padding = [double][regex]::Match($blocks['900000000010026'], 'm_Top: ([\d.]+)').Groups[1].Value + [double][regex]::Match($blocks['900000000010026'], 'm_Bottom: ([\d.]+)').Groups[1].Value
Assert-Scroll (($rowHeight * 2 + $spacing + $padding) -le $height) 'Dois participantes devem caber.'
foreach ($count in @(3,8,10,100)) {
    Assert-Scroll (($rowHeight * $count + $spacing * ($count - 1) + $padding) -gt $height) "$count participantes precisam de rolagem."
}
if ($Baseline) {
    $before = Read-Blocks ([IO.File]::ReadAllText($Baseline).Replace("`r`n", "`n"))
    $changed = @('900000000010018','900000000010022','900000000010028','900000000010036','900000000010040')
    foreach ($id in $before.Keys) {
        if ($changed -notcontains $id) { Assert-Scroll ($blocks[$id] -ceq $before[$id]) "Objeto fora do escopo alterado: $id" }
    }
    Assert-Scroll ($blocks.Count -eq $before.Count + 11) 'Apenas os onze objetos/componentes da barra foram adicionados.'
}
Write-Output "PARTICIPANT_SCROLL: PASS $script:checks verificacoes de cena, referencias e dimensoes."
