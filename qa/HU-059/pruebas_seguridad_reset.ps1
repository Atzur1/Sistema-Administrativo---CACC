<#
.SYNOPSIS
    HU-059 (code review): pruebas de seguridad de reset_base.ps1. Ninguna ejecuta un DROP sobre una base real.

.DESCRIPTION
    A. Validación pura (seguridad_base.ps1, sin SQL): nombres maliciosos, protegidos o mal formados, servidores no locales y
       entornos que no son Development/Testing se rechazan; los nombres de prueba válidos se aceptan.
    B. reset_base.ps1 de punta a punta con nombres maliciosos: corta antes de cualquier SQL (no llega a crear su log) y las
       bases reales siguen con la misma fecha de creación (consulta de solo lectura).
    C. La marca: una base descartable de este script (CACC_ResetSeguridad_Prueba), creada SIN marca, no se borra; con
       -AdoptarBaseSinMarca se recrea y queda marcada; la corrida siguiente ya no necesita el switch. Al final se borra.
       Es la única base que esta prueba crea o borra.

    Códigos de salida: 0 todo PASS; 1 algún FAIL.
#>
#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$Servidor = "(localdb)\MSSQLLocalDB",
    [string]$AdministrativoDll = ""
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (-not $AdministrativoDll) { $AdministrativoDll = Join-Path $repo "ApiGestion\bin\Debug\net9.0\ApiGestion.dll" }
. (Join-Path $PSScriptRoot "seguridad_base.ps1")

$resultados = New-Object Collections.Generic.List[object]
function Verificar([string]$Id, [string]$Caso, [bool]$Ok, [string]$Obtenido) {
    $resultados.Add([pscustomobject]@{ Id = $Id; Caso = $Caso; Estado = $(if ($Ok) { "PASS" } else { "FAIL" }); Obtenido = $Obtenido })
    Write-Host ("{0,-5} {1,-6} {2}  [{3}]" -f $(if ($Ok) { "PASS" } else { "FAIL" }), $Id, $Caso, $Obtenido) -ForegroundColor $(if ($Ok) { "Green" } else { "Red" })
}
function Rechaza([scriptblock]$Accion) { try { & $Accion; $null } catch { $_.Exception.Message } }
function Sql([string]$Base, [string]$Consulta) {
    $t = & sqlcmd -S $Servidor -E -C -b -I -W -h -1 -d $Base -Q "SET NOCOUNT ON; $Consulta" 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "SQL: $t" }
    $t.Trim()
}

