<#
.SYNOPSIS
    Base descartable para el QA de HU-106: esquema unificado (HU-053) + migraciones con el migrador de la API + cuentas y
    fixtures ficticios de HU-059 + volumen de club (carga_hu106.sql). Borra y recrea la base: solo nombres *_Prueba.
#>
#Requires -Version 7.0
param(
    [string]$Servidor = "(localdb)\MSSQLLocalDB",
    [string]$Base = "ClubCamionerosUnificadaV2_HU106_Prueba",
    [string]$AdministrativoDll = (Join-Path $PSScriptRoot "..\..\ApiGestion\bin\Debug\net9.0\ApiGestion.dll"),
    [string]$CuentasSql = (Join-Path $PSScriptRoot "..\HU-059\datos\01_cuentas_prueba.sql"),
    [string]$FixturesSql = (Join-Path $PSScriptRoot "..\HU-059\datos\02_fixtures_e2e.sql")
)
$ErrorActionPreference = "Stop"
if ($Base -notmatch "^[A-Za-z0-9_]+_Prueba$") { throw "Solo bases de prueba (*_Prueba)." }
if ($Servidor -notmatch "^\(localdb\)\\") { throw "Solo LocalDB." }
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$tmp = Join-Path $env:TEMP "cacc-hu106"; New-Item -ItemType Directory -Force $tmp | Out-Null
function EjecutarSql([string]$Db, [string[]]$Extra) {
    $t = & sqlcmd -S $Servidor -E -C -b -I -f 65001 -d $Db @Extra 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd: $t" }
    $t
}

EjecutarSql "master" @("-Q", "IF DB_ID(N'$Base') IS NOT NULL BEGIN ALTER DATABASE [$Base] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Base]; END") | Out-Null
$esquema = Join-Path $tmp "esquema.sql"
[IO.File]::WriteAllText($esquema, ([IO.File]::ReadAllText((Join-Path $repo "database\esquema\ClubCamioneros_unificada_crear_base.sql"))).Replace("ClubCamionerosUnificadaV2", $Base), [Text.UTF8Encoding]::new($false))
EjecutarSql "master" @("-i", $esquema) | Out-Null
Write-Host "Esquema creado."

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
Write-Host ($salida -split "`n" | Where-Object { $_ -match "Migraci" } | Select-Object -Last 2)

# Copias locales sin guiones dobles en la ruta (sqlcmd lee "--U..." de una ruta como si fuera la opción -U).
foreach ($archivo in $CuentasSql, $FixturesSql, (Join-Path $PSScriptRoot "carga_hu106.sql")) {
    $copia = Join-Path $tmp (Split-Path $archivo -Leaf)
    if ((Resolve-Path $archivo).Path -ne $copia) { Copy-Item $archivo $copia -Force }
    Write-Host ((EjecutarSql $Base @("-W", "-h", "-1", "-i", $copia)).Trim() -split "`n" | Select-Object -Last 1)
}
