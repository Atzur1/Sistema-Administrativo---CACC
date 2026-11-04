<#
.SYNOPSIS
    HU-057: pruebas de punta a punta de la baja y la reincorporación informadas por el Portal Deportivo, contra la API real.

.DESCRIPTION
    Levanta ApiGestion (ya compilada) sobre una base de PRUEBA con el esquema unificado (HU-053) y las cuentas ficticias de
    seed_cuentas_prueba.sql. La clave JWT y la credencial de integración (HU-055) se generan al azar en memoria: no se imprimen
    ni se guardan. La generación automática de cuotas se apaga para que el resultado no dependa del momento en que corre.
    Los datos de la prueba usan DNI 9857 9xxx y una categoría propia; se limpian al empezar.
#>
[CmdletBinding()]
param(
    [string]$Servidor = "(localdb)\MSSQLLocalDB",
    [string]$Base = "ClubCamionerosUnificadaV2_HU057_Prueba",
    [string]$Clave = "Camioneros.Test.2026!",
    [int]$Puerto = 5157,
    [string]$Salida = (Join-Path $env:TEMP "cacc-hu057")
)

$ErrorActionPreference = "Stop"
if ($Base -notmatch '(?i)prueba') { throw "La base '$Base' no es de prueba." }
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$dll = Join-Path $repo "ApiGestion\bin\Debug\net9.0\ApiGestion.dll"
if (-not (Test-Path $dll)) { throw "Compilar ApiGestion antes (dotnet build ApiGestion)." }
New-Item -ItemType Directory -Force -Path $Salida | Out-Null
Add-Type -AssemblyName System.Net.Http

function Azar([int]$bytes = 32) {
    $b = New-Object byte[] $bytes; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
    [Convert]::ToBase64String($b).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}
function Sql([string]$Consulta) {
    $t = & sqlcmd -S $Servidor -E -C -b -I -W -h -1 -d $Base -Q "SET NOCOUNT ON; $Consulta" 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "SQL: $t" }
    $t.Trim()
}
function Iniciar-Api([hashtable]$Entorno, [string]$Log) {
    $info = New-Object Diagnostics.ProcessStartInfo "dotnet", "`"$dll`""
    $info.WorkingDirectory = Split-Path $dll
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Development"
    $info.EnvironmentVariables["ASPNETCORE_URLS"] = "http://localhost:$Puerto"
    $info.EnvironmentVariables["ConnectionStrings__ConexionSQL"] = "Server=$Servidor;Database=$Base;Trusted_Connection=True;TrustServerCertificate=True;"
    $info.EnvironmentVariables["GeneracionCuotas__Habilitada"] = "false"
    foreach ($k in $Entorno.Keys) { $info.EnvironmentVariables[$k] = $Entorno[$k] }
    $p = [Diagnostics.Process]::Start($info)
    [pscustomobject]@{ Proceso = $p; Salida = $p.StandardOutput.ReadToEndAsync(); Error = $p.StandardError.ReadToEndAsync(); Log = $Log }
}
function Detener-Api($api) {
    if (-not $api.Proceso.HasExited) { $api.Proceso.Kill(); $api.Proceso.WaitForExit(10000) | Out-Null }
    $texto = $api.Salida.Result + "`n" + $api.Error.Result
    [IO.File]::WriteAllText($api.Log, $texto)
    $texto
}
function Esperar-Api($api) {
    $cliente = New-Object Net.Http.HttpClient
    for ($i = 0; $i -lt 60; $i++) {
        if ($api.Proceso.HasExited) { return $false }
        try { $cliente.GetAsync("http://localhost:$Puerto/api/integracion/v1/estado-deuda/dni/1").Result | Out-Null; return $true } catch { Start-Sleep -Milliseconds 500 }
    }
    $false
}

