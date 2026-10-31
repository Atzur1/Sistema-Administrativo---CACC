<#
.SYNOPSIS
    Base descartable para el QA de HU-107: esquema unificado (HU-053) + migraciones con el migrador de la API + cuentas y
    fixtures ficticios de HU-059 (los usa dotnet test) + cobros de qa/HU-107/carga_hu107.sql + dos cuentas propias del QA.
    Borra y recrea la base: solo nombres *_Prueba y solo LocalDB.

.DESCRIPTION
    Las cuentas del QA (SuperAdmin 95900001, Administrador 95900002) se crean con el hash BCrypt que se pase en
    -HashClave: la clave la genera qa_hu107.ps1 en cada corrida y no se guarda en ningún archivo.
    Los datos de HU-059 se leen del commit que los agregó (653cb77), porque esa carpeta no está en main.
#>
#Requires -Version 7.0
param(
    [Parameter(Mandatory)] [string]$HashClave,
    [string]$Servidor = "(localdb)\MSSQLLocalDB",
    [string]$Base = "ClubCamionerosUnificadaV2_HU107_Prueba",
    [string]$AdministrativoDll = (Join-Path $PSScriptRoot "..\..\ApiGestion\bin\Debug\net9.0\ApiGestion.dll"),
    [string]$CommitHu059 = "653cb77"
)
$ErrorActionPreference = "Stop"
if ($Base -notmatch "^[A-Za-z0-9_]+_Prueba$") { throw "Solo bases de prueba (*_Prueba)." }
if ($Servidor -notmatch "^\(localdb\)\\") { throw "Solo LocalDB." }
if ($HashClave -notmatch '^\$2[aby]\$\d\d\$[./A-Za-z0-9]{53}$') { throw "HashClave no es un hash BCrypt." }
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
# Carpeta temporal sin guiones dobles en la ruta (sqlcmd lee "--U..." de una ruta como si fuera la opción -U).
$tmp = Join-Path $env:TEMP "cacc-hu107"; New-Item -ItemType Directory -Force $tmp | Out-Null
function EjecutarSql([string]$Db, [string[]]$Extra) {
    $t = & sqlcmd -S $Servidor -E -C -b -I -f 65001 -d $Db @Extra 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd: $t" }
    $t
}

EjecutarSql "master" @("-Q", "IF DB_ID(N'$Base') IS NOT NULL BEGIN ALTER DATABASE [$Base] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Base]; END") | Out-Null
$esquema = Join-Path $tmp "esquema.sql"
[IO.File]::WriteAllText($esquema, ([IO.File]::ReadAllText((Join-Path $repo "database\esquema\ClubCamioneros_unificada_crear_base.sql"))).Replace("ClubCamionerosUnificadaV2", $Base), [Text.UTF8Encoding]::new($false))
EjecutarSql "master" @("-i", $esquema) | Out-Null
Write-Host "Esquema creado: $Base"

# Migraciones con el migrador de la propia API (mismas reglas y hashes que en producción).
$info = [Diagnostics.ProcessStartInfo]::new("dotnet", "`"$AdministrativoDll`" --DatabaseMigrations:SoloMigrar=true")
$info.WorkingDirectory = Split-Path $AdministrativoDll; $info.UseShellExecute = $false; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
$info.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Development"
$info.EnvironmentVariables["ConnectionStrings__ConexionSQL"] = "Server=$Servidor;Database=$Base;Trusted_Connection=True;TrustServerCertificate=True;"
$info.EnvironmentVariables["Jwt__Key"] = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
$info.EnvironmentVariables["Jwt__Issuer"] = "migrador"; $info.EnvironmentVariables["Jwt__Audience"] = "migrador"
$info.EnvironmentVariables["Integracion__ClienteDeportivo__Clave"] = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$p = [Diagnostics.Process]::Start($info); $salida = $p.StandardOutput.ReadToEnd() + $p.StandardError.ReadToEnd(); $p.WaitForExit()
if ($p.ExitCode -ne 0) { throw "Migrador: $salida" }
Write-Host ($salida -split "`n" | Where-Object { $_ -match "Migraci" } | Select-Object -Last 1)

# Cuentas y fixtures de HU-059 (los necesitan las pruebas de integración de dotnet test).
foreach ($nombre in "01_cuentas_prueba.sql", "02_fixtures_e2e.sql") {
    $destino = Join-Path $tmp $nombre
    $contenido = & git -C $repo show "${CommitHu059}:qa/HU-059/datos/$nombre"
    if ($LASTEXITCODE -ne 0) { throw "No se pudo leer $nombre de $CommitHu059." }
    [IO.File]::WriteAllText($destino, ($contenido -join "`n"), [Text.UTF8Encoding]::new($false))
    Write-Host "HU-059 $nombre`: $(((EjecutarSql $Base @('-W', '-h', '-1', '-i', $destino)).Trim() -split "`n" | Select-Object -Last 1))"
}

# Cuentas propias del QA de HU-107.
$cuentas = Join-Path $tmp "cuentas_hu107.sql"
[IO.File]::WriteAllText($cuentas, @"
SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON;
INSERT PERSONAS(nombre, apellido, dni) VALUES (N'Super', N'QA HU107', '95900001');
INSERT USUARIOS(FK_id_persona, FK_id_rol, email, contrasenia, activo, acceso_portal, rol_portal, activacion_pendiente)
VALUES (SCOPE_IDENTITY(), 1, 'superadmin.hu107@ejemplo.com', '`$(HASH)', 1, 1, 1, 0);
INSERT PERSONAS(nombre, apellido, dni) VALUES (N'Admin', N'QA HU107', '95900002');
INSERT USUARIOS(FK_id_persona, FK_id_rol, email, contrasenia, activo, acceso_portal, rol_portal, activacion_pendiente)
VALUES (SCOPE_IDENTITY(), 2, 'admin.hu107@ejemplo.com', '`$(HASH)', 1, 1, 2, 0);
SELECT 'cuentas QA HU-107 creadas';
"@, [Text.UTF8Encoding]::new($false))
Write-Host ((EjecutarSql $Base @("-W", "-h", "-1", "-v", "HASH=$HashClave", "-i", $cuentas)).Trim())

$carga = Join-Path $tmp "carga_hu107.sql"
Copy-Item (Join-Path $PSScriptRoot "carga_hu107.sql") $carga -Force
Write-Host ((EjecutarSql $Base @("-W", "-h", "-1", "-i", $carga)).Trim() -split "`n" | Select-Object -Last 1)