# ---------------------------------------------------------------- A. validación pura (sin SQL)
$local = "(localdb)\MSSQLLocalDB"
$malos = [ordered]@{
    "A1"  = @($local, "ClubCamionerosPRUEBA", "la base A de origen (termina en PRUEBA)")
    "A2"  = @($local, "x]; DROP DATABASE [ClubCamionerosPRUEBA]; --_Prueba", "inyección: cierra el corchete y agrega un DROP")
    "A3"  = @($local, "ClubCamionerosPRUEBA'; DROP DATABASE CACC_B_Origen; --_E2E", "inyección con comilla")
    "A4"  = @($local, "CACC_B_Origen", "base B de origen")
    "A5"  = @($local, "CCACUnificada", "base de trabajo")
    "A6"  = @($local, "master", "base del sistema")
    "A7"  = @($local, "algo_prueba", "sufijo en minúsculas")
    "A8"  = @($local, "Mi Base_Prueba", "con espacio")
    "A9"  = @($local, "1Base_Prueba", "empieza con dígito")
    "A10" = @($local, "Base-HU_Prueba", "con guion")
    "A11" = @($local, "BasePrueba", "sin '_' antes del sufijo")
    "A12" = @("sql.club.com.ar", "Cualquiera_Prueba", "servidor remoto")
    "A13" = @("10.0.0.5\SQLEXPRESS", "Cualquiera_Prueba", "servidor por IP de red")
    "A14" = @("(localdb)\MSSQLLocalDB;Database=ClubCamionerosPRUEBA", "Cualquiera_Prueba", "servidor con texto de cadena de conexión")
}
foreach ($id in $malos.Keys) {
    $s, $b, $que = $malos[$id]
    $msg = Rechaza { Assert-DestinoDePrueba $s $b }
    Verificar $id "Se rechaza: $que" ($null -ne $msg) $(if ($msg) { $msg.Substring(0, [Math]::Min(80, $msg.Length)) } else { "ACEPTADO" })
}
foreach ($par in @(@("A15", "Production"), @("A16", "Staging"))) {
    $anterior = $env:ASPNETCORE_ENVIRONMENT
    try { $env:ASPNETCORE_ENVIRONMENT = $par[1]; $msg = Rechaza { Assert-DestinoDePrueba $local "ClubCamionerosUnificadaV2_HU059_Prueba" } }
    finally { $env:ASPNETCORE_ENVIRONMENT = $anterior }
    Verificar $par[0] "Se rechaza con ASPNETCORE_ENVIRONMENT=$($par[1])" ($null -ne $msg) $(if ($msg) { "rechazado" } else { "ACEPTADO" })
}
$buenos = @(@("A17", $local, "ClubCamionerosUnificadaV2_HU059_Prueba"), @("A18", "localhost", "CACC_HU056_E2E"), @("A19", ".\SQLEXPRESS,1433", "Otra_Base_Prueba"))
foreach ($caso in $buenos) {
    $msg = Rechaza { Assert-DestinoDePrueba $caso[1] $caso[2] }
    Verificar $caso[0] "Se acepta: $($caso[2]) en $($caso[1])" ($null -eq $msg) $(if ($msg) { $msg } else { "aceptado" })
}
$anterior = $env:CACC_E2E_SERVIDORES_PERMITIDOS
try {
    $env:CACC_E2E_SERVIDORES_PERMITIDOS = "sql-pruebas.club.local"
    $ok = $null -eq (Rechaza { Assert-DestinoDePrueba "sql-pruebas.club.local" "Compartida_Prueba" })
    $otro = $null -ne (Rechaza { Assert-DestinoDePrueba "SQL-PRUEBAS.club.local.evil" "Compartida_Prueba" })
} finally { $env:CACC_E2E_SERVIDORES_PERMITIDOS = $anterior }
Verificar "A20" "Un servidor compartido solo con coincidencia exacta en CACC_E2E_SERVIDORES_PERMITIDOS" ($ok -and $otro) "exacto: $ok; parecido rechazado: $otro"

# Marca, con una consulta falsa (sin SQL).
$sinMarca = Rechaza { Assert-MarcaDePrueba "X_Prueba" { param($b, $c) if ($b -eq "master") { "1" } else { "" } } }
$conMarca = Assert-MarcaDePrueba "X_Prueba" { param($b, $c) if ($b -eq "master") { "1" } else { "reset_base" } }
$nueva = Assert-MarcaDePrueba "X_Prueba" { param($b, $c) "0" }
Verificar "A21" "Marca: existente sin marca se rechaza; con marca se acepta; inexistente es nueva" ($sinMarca -and $conMarca -eq "marcada" -and $nueva -eq "nueva") "sin marca: $([bool]$sinMarca); $conMarca; $nueva"