$http = New-Object Net.Http.HttpClient
$http.BaseAddress = [Uri]"http://localhost:$Puerto"
function Mensaje([string]$Metodo, [string]$Ruta, [string]$Token, [object]$Credencial, [string]$Cuerpo) {
    $m = New-Object Net.Http.HttpRequestMessage ([Net.Http.HttpMethod]::new($Metodo)), $Ruta
    if ($Token) { $m.Headers.Authorization = New-Object Net.Http.Headers.AuthenticationHeaderValue "Bearer", $Token }
    if ($null -ne $Credencial) { $m.Headers.TryAddWithoutValidation("X-Integration-Key", [string[]]@($Credencial)) | Out-Null }
    if ($Cuerpo) { $m.Content = New-Object Net.Http.StringContent $Cuerpo, ([Text.Encoding]::UTF8), "application/json" }
    $m
}
function Pedir([string]$Metodo, [string]$Ruta, [string]$Token, [object]$Credencial, [string]$Cuerpo) {
    $r = $http.SendAsync((Mensaje $Metodo $Ruta $Token $Credencial $Cuerpo)).Result
    [pscustomobject]@{ Codigo = [int]$r.StatusCode; Cuerpo = $r.Content.ReadAsStringAsync().Result }
}

# ---- Datos: categoría con arancel propio y tres jugadores con historia de pagos.
Sql @"
DECLARE @p TABLE (id INT); INSERT @p SELECT PK_id_persona FROM PERSONAS WHERE Dni LIKE '98579___';
DECLARE @j TABLE (id INT); INSERT @j SELECT PK_id_jugador FROM JUGADORES WHERE FK_id_persona IN (SELECT id FROM @p);
DELETE FROM PAGOS WHERE FK_id_jugador IN (SELECT id FROM @j);
DELETE FROM JUGADORES_CATEGORIAS WHERE FK_id_jugador IN (SELECT id FROM @j);
DELETE FROM JUGADORES_PERIODOS WHERE FK_id_jugador IN (SELECT id FROM @j);
DELETE FROM JUGADORES WHERE PK_id_jugador IN (SELECT id FROM @j);
DELETE FROM PERSONAS WHERE PK_id_persona IN (SELECT id FROM @p);
DELETE FROM ARANCELES WHERE FK_id_categoria IN (SELECT PK_id_categoria FROM CATEGORIAS WHERE nombre_categoria = 'HU057 E2E');
IF NOT EXISTS (SELECT 1 FROM CATEGORIAS WHERE nombre_categoria = 'HU057 E2E') INSERT CATEGORIAS (nombre_categoria, asociacion) VALUES ('HU057 E2E', 'HU057');
INSERT ARANCELES (genero, FK_id_categoria, monto, vigente_desde) SELECT NULL, PK_id_categoria, 1000, '2026-01-01' FROM CATEGORIAS WHERE nombre_categoria = 'HU057 E2E';
INSERT PERSONAS (nombre, apellido, Dni, genero, fecha_de_nacimiento) VALUES ('Prueba', 'Baja A', '98579001', 'Masculino', '2012-01-01'), ('Prueba', 'Baja B', '98579002', 'Masculino', '2012-01-01'), ('Prueba', 'Baja C', '98579003', 'Masculino', '2012-01-01');
INSERT JUGADORES (FK_id_persona, FK_id_categoria, fecha_alta)
SELECT p.PK_id_persona, c.PK_id_categoria, CASE p.Dni WHEN '98579002' THEN '2026-10-20' ELSE '2026-08-01' END
FROM PERSONAS p CROSS JOIN CATEGORIAS c WHERE p.Dni LIKE '98579___' AND c.nombre_categoria = 'HU057 E2E';
"@ | Out-Null
$idA = Sql "SELECT j.PK_id_jugador FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.Dni = '98579001'"
$idB = Sql "SELECT j.PK_id_jugador FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.Dni = '98579002'"
$idC = Sql "SELECT j.PK_id_jugador FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.Dni = '98579003'"
$personaA = Sql "SELECT FK_id_persona FROM JUGADORES WHERE PK_id_jugador = $idA"
# Agosto vencida, septiembre parcial (600 pendientes + 400 abonados), octubre pendiente, noviembre emitida por adelantado sin pagos.
Sql @"
INSERT PAGOS (FK_id_jugador, monto_base, monto_final, fecha_vencimiento, estado, concepto) VALUES
  ($idA, 1000, 1000, '2026-08-01', 0, 'Cuota'), ($idA, 1000, 600, '2026-09-01', 0, 'Cuota'),
  ($idA, 1000, 1000, '2026-10-01', 0, 'Cuota'), ($idA, 1000, 1000, '2026-11-01', 0, 'Cuota');
