<#
.SYNOPSIS
  Gera dist\HYDRO.bundle (versionado no git) para instalar em outra máquina sem compilar.
.DESCRIPTION
  Compila em Release contra refs\acad2026 quando existir (DLLs .NET 8 do Civil 3D 2026 25.1.60):
  o resultado net8.0 carrega tanto nos builds .NET 8 quanto nos .NET 10 do Civil 3D 2026.
  Sem refs, usa a instalação local. Para instalar a pasta gerada: copie dist\HYDRO.bundle
  para %APPDATA%\Autodesk\ApplicationPlugins\ (ou rode tools\Install-Bundle.ps1 -NoBuild).
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$refs = Join-Path $root 'refs\acad2026'
$project = Join-Path $root 'src\Hydro.Civil3D\Hydro.Civil3D.csproj'

$buildArgs = @('build', $project, '-c', 'Release')
if (Test-Path (Join-Path $refs 'acdbmgd.dll')) { $buildArgs += "-p:AcadDir=$refs" }
dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw 'falha na compilação' }

$bundle = Join-Path $root 'dist\HYDRO.bundle'
if (Test-Path $bundle) { Remove-Item -Recurse -Force $bundle }
$contents = Join-Path $bundle 'Contents'
New-Item -ItemType Directory -Force $contents | Out-Null
Copy-Item (Join-Path $root 'deploy\HYDRO.bundle\PackageContents.xml') $bundle
Copy-Item (Join-Path $root 'NETLOAD\*') $contents -Recurse -Exclude '*.pdb'
Copy-Item (Join-Path $root 'deploy\THIRD-PARTY-NOTICES.txt') $contents
Write-Host "Bundle gerado em $bundle"
