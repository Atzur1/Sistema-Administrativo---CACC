<#
.SYNOPSIS
    HU-053: arma una base unificada de PRUEBA desde cero e importa A y B. Nunca escribe en las bases de origen.

.DESCRIPTION
    1. (Opcional) Respaldo COPY_ONLY de las bases de origen (no altera su cadena de respaldos).
    2. Huella de las bases de origen (filas y checksum por tabla) para demostrar al final que no cambiaron.
    3. Borra y crea la base destino con esquema/ClubCamioneros_unificada_crear_base.sql (solo si su nombre es de prueba).
    4. Aplica las migraciones de database/migrations/ que el esquema todavia no trae y las registra con el mismo hash
       que calcula la API (ApiGestion.Database.Migracion).
    5. Pasos 01 a 04 de esta carpeta. Si el paso 2 queda BLOQUEADO y se pidio -ConResolucionDePrueba, carga
       resolucion_dni_PRUEBA.sql (decisiones de prueba, no del club) y lo repite.
    6. Vuelve a tomar la huella de los origenes y la compara.

.EXAMPLE
    .\database\unificacion\00_ejecutar_prueba.ps1 -ConResolucionDePrueba -RespaldarOrigenes
#>
[CmdletBinding()]
param(
    [string]$Servidor = "(localdb)\MSSQLLocalDB",
    [string]$BaseA = "ClubCamionerosPRUEBA",
    [string]$BaseB = "CACC_B_Origen",
    [string]$BaseDestino = "ClubCamionerosUnificadaV2_HU053",
    [string]$Salida = (Join-Path $env:TEMP "cacc-hu053"),
    [switch]$ConResolucionDePrueba,
    [switch]$RespaldarOrigenes,
    # Solo crea la base con el esquema y las migraciones (sin importar): para correr las pruebas de DAO "previas".
    [switch]$SoloEsquema
)

$ErrorActionPreference = "Stop"
$carpeta = $PSScriptRoot
$database = Split-Path $carpeta -Parent

# La base destino se borra y se crea: solo se acepta un nombre de prueba, distinto de los origenes.
if ($BaseDestino -notmatch '(?i)(prueba|_hu053)' -or $BaseDestino -in @($BaseA, $BaseB)) {
    throw "La base destino '$BaseDestino' no tiene nombre de prueba (debe contener 'Prueba' o '_HU053') o coincide con un origen."
}

New-Item -ItemType Directory -Force -Path $Salida | Out-Null
$log = Join-Path $Salida ("hu053_{0:yyyyMMdd_HHmmss}.log" -f (Get-Date))
"HU-053 - $(Get-Date -Format s) - servidor $Servidor - A=$BaseA B=$BaseB destino=$BaseDestino" | Set-Content $log

function Invoke-Sql {
    param([string]$Base, [string]$Archivo, [string]$Consulta, [switch]$PermitirFalla)
    $argumentos = @("-S", $Servidor, "-E", "-C", "-b", "-I", "-W", "-s", "|", "-d", $Base,
                    "-v", "BaseA=$BaseA", "BaseB=$BaseB")
    if ($Archivo) { $argumentos += @("-i", $Archivo) } else { $argumentos += @("-Q", $Consulta) }
    $texto = & sqlcmd @argumentos 2>&1 | Out-String
    $codigo = $LASTEXITCODE
    $titulo = if ($Archivo) { Split-Path $Archivo -Leaf } else { "consulta" }
    "`n===== $titulo (base $Base, salida $codigo) =====`n$texto" | Add-Content $log
    Write-Host "== $titulo -> $codigo"
    if ($codigo -ne 0 -and -not $PermitirFalla) { Write-Host $texto; throw "Fallo $titulo. Ver $log" }
    [pscustomobject]@{ Codigo = $codigo; Texto = $texto }
}