INSERT PAGOS (FK_id_jugador, monto_base, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado, concepto)
VALUES ($idA, 1000, 400, '2026-09-12', 'Efectivo', '2026-09-01', 1, 'Cuota');
"@ | Out-Null
function PagosDe([string]$id, [string]$hasta) { Sql "SELECT STRING_AGG(CONCAT(PK_id_pago, ':', CONVERT(CHAR(10), fecha_vencimiento, 23), ':', monto_final, ':', estado, ':', metodo_pago), '|') WITHIN GROUP (ORDER BY PK_id_pago) FROM PAGOS WHERE FK_id_jugador = $id AND fecha_vencimiento <= '$hasta'" }
$historiaA = PagosDe $idA "2026-10-01"
$hoy = Sql "SELECT CONVERT(CHAR(10), CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Argentina Standard Time' AS DATE), 23)"
$manana = Sql "SELECT CONVERT(CHAR(10), DATEADD(DAY, 1, CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Argentina Standard Time' AS DATE)), 23)"
$idSuperAdmin = Sql "SELECT u.PK_id_usuario FROM USUARIOS u JOIN PERSONAS p ON p.PK_id_persona = u.FK_id_persona WHERE p.dni = '20111222'"
$idCategoria = Sql "SELECT PK_id_categoria FROM CATEGORIAS WHERE nombre_categoria = 'HU057 E2E'"

# ---- API con credenciales de esta corrida.
$credencial = Azar
$api = Iniciar-Api @{ "Jwt__Key" = (Azar 48); "Jwt__Issuer" = "CACC-HU057"; "Jwt__Audience" = "CACC-HU057"; "Integracion__ClienteDeportivo__Clave" = $credencial } (Join-Path $Salida "api_hu057.log")
if (-not (Esperar-Api $api)) { $t = Detener-Api $api; throw "La API no arrancó. Ver $($api.Log)" }
function Login([string]$Dni) {
    $r = Pedir "POST" "/api/auth/login" $null $null (@{ username = $Dni; password = $Clave } | ConvertTo-Json)
    if ($r.Codigo -ne 200) { throw "Login de $Dni respondió $($r.Codigo)" }
    ($r.Cuerpo | ConvertFrom-Json).token
}
$tSA = Login "20111222"
$tMedica = Login "30000003"
$bajaA = "/api/integracion/v1/jugadores/$idA/baja"
$reinA = "/api/integracion/v1/jugadores/$idA/reincorporacion"
$cuerpoBaja = '{"fechaBaja":"2026-10-15"}'
$auditoriaAntes = [int](Sql "SELECT ISNULL(MAX(PK_id_evento), 0) FROM AUDITORIA_CAMBIOS")

