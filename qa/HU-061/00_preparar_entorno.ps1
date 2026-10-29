# HU-061 - Prepara (o vuelve a dejar en cero) la base descartable CCACUnificada_QA_HU061.
# Borra SOLO esa base, la crea con el esquema unificado del repositorio y carga los datos ficticios.
# Uso (desde la raiz del repo):  powershell -ExecutionPolicy Bypass -File qa\HU-061\00_preparar_entorno.ps1
param([string]$Servidor = '(localdb)\MSSQLLocalDB')

$ErrorActionPreference = 'Stop'
$base = 'CCACUnificada_QA_HU061'
$raiz = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$esquema = Join-Path $raiz 'database\esquema\ClubCamioneros_unificada_crear_base.sql'
$temporal = Join-Path $env:TEMP "hu061_crear_base.sql"

sqlcmd -S $Servidor -E -b -Q "IF DB_ID(N'$base') IS NOT NULL BEGIN ALTER DATABASE [$base] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$base]; END"
if ($LASTEXITCODE -ne 0) { throw 'No se pudo borrar la base anterior.' }

(Get-Content $esquema -Raw -Encoding UTF8).Replace('ClubCamionerosUnificadaV2', $base) | Set-Content $temporal -Encoding UTF8
sqlcmd -S $Servidor -E -b -I -i $temporal
if ($LASTEXITCODE -ne 0) { throw 'Fallo la creacion del esquema.' }

sqlcmd -S $Servidor -d $base -E -b -I -W -i (Join-Path $PSScriptRoot '01_datos_prueba.sql')
if ($LASTEXITCODE -ne 0) { throw 'Fallo la carga de datos de prueba.' }

Write-Host "Listo: $base preparada. Levantar la API con:"
Write-Host "  `$env:ConnectionStrings__ConexionSQL='Server=$Servidor;Database=$base;Trusted_Connection=True;TrustServerCertificate=True;'"
Write-Host "  `$env:Email__LogResetLink='true'; `$env:GeneracionCuotas__Habilitada='false'"
Write-Host "  dotnet run --project ApiGestion --launch-profile http > qa\HU-061\api.log"
