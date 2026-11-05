param([double]$Threshold = 0.0)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$python = Join-Path $projectRoot '.tools/coacd/Scripts/python.exe'
$cache = Join-Path $projectRoot '.tools/coacd/cache'
$worker = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'coacd_boxes.py') -Raw
foreach ($request in Get-ChildItem -LiteralPath $cache -Filter '*.input.json') {
    $output = $request.FullName.Replace('.input.json', '.json')
    if (Test-Path -LiteralPath $output) { continue }
    $data = Get-Content -LiteralPath $request.FullName -Raw | ConvertFrom-Json
    if ($data.worker -ne $worker -or ($Threshold -gt 0 -and $data.threshold -ne $Threshold)) { continue }
    Write-Output ('Preparing ' + $request.BaseName)
    & $python (Join-Path $PSScriptRoot 'coacd_boxes.py') $request.FullName $output
    if ($LASTEXITCODE -ne 0) { throw ('CoACD failed: ' + $request.Name) }
}