$resultados = New-Object Collections.Generic.List[object]
function Caso([string]$N, $Esperado, $Obtenido, [bool]$Ok, [string]$Respuesta = "") {
    $resultados.Add([pscustomobject]@{ Caso = $N; Esperado = "$Esperado"; Obtenido = "$Obtenido"; Resultado = if ($Ok) { "PASS" } else { "FAIL" }
        Respuesta = $Respuesta.Substring(0, [Math]::Min(160, $Respuesta.Length)) })
}
function HTTP([string]$N, [string]$M, [string]$R, [string]$T, [object]$C, [string]$B, [int]$Esperado, [string]$Codigo, [string]$Campo, [string]$Debe) {
    # $resp y no $r: PowerShell no distingue mayúsculas y $r pisaría el parámetro $R.
    $resp = Pedir $M $R $T $C $B
    $ok = $resp.Codigo -eq $Esperado
    if ($Codigo) { $ok = $ok -and $resp.Cuerpo -match "`"errorCode`":`"$Codigo`"" }
    if ($Campo) { $ok = $ok -and $resp.Cuerpo -match "`"field`":`"$Campo`"" }
    if ($Debe) { $ok = $ok -and $resp.Cuerpo -match $Debe }
    $ok = $ok -and ($resp.Cuerpo -notlike "*$credencial*") -and ($resp.Cuerpo -notmatch 'eyJ|   at |Exception|monto')
    Caso $N $Esperado $resp.Codigo $ok $resp.Cuerpo
    $resp
}

# ---- Seguridad (HU-055): sin la credencial de sistema no llega al endpoint; la credencial no es un usuario.
HTTP "S1. Baja sin credencial (token válido)" "POST" $bajaA $tSA $null $cuerpoBaja 401 "UNAUTHORIZED" $null $null | Out-Null
HTTP "S2. Baja con credencial inválida" "POST" $bajaA $tSA (Azar) $cuerpoBaja 401 "UNAUTHORIZED" $null $null | Out-Null
HTTP "S3. Baja con credencial válida SIN token" "POST" $bajaA $null $credencial $cuerpoBaja 401 $null $null $null | Out-Null
HTTP "S4. Baja con un rol sin permiso (Médica)" "POST" $bajaA $tMedica $credencial $cuerpoBaja 403 "FORBIDDEN" $null $null | Out-Null
HTTP "S5. Reincorporación sin credencial" "POST" $reinA $tSA $null $null 401 "UNAUTHORIZED" $null $null | Out-Null
Caso "S6. Ningún rechazo de seguridad tocó al jugador" "fecha_baja NULL" (Sql "SELECT ISNULL(CONVERT(CHAR(10), fecha_baja, 23), 'NULL') FROM JUGADORES WHERE PK_id_jugador = $idA") `
    ((Sql "SELECT COUNT(*) FROM JUGADORES WHERE PK_id_jugador = $idA AND fecha_baja IS NULL") -eq "1")

# ---- Validaciones de la baja (HU-054 / HU-058: contrato de errores común).
HTTP "V1. Jugador inexistente" "POST" "/api/integracion/v1/jugadores/999999999/baja" $tSA $credencial $cuerpoBaja 404 "NOT_FOUND" $null $null | Out-Null
HTTP "V2. Id de jugador 0" "POST" "/api/integracion/v1/jugadores/0/baja" $tSA $credencial $cuerpoBaja 400 "INVALID_INPUT" "idJugador" $null | Out-Null
HTTP "V3. Sin cuerpo (falta la fecha)" "POST" $bajaA $tSA $credencial $null 400 "INVALID_INPUT" "fechaBaja" $null | Out-Null
HTTP "V4. Fecha con otro formato (15/10/2026)" "POST" $bajaA $tSA $credencial '{"fechaBaja":"15/10/2026"}' 400 "INVALID_INPUT" "fechaBaja" $null | Out-Null
HTTP "V5. Fecha futura ($manana)" "POST" $bajaA $tSA $credencial "{`"fechaBaja`":`"$manana`"}" 400 "INVALID_INPUT" "fechaBaja" $null | Out-Null
HTTP "V6. JSON mal formado" "POST" $bajaA $tSA $credencial '{"fechaBaja":' 400 "INVALID_INPUT" $null $null | Out-Null
HTTP "V7. Baja anterior al alta (B entró el 20/10)" "POST" "/api/integracion/v1/jugadores/$idB/baja" $tSA $credencial $cuerpoBaja 422 "UNPROCESSABLE" $null "20/10/2026" | Out-Null
Caso "V8. Ninguna validación registró una baja" "0 bajas" (Sql "SELECT COUNT(*) FROM JUGADORES WHERE PK_id_jugador IN ($idA, $idB) AND fecha_baja IS NOT NULL") `
    ((Sql "SELECT COUNT(*) FROM JUGADORES WHERE PK_id_jugador IN ($idA, $idB) AND fecha_baja IS NOT NULL") -eq "0")

# ---- Baja efectiva, reintento y conflicto.
HTTP "B1. Baja de A con fecha efectiva 2026-10-15" "POST" $bajaA $tSA $credencial $cuerpoBaja 200 $null $null '"idJugador":\d+,"estado":"baja","fechaBaja":"2026-10-15","yaEstabaDadoDeBaja":false\}$' | Out-Null
HTTP "B2. Reintento idéntico (respuesta perdida)" "POST" $bajaA $tSA $credencial $cuerpoBaja 200 $null $null '"yaEstabaDadoDeBaja":true' | Out-Null
HTTP "B3. Otra baja con otra fecha" "POST" $bajaA $tSA $credencial '{"fechaBaja":"2026-10-01"}' 409 "CONFLICT" $null "15/10/2026" | Out-Null
HTTP "B4. estado-deuda informa la baja al QR" "GET" "/api/integracion/v1/estado-deuda/dni/98579001" $tSA $credencial $null 200 $null $null '"estado":"baja","accesoPermitido":false,"fechaBaja":"2026-10-15"' | Out-Null

$fila = Sql "SELECT CONCAT(PK_id_jugador, '|', FK_id_persona, '|', CONVERT(CHAR(10), fecha_baja, 23), '|', CAST(activo AS INT)) FROM JUGADORES WHERE PK_id_jugador = $idA"
Caso "B5. Baja lógica: mismo jugador y persona, fecha efectiva y activo=0" "$idA|$personaA|2026-10-15|0" $fila ($fila -eq "$idA|$personaA|2026-10-15|0")
$despues = PagosDe $idA "2026-10-01"
Caso "B6. Deuda y pagos hasta el mes de la baja intactos (ago vencida, sep parcial + abono, oct)" "sin cambios" $(if ($despues -eq $historiaA) { "sin cambios" } else { $despues }) ($despues -eq $historiaA)
$nov = Sql "SELECT COUNT(*) FROM PAGOS WHERE FK_id_jugador = $idA AND fecha_vencimiento = '2026-11-01'"
Caso "B7. La cuota de noviembre sin pagos (posterior a la baja) se quitó" "0" $nov ($nov -eq "0")
$deudores = Pedir "GET" "/api/pagos/pendientes" $tSA $null $null
Caso "B8. Deudas y Morosidad sigue mostrando a A con su deuda y su fecha de baja" 200 $deudores.Codigo ($deudores.Codigo -eq 200 -and $deudores.Cuerpo -match "`"idJugador`":$idA,[^}]*") ""
$periodos = Sql "SELECT STRING_AGG(CONCAT(CONVERT(CHAR(10), fecha_alta, 23), '..', CONVERT(CHAR(10), fecha_baja, 23)), '|') FROM JUGADORES_PERIODOS WHERE FK_id_jugador = $idA"
Caso "B9. JUGADORES_PERIODOS cierra la permanencia" "2026-08-01..2026-10-15" $periodos ($periodos -eq "2026-08-01..2026-10-15")
$ev = Sql "SELECT CONCAT(COUNT(*), '|', MIN(origen_sistema), '|', MIN(id_usuario)) FROM AUDITORIA_CAMBIOS WHERE PK_id_evento > $auditoriaAntes AND entidad = N'JUGADORES' AND id_entidad = '$idA' AND JSON_VALUE(datos_antes, '$.fecha_baja') IS NULL AND JSON_VALUE(datos_despues, '$.fecha_baja') = '2026-10-15'"
Caso "B10. Un solo evento de baja, origen PortalDeportivo y operador del token" "1|PortalDeportivo|$idSuperAdmin" $ev ($ev -eq "1|PortalDeportivo|$idSuperAdmin")
$evB = Sql "SELECT COUNT(*) FROM AUDITORIA_CAMBIOS WHERE PK_id_evento > $auditoriaAntes AND entidad = N'JUGADORES' AND id_entidad = '$idB'"
Caso "B11. La baja rechazada de B no dejó eventos" "0" $evB ($evB -eq "0")

# ---- Concurrencia por HTTP: seis bajas simultáneas de C.
$tareas = 1..6 | ForEach-Object { $http.SendAsync((Mensaje "POST" "/api/integracion/v1/jugadores/$idC/baja" $tSA $credencial $cuerpoBaja)) }
[Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]$tareas)
$cuerpos = $tareas | ForEach-Object { $_.Result.Content.ReadAsStringAsync().Result }
$codigos = ($tareas | ForEach-Object { [int]$_.Result.StatusCode }) -join ","
$nuevas = ($cuerpos | Where-Object { $_ -match '"yaEstabaDadoDeBaja":false' }).Count
$evC = Sql "SELECT COUNT(*) FROM AUDITORIA_CAMBIOS WHERE PK_id_evento > $auditoriaAntes AND entidad = N'JUGADORES' AND id_entidad = '$idC'"
Caso "C1. Seis bajas simultáneas: todas 200, una sola efectiva, un solo evento" "200x6, 1, 1" "$codigos, $nuevas, $evC" ($codigos -eq "200,200,200,200,200,200" -and $nuevas -eq 1 -and $evC -eq "1")

