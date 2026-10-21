<#
.SYNOPSIS
  Compila o plugin e instala o bundle de autoload do DeviceModeler para o usuário atual.
.DESCRIPTION
  Copia deploy\DeviceModeler.bundle\PackageContents.xml e o conteúdo de NETLOAD\ (Modeler.Civil3D.dll + catalog\)
  para %APPDATA%\Autodesk\ApplicationPlugins\DeviceModeler.bundle\. Feche o Civil 3D antes
  (a DLL carregada fica bloqueada). Use -Remove para desinstalar.
#>
param(
    [switch]$Remove,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\DeviceModeler.bundle'

if ($Remove) {
    if (Test-Path $target) { Remove-Item -Recurse -Force $target }
    Write-Host "DeviceModeler removido de $target"
    return
}

if (-not $NoBuild) {
    dotnet build (Join-Path $root 'src\Modeler.Civil3D\Modeler.Civil3D.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'falha na compilação' }
}

$contents = Join-Path $target 'Contents'
# O conteúdo antigo sai antes: DLLs avulsas de versões anteriores à DLL única quebrariam o plugin.
if (Test-Path $contents) { Remove-Item -Recurse -Force $contents }
New-Item -ItemType Directory -Force $contents | Out-Null
Copy-Item (Join-Path $root 'deploy\DeviceModeler.bundle\PackageContents.xml') $target -Force
Copy-Item (Join-Path $root 'NETLOAD\*') $contents -Recurse -Force
Write-Host "DeviceModeler instalado em $target (carrega ao abrir o Civil 3D 2026)."