$huella = @"
SET NOCOUNT ON;
DECLARE @sql nvarchar(max) = (SELECT STRING_AGG(CONVERT(nvarchar(max),
    N'SELECT N''' + t.name + N''' AS tabla, COUNT_BIG(*) AS filas, CHECKSUM_AGG(BINARY_CHECKSUM(*)) AS huella FROM dbo.' + QUOTENAME(t.name)), N' UNION ALL ')
    FROM sys.tables t WHERE t.schema_id = 1 AND NOT EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = t.object_id AND TYPE_NAME(c.user_type_id) IN (N'varbinary', N'image', N'xml', N'text', N'ntext')))
    + N' ORDER BY tabla';
EXEC sys.sp_executesql @sql;
"@

$huellaAntes = @{}
foreach ($origen in @($BaseA, $BaseB)) {
    $huellaAntes[$origen] = (Invoke-Sql -Base $origen -Consulta $huella).Texto
    if ($RespaldarOrigenes) {
        $archivo = Join-Path $Salida "$origen`_antes_HU053.bak"
        Invoke-Sql -Base master -Consulta "BACKUP DATABASE [$origen] TO DISK = N'$archivo' WITH COPY_ONLY, INIT, CHECKSUM, FORMAT;" | Out-Null
        Invoke-Sql -Base master -Consulta "RESTORE VERIFYONLY FROM DISK = N'$archivo' WITH CHECKSUM;" | Out-Null
    }
}

# Base destino desde cero.
Invoke-Sql -Base master -Consulta "IF DB_ID(N'$BaseDestino') IS NOT NULL BEGIN ALTER DATABASE [$BaseDestino] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$BaseDestino]; END" | Out-Null
$esquema = Get-Content (Join-Path $database "esquema\ClubCamioneros_unificada_crear_base.sql") -Raw -Encoding UTF8
$esquemaDestino = Join-Path $Salida "esquema_$BaseDestino.sql"
[IO.File]::WriteAllText($esquemaDestino, $esquema.Replace("ClubCamionerosUnificadaV2", $BaseDestino), [Text.UTF8Encoding]::new($false))
Invoke-Sql -Base master -Archivo $esquemaDestino | Out-Null

# Migraciones posteriores al esquema, con el mismo hash que la API (UTF-8, sin BOM, saltos de linea LF).
$registradas = (Invoke-Sql -Base $BaseDestino -Consulta "SET NOCOUNT ON; SELECT Nombre FROM dbo.__CaccMigraciones").Texto
foreach ($migracion in Get-ChildItem (Join-Path $database "migrations") -Filter "V*.sql" | Sort-Object Name) {
    if ($registradas -match [regex]::Escape($migracion.Name)) { continue }
    $texto = [IO.File]::ReadAllText($migracion.FullName, [Text.Encoding]::UTF8).TrimStart([char]0xFEFF).Replace("`r`n", "`n").Replace("`r", "`n")
    # Compatible con Windows PowerShell 5.1 (.NET Framework no tiene SHA256.HashData ni Convert.ToHexString).
    $sha = [Security.Cryptography.SHA256]::Create()
    $hash = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($texto)))).Replace("-", "")
    $sha.Dispose()
    Invoke-Sql -Base $BaseDestino -Archivo $migracion.FullName | Out-Null
    Invoke-Sql -Base $BaseDestino -Consulta "INSERT dbo.__CaccMigraciones (Nombre, Hash) VALUES (N'$($migracion.Name)', '$hash');" | Out-Null
}

if ($SoloEsquema) { Write-Host "Base $BaseDestino creada con el esquema unificado, sin importar datos. Log: $log"; exit 0 }

Invoke-Sql -Base $BaseDestino -Archivo (Join-Path $carpeta "01_preparar_migra.sql") | Out-Null
$analisis = Invoke-Sql -Base $BaseDestino -Archivo (Join-Path $carpeta "02_analizar_identidades.sql") -PermitirFalla
# Solo el error 53001 es un bloqueo por conflictos de DNI; cualquier otra falla es un error del paso y se informa como tal.
$bloqueada = $analisis.Codigo -ne 0 -and $analisis.Texto -match 'Msg 53001,'
if ($analisis.Codigo -ne 0 -and -not $bloqueada) { Write-Host $analisis.Texto; throw "El paso 2 fallo por un error (no por conflictos). Ver $log" }
if ($bloqueada) {
    Write-Host "Paso 2 BLOQUEADO (esperado si hay conflictos de DNI sin decision)."
    if (-not $ConResolucionDePrueba) { Write-Host "Ver migra.conflicto_dni en $BaseDestino y $log"; exit 2 }
    Invoke-Sql -Base $BaseDestino -Archivo (Join-Path $carpeta "resolucion_dni_PRUEBA.sql") | Out-Null
    Invoke-Sql -Base $BaseDestino -Archivo (Join-Path $carpeta "02_analizar_identidades.sql") | Out-Null
}
Invoke-Sql -Base $BaseDestino -Archivo (Join-Path $carpeta "03_importar.sql") | Out-Null
$validacion = Invoke-Sql -Base $BaseDestino -Archivo (Join-Path $carpeta "04_validar.sql") -PermitirFalla

# Los origenes no cambiaron.
$origenesIntactos = $true
foreach ($origen in @($BaseA, $BaseB)) {
    $despues = (Invoke-Sql -Base $origen -Consulta $huella).Texto
    if ($despues -ne $huellaAntes[$origen]) { $origenesIntactos = $false; Write-Host "ATENCION: la base de origen $origen cambio." }
}
"`nOrigenes intactos: $origenesIntactos" | Add-Content $log

Write-Host "`nLog completo: $log"
Write-Host "Paso 2 bloqueado sin resolucion: $bloqueada | Validacion: $(if ($validacion.Codigo -eq 0) { 'PASS' } else { 'FAIL' }) | Origenes intactos: $origenesIntactos"
if ($validacion.Codigo -ne 0 -or -not $origenesIntactos) { exit 1 }