# ---- Reincorporación.
HTTP "R1. Categoría inexistente" "POST" $reinA $tSA $credencial '{"idCategoria":999999}' 422 "UNPROCESSABLE" $null $null | Out-Null
HTTP "R2. Categoría 0" "POST" $reinA $tSA $credencial '{"idCategoria":0}' 400 "INVALID_INPUT" "idCategoria" $null | Out-Null
HTTP "R3. Reincorporar a un jugador activo (B)" "POST" "/api/integracion/v1/jugadores/$idB/reincorporacion" $tSA $credencial $null 409 "CONFLICT" $null $null | Out-Null
$antesReingreso = Sql "SELECT COUNT(*) FROM PAGOS WHERE FK_id_jugador = $idA"
HTTP "R4. Reincorporación de A (sin cuerpo: conserva categoría)" "POST" $reinA $tSA $credencial $null 200 $null $null "`"fechaAlta`":`"$hoy`",`"categoriaCambiada`":false" | Out-Null
HTTP "R5. Reintento de la reincorporación" "POST" $reinA $tSA $credencial $null 409 "CONFLICT" $null $null | Out-Null
$filaR = Sql "SELECT CONCAT(PK_id_jugador, '|', FK_id_persona, '|', ISNULL(CONVERT(VARCHAR(10), fecha_baja, 23), 'NULL'), '|', CONVERT(CHAR(10), fecha_alta, 23), '|', FK_id_categoria) FROM JUGADORES WHERE PK_id_jugador = $idA"
Caso "R6. Mismo jugador y persona; fecha_baja NULL; fecha_alta = hoy; misma categoría" "$idA|$personaA|NULL|$hoy|$idCategoria" $filaR ($filaR -eq "$idA|$personaA|NULL|$hoy|$idCategoria")
$cuotas = Sql "SELECT CONCAT(COUNT(*), '|', SUM(CASE WHEN fecha_vencimiento = '2026-10-01' AND concepto = 'Cuota' AND estado = 0 THEN 1 ELSE 0 END), '|', SUM(CASE WHEN fecha_vencimiento > '2026-10-01' THEN 1 ELSE 0 END)) FROM PAGOS WHERE FK_id_jugador = $idA"
Caso "R7. Reingreso el día $($hoy.Substring(8)) (>= 10): la cuota de octubre ya existía, no se duplica; nada posterior" "$antesReingreso|1|0" $cuotas ($cuotas -eq "$antesReingreso|1|0")
Caso "R8. La deuda anterior sigue igual después del reingreso" "sin cambios" $(if ((PagosDe $idA "2026-10-01") -eq $historiaA) { "sin cambios" } else { "cambió" }) ((PagosDe $idA "2026-10-01") -eq $historiaA)
$periodosR = Sql "SELECT STRING_AGG(CONCAT(CONVERT(CHAR(10), fecha_alta, 23), '..', CONVERT(CHAR(10), fecha_baja, 23)), '|') WITHIN GROUP (ORDER BY fecha_alta) FROM JUGADORES_PERIODOS WHERE FK_id_jugador = $idA"
Caso "R9. Historial: la baja queda y se abre un período nuevo" "2026-08-01..2026-10-15|$hoy.." $periodosR ($periodosR -eq "2026-08-01..2026-10-15|$hoy..")
$evR = Sql "SELECT CONCAT(COUNT(*), '|', MIN(origen_sistema)) FROM AUDITORIA_CAMBIOS WHERE PK_id_evento > $auditoriaAntes AND entidad = N'JUGADORES' AND id_entidad = '$idA' AND JSON_VALUE(datos_antes, '$.fecha_baja') = '2026-10-15' AND JSON_VALUE(datos_despues, '$.fecha_baja') IS NULL"
Caso "R10. Reincorporación auditada una vez con origen PortalDeportivo" "1|PortalDeportivo" $evR ($evR -eq "1|PortalDeportivo")
HTTP "R11. estado-deuda vuelve a evaluar la deuda (ya no 'baja')" "GET" "/api/integracion/v1/estado-deuda/dni/98579001" $tSA $credencial $null 200 $null $null '"fechaBaja":null' | Out-Null

