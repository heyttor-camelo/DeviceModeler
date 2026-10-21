<#
.SYNOPSIS
  Compila o plugin e instala o bundle de autoload do HYDRO para o usuário atual.
.DESCRIPTION
  Copia deploy\HYDRO.bundle\PackageContents.xml e o conteúdo de NETLOAD\ (Hydro.Civil3D.dll + catalog\)
  para %APPDATA%\Autodesk\ApplicationPlugins\HYDRO.bundle\. Apaga antes o bundle do nome antigo
  (DeviceModeler.bundle), se existir. Com o Civil 3D aberto, para antes de mexer em qualquer coisa
  (a DLL carregada fica bloqueada). Use -Remove para desinstalar.
#>
param(
    [switch]$Remove,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$plugins = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'
$target = Join-Path $plugins 'HYDRO.bundle'
# Nome antigo do plugin (DeviceModeler, até out/2026): sai sempre, senão os dois carregariam juntos.
$legacy = Join-Path $plugins 'DeviceModeler.bundle'

# Civil 3D (e AutoCAD) rodam como acad.exe: com ele aberto a DLL está bloqueada e a cópia falharia no meio.
$acad = @(Get-Process -Name 'acad' -ErrorAction SilentlyContinue)
if ($acad.Count -gt 0) {
    Write-Host "O Civil 3D está aberto ($($acad.Count) processo(s) acad.exe). Feche-o e rode o script de novo." -ForegroundColor Yellow
    Write-Host 'Nada foi alterado.'
    exit 1
}

if (-not $Remove -and -not $NoBuild) {
    dotnet build (Join-Path $root 'src\Hydro.Civil3D\Hydro.Civil3D.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'falha na compilação' }
}

if (Test-Path $legacy) {
    Remove-Item -Recurse -Force $legacy
    Write-Host "Bundle antigo removido: $legacy"
}

if ($Remove) {
    if (Test-Path $target) { Remove-Item -Recurse -Force $target }
    Write-Host "HYDRO removido de $target"
    return
}

$contents = Join-Path $target 'Contents'
# O conteúdo antigo sai antes: DLLs avulsas de versões anteriores à DLL única quebrariam o plugin.
if (Test-Path $contents) { Remove-Item -Recurse -Force $contents }
New-Item -ItemType Directory -Force $contents | Out-Null
Copy-Item (Join-Path $root 'deploy\HYDRO.bundle\PackageContents.xml') $target -Force
Copy-Item (Join-Path $root 'NETLOAD\*') $contents -Recurse -Force
Write-Host "HYDRO instalado em $target (carrega ao abrir o Civil 3D 2026)."
