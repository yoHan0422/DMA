# Developer-only release helper. Never uploads DMA source to public GitHub.
# Builds a disposable extracted copy, validates the existing v0.2.32 smoke test,
# then packages only the publish/ contents for GitHub Releases.
param(
  [Parameter(Mandatory=$true)][string]$SourceZip,
  [switch]$Upload
)
$ErrorActionPreference='Stop'
$repo='yoHan0422/DMA'
$version='0.2.32'
$tag='v'+$version
$here=Split-Path -Parent $MyInvocation.MyCommand.Path
$dist=Join-Path (Split-Path -Parent $here) 'dist'
$zipOutput=Join-Path $dist ('DMA_v'+$version+'.zip')
$shaOutput=$zipOutput+'.sha256'
$source=(Resolve-Path -LiteralPath $SourceZip).Path
$scratch=Join-Path $env:TEMP ('DMA_Release_'+[guid]::NewGuid().ToString('N'))
try {
  New-Item -ItemType Directory -Force -Path $scratch,$dist | Out-Null
  Write-Host '[DMA] Extracting source into a disposable workspace...'
  Expand-Archive -LiteralPath $source -DestinationPath $scratch -Force
  $project=Join-Path $scratch 'DMA\DMA.csproj'
  if (!(Test-Path -LiteralPath $project)) {throw 'The ZIP is not the v0.2.32 DMA source ZIP.'}
  $csproj=Get-Content -LiteralPath $project -Raw
  if ($csproj -notmatch '<Version>0\.2\.32</Version>') {throw 'Source version differs from v0.2.32.'}
  $buildScript=Join-Path $scratch 'tools\build.ps1'
  if (!(Test-Path -LiteralPath $buildScript)) {throw 'tools/build.ps1 not found in source ZIP.'}
  Write-Host '[DMA] Building disposable v0.2.32 copy; original source ZIP is never changed...'
  $argsList=@('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$buildScript+'"'),'-SkipAI')
  $build=Start-Process -FilePath 'powershell.exe' -ArgumentList $argsList -WorkingDirectory $scratch -PassThru -Wait
  if ($build.ExitCode -ne 0) {
    $report=Join-Path $scratch 'build_report.json'
    if (Test-Path $report) {Write-Host (Get-Content -Raw $report)}
    throw ('DMA source failed build/self-test; code '+$build.ExitCode+'. Nothing was published.')
  }
  $published=Join-Path $scratch 'publish'
  $exe=Join-Path $published 'DMA.exe'
  if (!(Test-Path -LiteralPath $exe)) {throw 'DMA.exe not found after build.'}
  if (Test-Path -LiteralPath $zipOutput) {Remove-Item -LiteralPath $zipOutput -Force}
  Compress-Archive -Path (Join-Path $published '*') -DestinationPath $zipOutput -CompressionLevel Optimal
  $sha=(Get-FileHash -LiteralPath $zipOutput -Algorithm SHA256).Hash.ToLowerInvariant()
  [IO.File]::WriteAllText($shaOutput,($sha+'  DMA_v'+$version+'.zip' + [Environment]::NewLine),[Text.Encoding]::ASCII)
  Write-Host ('[DMA] RELEASE PACKAGE: '+$zipOutput)
  Write-Host ('[DMA] SHA256: '+$sha)
  if ($Upload) {
    $gh=Get-Command gh.exe -ErrorAction SilentlyContinue
    if ($null -eq $gh) {throw 'GitHub CLI is not installed. Install gh and run gh auth login, then rerun -Upload. The package is saved in dist/.'}
    & $gh.Source auth status | Out-Host
    if ($LASTEXITCODE -ne 0) {throw 'GitHub CLI authorization is missing. Run gh auth login, then retry -Upload.'}
    # In Windows PowerShell 5.1, a missing release prints native stderr.
    # Treat this expected GitHub 404 as a normal "create release" branch.
    $previousErrorAction=$ErrorActionPreference
    try {
      $ErrorActionPreference='Continue'
      & $gh.Source release view $tag -R $repo 1>$null 2>$null
      $releaseExists=($LASTEXITCODE -eq 0)
    } finally { $ErrorActionPreference=$previousErrorAction }
    if ($releaseExists) {
      & $gh.Source release upload $tag $zipOutput $shaOutput --clobber -R $repo | Out-Host
    } else {
      & $gh.Source release create $tag $zipOutput $shaOutput -R $repo --title ('DMA v'+$version) --notes 'DMA Windows x64 binary release. Contains no C# source.' | Out-Host
    }
    if ($LASTEXITCODE -ne 0) {throw 'GitHub release upload failed. The package is saved in dist/.'}
    Write-Host ('[DMA] PUBLISHED: https://github.com/'+$repo+'/releases/tag/'+$tag)
  } else {
    Write-Host '[DMA] Not uploaded. To publish, rerun with -Upload after gh auth login.'
  }
}
finally {
  if (Test-Path -LiteralPath $scratch) {
    try {Remove-Item -LiteralPath $scratch -Recurse -Force} catch {Write-Warning ('Could not clean staging folder: '+$scratch)}
  }
}