# ---- Reincorporación con categoría nueva (C).
$idCat2 = Sql "SELECT TOP (1) PK_id_categoria FROM CATEGORIAS WHERE PK_id_categoria <> $idCategoria ORDER BY PK_id_categoria"
HTTP "R12. Reincorporación de C con otra categoría" "POST" "/api/integracion/v1/jugadores/$idC/reincorporacion" $tSA $credencial "{`"idCategoria`":$idCat2}" 200 $null $null '"categoriaCambiada":true' | Out-Null
$filaC = Sql "SELECT CONCAT(j.FK_id_categoria, '|', (SELECT COUNT(*) FROM JUGADORES_CATEGORIAS c WHERE c.FK_id_jugador = j.PK_id_jugador AND c.FK_id_categoria = $idCat2)) FROM JUGADORES j WHERE j.PK_id_jugador = $idC"
Caso "R13. C queda en la categoría nueva (cobro y pertenencia)" "$idCat2|1" $filaC ($filaC -eq "$idCat2|1")

$log = Detener-Api $api
Caso "L1. La credencial y los tokens no aparecen en el log" "0" "$(([regex]::Matches($log, [regex]::Escape($credencial))).Count) credencial" `
    ($log -notlike "*$credencial*" -and $log -notlike "*$tSA*" -and $log -notmatch 'eyJ[A-Za-z0-9_-]{10,}')
Caso "L2. Los rechazos quedan en el log con la plantilla del endpoint" "presente" "" ($log -match 'Endpoint: POST api/integracion/v1/jugadores/\{idJugador:int\}/baja' -or $log -match 'Endpoint: POST api/integracion/v1/jugadores/\{idJugador\}/baja')

$resultados | Format-Table -AutoSize -Wrap Caso, Esperado, Obtenido, Resultado
$resultados | Where-Object { $_.Resultado -eq "FAIL" } | Format-List Caso, Obtenido, Respuesta
$resultados | ConvertTo-Json -Depth 3 | Set-Content -Encoding UTF8 (Join-Path $PSScriptRoot "evidencia_e2e.json")
"Total: $($resultados.Count) | PASS: $(($resultados | Where-Object Resultado -eq 'PASS').Count) | FAIL: $(($resultados | Where-Object Resultado -eq 'FAIL').Count)"
if ($resultados | Where-Object Resultado -eq "FAIL") { exit 1 }
