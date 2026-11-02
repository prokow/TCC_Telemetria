param([int]$Latest=3)
$ErrorActionPreference='Stop'
$workspacePath=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$settings=[IO.File]::ReadAllText((Join-Path $workspacePath 'ProjectSettings/ProjectSettings.asset'))
$company=[regex]::Match($settings,'(?m)^  companyName: (.+)$').Groups[1].Value.Trim()
$product=[regex]::Match($settings,'(?m)^  productName: (.+)$').Groups[1].Value.Trim()
$telemetryRoot=Join-Path $env:USERPROFILE ('AppData/LocalLow/'+$company+'/'+$product+'/Sessoes_Pendentes')
$results=@()
if(Test-Path -LiteralPath $telemetryRoot) {
    foreach($folder in (Get-ChildItem -LiteralPath $telemetryRoot -Directory | Where-Object {$_.Name -match '^[a-f0-9]{32}$'} | Sort-Object LastWriteTime -Descending | Select-Object -First ([Math]::Max(1,[Math]::Min(20,$Latest))))) {
        $result=[ordered]@{session=$folder.Name}
        foreach($name in @('Head.csv','Controllers.csv')) {
            $path=Join-Path $folder.FullName $name
            if(-not(Test-Path -LiteralPath $path)){$result[$name]=@{exists=$false};continue}
            $stream=[IO.File]::OpenText($path)
            try {
                $header=$stream.ReadLine(); $samples=0; $poses=[Collections.Generic.HashSet[string]]::new()
                while($null -ne ($line=$stream.ReadLine())) {
                    if(!$line.Trim()){continue}; $samples++; $fields=$line.Split(',')
                    $start=if($header.StartsWith('ParticipantCode,')){3}else{1}
                    if($fields.Length -ge $start+6 -and $poses.Count -lt 1000000){$null=$poses.Add(($fields[$start..($fields.Length-1)] -join ','))}
                }
                $result[$name]=@{exists=$true;samples=$samples;distinctPoseAndInputRows=$poses.Count;uuidColumns=$header.StartsWith('ParticipantCode,SessionId,')}
            }finally{$stream.Dispose()}
        }
        $statePath=Join-Path $folder.FullName 'UploadState.json'
        if(Test-Path -LiteralPath $statePath){$state=[IO.File]::ReadAllText($statePath)|ConvertFrom-Json;$result.uploadStatus=$state.Status;$result.confirmedFiles=$state.ConfirmedFiles}
        $results += $result
    }
}
$reportDirectory=Join-Path $workspacePath 'Temp/SampleSceneChecks'; $null=New-Item -ItemType Directory -Path $reportDirectory -Force
[IO.File]::WriteAllText((Join-Path $reportDirectory 'movement-csv-report.json'),(@{mode='Leitura dos CSVs locais reais; nao consulta o Supabase remoto';sessions=@($results)}|ConvertTo-Json -Depth 7),[Text.UTF8Encoding]::new($false))
if(!$results.Count){Write-Output 'Movimentos: nenhuma sessao local encontrada neste computador.'}
foreach($result in $results){Write-Output ($result | ConvertTo-Json -Depth 5 -Compress)}
