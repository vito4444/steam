[CmdletBinding()]
param(
  [string]$OutputDirectory = (Join-Path $PSScriptRoot 'reassembled')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$artifacts = @(
  [pscustomobject]@{
    Name = 'PixelFit-Setup-0.4.0-x64.exe'
    Bytes = 526757255
    Sha256 = 'f32062be8340aa6923012dcc38f3ae286ac6b49341bf75dad763bcfce2043b4b'
    Parts = @(
      '044ebaa3e1df2a19d88885204f6c843b20680209229dc438b2f17e5c249083a3',
      '5d4bc8ebdfca74356a4803808e9347b49cf20279ea9045702a971e804a1721f8',
      '590d8cfd1db871bb606cd7605eef678922bed6ee5e687279115fcde67266826c',
      'e30b04c6ba41744a2217c7d5f989c2d66c86c9ca5342fbb0d6be6e228b062f0a',
      '45b35da8429ed64d727671370e015d7c30ddb684a45829aa9cedcd181087d273',
      '8e9751a9619be9e4966a981674fd1b784874c4f98d210533e50c81055be0dbbf',
      '12ac7d909fab8677e7560c2041c83ae345754daf82df6008d198a264fbcaa426',
      '84453f6c87d8bcdc5efac247e932230007e059c290647fcf58b531aabdaa2863',
      'ecb1833e629088462a5023356cf862c8e39493e7fa9734b790a38581aa59ed0b',
      '12b801c7db83612414eed25fdf9b01a4994458958c1edd034cb11a7acf257a4c',
      '49d7b544a9ad725be073c267314c5fdc6e960c9e338c8d64433ded3f7ad7a10f',
      'db09af915d7964620d2b03fa95167a4ff86cbb3605e37c89459d7eb635f00d57'
    )
  },
  [pscustomobject]@{
    Name = 'PixelFit-Portable-0.4.0-x64.exe'
    Bytes = 526529608
    Sha256 = '67e72ad1ff755c2c9841399fdd8767b7b39444ade8d77d88d7866475eb7912ae'
    Parts = @(
      '584f47c0189f08169c6b29e0e3a0cf39617d06f83be2b7d396d33e0a96c7d52e',
      '887e107e48aee6a54a8cb252b0d8a3e8fb7cd0a72e9fbc9aa7e9b9250d37fae5',
      'b08dcc1c7e822663a1b34c4486dc592ab043512a8faaae56a0e37a3e1b894101',
      '78e368d5ba6a14c8899dee22898948607c6ba91e3ca5b37b0b5ea70bcba7b1ed',
      '8181e4635ba953e2e6add52260178fdd725a5c2d87ad149a7210666d43f82828',
      'f435911da2814435123a3187606746093b81a0e21d3a434f6c653b3cc1cc89cf',
      '6a0567d5b28ff710cb1b5b31801a232d14dbe414b0bff788bbb2760ef190758e',
      'ecdd965c4a1925623ee4232292ff81fbe2b271ba6f1abb0200aae0b31796f821',
      '90fc16e50a6948772f620971033c15480a9eaade3bfb33479c20c575b425b043',
      'eb7ec2745a7e134b802110e0f9cac540e67ca4174ff49dcd4b61d345decf8828',
      '5e2aa65c2d54b86b307c8e65d482cf2cb57229f8e450c78af669dc5cded092c3',
      'a38e72765750ae605cbcce26ba4c247b85ce765fc94d59373ca095e2ba7b9d63'
    )
  }
)

$sourceDirectory = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
if ($resolvedOutput -eq $sourceDirectory) { throw 'OutputDirectory must not be the part directory.' }
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null

foreach ($artifact in $artifacts) {
  $expectedNames = 1..$artifact.Parts.Count | ForEach-Object { '{0}.part{1:D3}' -f $artifact.Name, $_ }
  $actualParts = @(Get-ChildItem -LiteralPath $sourceDirectory -File -Filter "$($artifact.Name).part*" | Sort-Object Name)
  if ($actualParts.Count -ne $expectedNames.Count) {
    throw "$($artifact.Name): expected exactly $($expectedNames.Count) numbered parts."
  }

  for ($index = 0; $index -lt $expectedNames.Count; $index++) {
    if ($actualParts[$index].Name -cne $expectedNames[$index]) {
      throw "$($artifact.Name): expected exactly $($expectedNames.Count) numbered parts."
    }
  }

  for ($index = 0; $index -lt $actualParts.Count; $index++) {
    $actualHash = (Get-FileHash -LiteralPath $actualParts[$index].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $artifact.Parts[$index]) { throw "$($actualParts[$index].Name): SHA-256 mismatch." }
  }

  $destination = Join-Path $resolvedOutput $artifact.Name
  if (Test-Path -LiteralPath $destination) {
    $existing = Get-Item -LiteralPath $destination
    $existingHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($existing.Length -eq $artifact.Bytes -and $existingHash -eq $artifact.Sha256) {
      Write-Host "Verified existing $($artifact.Name): $($existing.Length) bytes, $existingHash"
      continue
    }
    throw "$destination already exists but does not match the documented size/SHA-256; refusing to replace it."
  }

  $output = $null
  $createdHere = $false
  try {
    $output = [IO.File]::Open($destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $createdHere = $true
    try {
      foreach ($part in $actualParts) {
        $input = [IO.File]::OpenRead($part.FullName)
        try { $input.CopyTo($output) } finally { $input.Dispose() }
      }
    } finally {
      $output.Dispose()
      $output = $null
    }

    $result = Get-Item -LiteralPath $destination
    $actualHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($result.Length -ne $artifact.Bytes -or $actualHash -ne $artifact.Sha256) {
      throw "$($artifact.Name): reassembled file did not match the documented size/SHA-256."
    }
    Write-Host "Verified $($artifact.Name): $($result.Length) bytes, $actualHash"
  } catch {
    if ($null -ne $output) { $output.Dispose() }
    if ($createdHere -and (Test-Path -LiteralPath $destination)) {
      Remove-Item -LiteralPath $destination -Force
    }
    throw
  }
}