# ---------------------------------------------------------------- B. reset_base.ps1 de punta a punta con nombres maliciosos
$reales = @("ClubCamionerosPRUEBA", "CACC_B_Origen", "CCACUnificada")
function Huella { Sql "master" "SELECT STRING_AGG(CONCAT(name, '@', CONVERT(VARCHAR(23), create_date, 126)), ',') WITHIN GROUP (ORDER BY name) FROM sys.databases WHERE name IN ('$($reales -join "','")')" }
$antes = Huella
$i = 0
foreach ($b in @("ClubCamionerosPRUEBA", "x]; DROP DATABASE [ClubCamionerosPRUEBA]; --_Prueba", "CACC_B_Origen")) {
    $i++
    $salida = Join-Path $env:TEMP ("cacc-reset-seguridad-" + [guid]::NewGuid().ToString("N"))
    $msg = Rechaza { & (Join-Path $PSScriptRoot "reset_base.ps1") -Servidor $Servidor -Base $b -AdministrativoDll $AdministrativoDll -Salida $salida }
    $sinSql = -not (Test-Path (Join-Path $salida "reset_base.log"))
    Verificar "B$i" "reset_base.ps1 -Base '$b': falla antes de cualquier SQL" ($null -ne $msg -and $sinSql) "rechazado: $($null -ne $msg); sin log: $sinSql"
    if (Test-Path $salida) { Remove-Item -Recurse -Force $salida }
}
$despues = Huella
Verificar "B4" "Las bases reales siguen intactas (misma fecha de creación)" ($antes -eq $despues -and $antes) $despues

# ---------------------------------------------------------------- C. la marca, sobre una base descartable propia
$descartable = "CACC_ResetSeguridad_Prueba"
$salida = Join-Path $env:TEMP "cacc-reset-seguridad"
try {
    Sql "master" "IF DB_ID(N'$descartable') IS NOT NULL BEGIN ALTER DATABASE [$descartable] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$descartable]; END; CREATE DATABASE [$descartable];" | Out-Null
    Sql $descartable "CREATE TABLE dbo.DatoQueNoSePierde (id INT); INSERT dbo.DatoQueNoSePierde VALUES (42);" | Out-Null
    $creada = Sql "master" "SELECT CONVERT(VARCHAR(23), create_date, 126) FROM sys.databases WHERE name = N'$descartable'"
    $msg = Rechaza { & (Join-Path $PSScriptRoot "reset_base.ps1") -Servidor $Servidor -Base $descartable -AdministrativoDll $AdministrativoDll -Salida $salida }
    $sigue = Sql $descartable "SELECT CONCAT(CONVERT(VARCHAR(23), (SELECT create_date FROM sys.databases WHERE name = DB_NAME()), 126), '|', (SELECT id FROM dbo.DatoQueNoSePierde))"
    Verificar "C1" "Una base con nombre de prueba que NO creó reset_base.ps1 no se borra" ($null -ne $msg -and $sigue -eq "$creada|42") "rechazado: $($null -ne $msg); $sigue"

    & (Join-Path $PSScriptRoot "reset_base.ps1") -Servidor $Servidor -Base $descartable -AdministrativoDll $AdministrativoDll -Salida $salida -AdoptarBaseSinMarca | Out-Null
    $marca = Sql $descartable "SELECT CONVERT(NVARCHAR(50), value) FROM sys.extended_properties WHERE class = 0 AND name = N'CACC_BaseDePrueba'"
    Verificar "C2" "Con -AdoptarBaseSinMarca se recrea y queda marcada" ($marca -eq "reset_base") "marca: $marca"

    & (Join-Path $PSScriptRoot "reset_base.ps1") -Servidor $Servidor -Base $descartable -AdministrativoDll $AdministrativoDll -Salida $salida | Out-Null
    Verificar "C3" "La corrida siguiente recrea la base marcada sin el switch" $true "ok"
}
catch { Verificar "C-X" "La prueba de la marca se interrumpió" $false $_.Exception.Message }
finally {
    Sql "master" "IF DB_ID(N'$descartable') IS NOT NULL BEGIN ALTER DATABASE [$descartable] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$descartable]; END" | Out-Null
}

$pass = @($resultados | Where-Object Estado -eq "PASS").Count
$fail = @($resultados | Where-Object Estado -eq "FAIL").Count
Write-Host "`nTotal: $pass PASS | $fail FAIL"
if ($fail -gt 0) { exit 1 }
exit 0
