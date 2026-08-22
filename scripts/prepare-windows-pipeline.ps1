[CmdletBinding()]
param(
  [string]$LockFile = (Join-Path $PSScriptRoot '..\pipeline\models.lock.json'),
  [string]$ModelsDirectory = (Join-Path $PSScriptRoot '..\pipeline\models'),
  [switch]$VerifyOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$lock = Get-Content -Raw -LiteralPath $LockFile | ConvertFrom-Json
if (-not $lock.models -or $lock.models.Count -eq 0) {
  throw "No models are defined in $LockFile"
}

if (-not $VerifyOnly) {
  New-Item -ItemType Directory -Force -Path $ModelsDirectory | Out-Null
}

foreach ($model in $lock.models) {
  $target = Join-Path $ModelsDirectory $model.runtime_filename
  if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
    if ($VerifyOnly) {
      throw "Missing locked model file: $target"
    }
    $temporary = "$target.download"
    try {
      Invoke-WebRequest -Uri $model.download_url -OutFile $temporary
      Move-Item -Force -LiteralPath $temporary -Destination $target
    }
    finally {
      if (Test-Path -LiteralPath $temporary) {
        Remove-Item -Force -LiteralPath $temporary
      }
    }
  }

  $file = Get-Item -LiteralPath $target
  if ($file.Length -ne [long]$model.bytes) {
    throw "Byte-size mismatch for $($model.runtime_filename): expected $($model.bytes), got $($file.Length)"
  }
  $stream = [System.IO.File]::OpenRead($target)
  $md5 = [System.Security.Cryptography.MD5]::Create()
  try {
    $actualMd5 = (($md5.ComputeHash($stream) | ForEach-Object { $_.ToString('x2') }) -join '')
  }
  finally {
    $md5.Dispose()
    $stream.Dispose()
  }
  if ($actualMd5 -ne ([string]$model.md5).ToLowerInvariant()) {
    throw "MD5 mismatch for $($model.runtime_filename): expected $($model.md5), got $actualMd5"
  }
}

$modelLabel = if ($lock.models.Count -eq 1) { 'file' } else { 'files' }
Write-Output "Verified $($lock.models.Count) locked model $modelLabel."
if ($VerifyOnly) {
  return
}

$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$pipelineRoot = Join-Path $projectRoot 'pipeline'
$virtualEnvironment = Join-Path $pipelineRoot '.venv'
$python = Join-Path $virtualEnvironment 'Scripts\python.exe'
if (-not (Test-Path -LiteralPath $python -PathType Leaf)) {
  & py -3.11 -m venv $virtualEnvironment
  if ($LASTEXITCODE -ne 0) { throw "Python 3.11 virtual-environment creation failed with exit code $LASTEXITCODE" }
}

& $python -m pip install --requirement (Join-Path $pipelineRoot 'requirements-models.txt') 'pyinstaller==6.15.0'
if ($LASTEXITCODE -ne 0) { throw "Pipeline dependency installation failed with exit code $LASTEXITCODE" }

$runtimeDirectory = Join-Path $pipelineRoot 'runtime'
$workDirectory = Join-Path $pipelineRoot '.build'
& $python -m PyInstaller --noconfirm --clean --onedir `
  --name pixelfit-pipeline `
  --paths (Join-Path $pipelineRoot 'src') `
  --hidden-import rembg `
  --hidden-import onnxruntime `
  --hidden-import backports `
  --hidden-import backports.tarfile `
  --hidden-import setuptools._vendor.backports `
  --hidden-import setuptools._vendor.backports.tarfile `
  --copy-metadata pymatting `
  --distpath $runtimeDirectory `
  --workpath $workDirectory `
  --specpath $workDirectory `
  (Join-Path $pipelineRoot 'entry.py')
if ($LASTEXITCODE -ne 0) { throw "PyInstaller failed with exit code $LASTEXITCODE" }

$runtime = Join-Path $runtimeDirectory 'pixelfit-pipeline\pixelfit-pipeline.exe'
'{"command":"ping"}' | & $runtime
if ($LASTEXITCODE -ne 0) { throw "Packaged pipeline ping failed with exit code $LASTEXITCODE" }
