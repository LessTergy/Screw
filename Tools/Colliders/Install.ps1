$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$env:UV_CACHE_DIR = Join-Path $projectRoot '.tools/uv-cache'
$env:UV_PYTHON_INSTALL_DIR = Join-Path $projectRoot '.tools/python'
$environmentPath = Join-Path $projectRoot '.tools/coacd'
if (-not (Test-Path -LiteralPath (Join-Path $environmentPath 'Scripts/python.exe'))) {
    uv venv --python 3.12 $environmentPath
    if ($LASTEXITCODE -ne 0) { throw 'Unable to create the CoACD environment.' }
}
uv pip install --python (Join-Path $environmentPath 'Scripts/python.exe') -r (Join-Path $PSScriptRoot 'requirements.txt')
if ($LASTEXITCODE -ne 0) { throw 'Unable to install CoACD.' }
