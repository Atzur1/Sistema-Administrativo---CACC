<#
.SYNOPSIS
    HU-055: pruebas de punta a punta de la credencial de sistema de la API de integración, contra la API real.

.DESCRIPTION
    Levanta ApiGestion (ya compilada) sobre una base de PRUEBA con el esquema unificado y las cuentas ficticias de
    cacc-unificacion/pruebas/seed_cuentas_prueba.sql. La clave JWT y la credencial de integración se generan al azar en
    memoria para esta corrida: no se imprimen ni se guardan. Al final busca la credencial y los tokens en el log de la API.
    También prueba el arranque sin la credencial y con la configuración de Development (user-secrets).
#>
[CmdletBinding()]
param(
    [string]$Servidor = "(localdb)\MSSQLLocalDB",
    [string]$Base = "ClubCamionerosUnificadaV2_HU055_Prueba",
    [string]$Clave = "Camioneros.Test.2026!",
    [int]$Puerto = 5199,
    [string]$Salida = (Join-Path $env:TEMP "cacc-hu055")
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
    foreach ($k in $Entorno.Keys) { $info.EnvironmentVariables[$k] = $Entorno[$k] }
    $p = [Diagnostics.Process]::Start($info)
    $salidaTarea = $p.StandardOutput.ReadToEndAsync()
    $errorTarea = $p.StandardError.ReadToEndAsync()
    [pscustomobject]@{ Proceso = $p; Salida = $salidaTarea; Error = $errorTarea; Log = $Log }
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
function Pedir([string]$Metodo, [string]$Ruta, [string]$Token, [object]$Credencial, [string]$Cuerpo) {
    $m = New-Object Net.Http.HttpRequestMessage ([Net.Http.HttpMethod]::new($Metodo)), $Ruta
    if ($Token) { $m.Headers.Authorization = New-Object Net.Http.Headers.AuthenticationHeaderValue "Bearer", $Token }
    if ($null -ne $Credencial) { $m.Headers.TryAddWithoutValidation("X-Integration-Key", [string[]]@($Credencial)) | Out-Null }
    if ($Cuerpo) { $m.Content = New-Object Net.Http.StringContent $Cuerpo, ([Text.Encoding]::UTF8), "application/json" }
    $r = $http.SendAsync($m).Result
    [pscustomobject]@{ Codigo = [int]$r.StatusCode; Cuerpo = $r.Content.ReadAsStringAsync().Result }
}

# ---- Datos de prueba: un jugador nuevo con aranceles vigentes (para que cargos-alta escriba de verdad).
Sql @"
IF NOT EXISTS (SELECT 1 FROM ARANCELES WHERE genero = 'Masculino') INSERT ARANCELES (genero, monto, vigente_desde) VALUES ('Masculino', 1000, '20260101');
IF NOT EXISTS (SELECT 1 FROM ARANCELES_INSCRIPCION WHERE genero = 'Masculino') INSERT ARANCELES_INSCRIPCION (genero, monto, vigente_desde) VALUES ('Masculino', 500, '20260101');
DELETE FROM PAGOS WHERE FK_id_jugador IN (SELECT j.PK_id_jugador FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni = '41000055');
"@ | Out-Null
$idJugador = Sql @"
IF NOT EXISTS (SELECT 1 FROM PERSONAS WHERE dni = '41000055')
    INSERT PERSONAS (nombre, apellido, dni, genero, fecha_de_nacimiento) VALUES ('Prueba', 'Integracion HU055', '41000055', 'Masculino', '20100101');
IF NOT EXISTS (SELECT 1 FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni = '41000055')
    INSERT JUGADORES (FK_id_persona, FK_id_categoria, fecha_alta) SELECT PK_id_persona, 1, CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Argentina Standard Time' AS date) FROM PERSONAS WHERE dni = '41000055';
UPDATE j SET fecha_alta = CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Argentina Standard Time' AS date) FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni = '41000055';
SELECT j.PK_id_jugador FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni = '41000055';
"@
$idSuperAdmin = Sql "SELECT u.PK_id_usuario FROM USUARIOS u JOIN PERSONAS p ON p.PK_id_persona = u.FK_id_persona WHERE p.dni = '20111222'"

# ---- API con credenciales de esta corrida.
$credencial = Azar
$api = Iniciar-Api @{ "Jwt__Key" = (Azar 48); "Jwt__Issuer" = "CACC-HU055"; "Jwt__Audience" = "CACC-HU055"; "Integracion__ClienteDeportivo__Clave" = $credencial } (Join-Path $Salida "api_hu055.log")
if (-not (Esperar-Api $api)) { $t = Detener-Api $api; throw "La API no arrancó. Ver $($api.Log)" }

function Login([string]$Dni) {
    $r = Pedir "POST" "/api/auth/login" $null $null (@{ username = $Dni; password = $Clave } | ConvertTo-Json)
    if ($r.Codigo -ne 200) { throw "Login de $Dni respondió $($r.Codigo)" }
    ($r.Cuerpo | ConvertFrom-Json).token
}
$tokenSuperAdmin = Login "20111222"
$tokenMedica = Login "30000003"
$dni = "/api/integracion/v1/estado-deuda/dni/41000055"
$cargos = "/api/integracion/v1/jugadores/$idJugador/cargos-alta"
$auditoriaAntes = [int](Sql "SELECT ISNULL(MAX(PK_id_evento), 0) FROM AUDITORIA_CAMBIOS")

$casos = @(
    @{ N = "1. Sin credencial (con token válido)"; M = "GET"; R = $dni; T = $tokenSuperAdmin; C = $null; Esperado = 401; Codigo = "UNAUTHORIZED" },
    @{ N = "2. Credencial vacía"; M = "GET"; R = $dni; T = $tokenSuperAdmin; C = ""; Esperado = 401; Codigo = "UNAUTHORIZED" },
    @{ N = "3. Credencial inválida"; M = "GET"; R = $dni; T = $tokenSuperAdmin; C = (Azar); Esperado = 401; Codigo = "UNAUTHORIZED" },
    @{ N = "3b. Credencial alterada (un carácter)"; M = "GET"; R = $dni; T = $tokenSuperAdmin; C = ($credencial.Substring(0, $credencial.Length - 1) + "x"); Esperado = 401; Codigo = "UNAUTHORIZED" },
    @{ N = "3c. Alias /api/estado-deuda sin credencial"; M = "GET"; R = "/api/estado-deuda/dni/41000055"; T = $tokenSuperAdmin; C = $null; Esperado = 401; Codigo = "UNAUTHORIZED" },
    @{ N = "3d. Lote sin credencial"; M = "POST"; R = "/api/integracion/v1/estado-deuda/lote"; T = $tokenSuperAdmin; C = $null; B = '{"dnis":["41000055"]}'; Esperado = 401; Codigo = "UNAUTHORIZED" },
    @{ N = "4. Credencial válida + token"; M = "GET"; R = $dni; T = $tokenSuperAdmin; C = $credencial; Esperado = 200; Codigo = $null },
    @{ N = "4b. Credencial válida + token (lote)"; M = "POST"; R = "/api/integracion/v1/estado-deuda/lote"; T = $tokenSuperAdmin; C = $credencial; B = '{"dnis":["41000055"]}'; Esperado = 200; Codigo = $null },
    @{ N = "4c. Credencial válida SIN token"; M = "GET"; R = $dni; T = $null; C = $credencial; Esperado = 401; Codigo = "UNAUTHORIZED" },
    @{ N = "4d. Credencial válida + rol sin permiso (Médica en cargos-alta)"; M = "POST"; R = $cargos; T = $tokenMedica; C = $credencial; Esperado = 403; Codigo = "FORBIDDEN" },
    @{ N = "5. Credencial válida + DNI inválido"; M = "GET"; R = "/api/integracion/v1/estado-deuda/dni/12ab"; T = $tokenSuperAdmin; C = $credencial; Esperado = 400; Codigo = "INVALID_INPUT" },
    @{ N = "5b. Credencial válida + jugador inexistente"; M = "POST"; R = "/api/integracion/v1/jugadores/999999/cargos-alta"; T = $tokenSuperAdmin; C = $credencial; Esperado = 404; Codigo = "NOT_FOUND" },
    @{ N = "6. Solo credencial contra /api/usuarios/habilitados"; M = "GET"; R = "/api/usuarios/habilitados"; T = $null; C = $credencial; Esperado = 401; Codigo = $null },
    @{ N = "6b. Solo credencial contra /api/auditoria/cambios"; M = "GET"; R = "/api/auditoria/cambios"; T = $null; C = $credencial; Esperado = 401; Codigo = $null },
    @{ N = "6c. Solo credencial contra /api/pagos"; M = "GET"; R = "/api/pagos/pendientes"; T = $null; C = $credencial; Esperado = 401; Codigo = $null },
    @{ N = "7. Escritura: cargos-alta con credencial + SuperAdmin"; M = "POST"; R = $cargos; T = $tokenSuperAdmin; C = $credencial; Esperado = 200; Codigo = $null }
)

$resultados = foreach ($c in $casos) {
    $r = Pedir $c.M $c.R $c.T $c.C $c.B
    $codigoOk = if ($c.Codigo) { $r.Cuerpo -match "`"errorCode`":`"$($c.Codigo)`"" } else { $true }
    $sinDatos = if ($r.Codigo -eq 401) { $r.Cuerpo -notmatch 'idJugador|accesoPermitido|"estado"' } else { $true }
    $sinSecretos = ($r.Cuerpo -notlike "*$credencial*") -and ($r.Cuerpo -notmatch 'eyJ') -and ($r.Cuerpo -notmatch 'at [A-Z][A-Za-z]+\.')
    [pscustomobject]@{ Caso = $c.N; Esperado = $c.Esperado; Obtenido = $r.Codigo
        Resultado = if ($r.Codigo -eq $c.Esperado -and $codigoOk -and $sinDatos -and $sinSecretos) { "PASS" } else { "FAIL" }
        Respuesta = $r.Cuerpo.Substring(0, [Math]::Min(110, $r.Cuerpo.Length)) }
}

# ---- 7. Auditoría de la escritura: los eventos de esta llamada llevan origen Deportivo y el operador del token.
$auditoria = Sql "SELECT CONCAT(COUNT(*), '|', SUM(CASE WHEN origen_sistema = 'PortalDeportivo' THEN 1 ELSE 0 END), '|', SUM(CASE WHEN id_usuario = $idSuperAdmin THEN 1 ELSE 0 END), '|', STRING_AGG(entidad + ':' + accion, ',')) FROM AUDITORIA_CAMBIOS WHERE PK_id_evento > $auditoriaAntes"
$partes = $auditoria.Split('|')
$resultados += [pscustomobject]@{ Caso = "7b. Eventos de la escritura con origen PortalDeportivo y operador SuperAdmin"; Esperado = "todos"; Obtenido = "$($partes[1])/$($partes[0]) origen, $($partes[2])/$($partes[0]) operador ($($partes[3]))"
    Resultado = if ([int]$partes[0] -gt 0 -and $partes[0] -eq $partes[1] -and $partes[0] -eq $partes[2]) { "PASS" } else { "FAIL" }; Respuesta = "" }
$api7 = Pedir "GET" "/api/auditoria/cambios?pageSize=5" $tokenSuperAdmin $null $null
$resultados += [pscustomobject]@{ Caso = "7c. La API de Auditoría devuelve el origen (sourceSystem)"; Esperado = 200; Obtenido = $api7.Codigo
    Resultado = if ($api7.Codigo -eq 200 -and $api7.Cuerpo -match '"sourceSystem":"PortalDeportivo"') { "PASS" } else { "FAIL" }; Respuesta = "" }
$repetida = Pedir "POST" $cargos $tokenSuperAdmin $credencial $null
$resultados += [pscustomobject]@{ Caso = "7d. Repetir cargos-alta no duplica (idempotente)"; Esperado = 200; Obtenido = $repetida.Codigo
    Resultado = if ($repetida.Codigo -eq 200 -and $repetida.Cuerpo -match '"inscripcionGenerada":false' -and $repetida.Cuerpo -match '"cuotaGenerada":false') { "PASS" } else { "FAIL" }; Respuesta = $repetida.Cuerpo }

$log = Detener-Api $api
# ---- 8. Ni la credencial ni los tokens en el log; sí los rechazos con endpoint y correlación.
$resultados += [pscustomobject]@{ Caso = "8. La credencial y los tokens no aparecen en el log de la API"; Esperado = "0"; Obtenido = "$(([regex]::Matches($log, [regex]::Escape($credencial))).Count) credencial, $(([regex]::Matches($log, 'eyJ[A-Za-z0-9_-]{10,}')).Count) tokens"
    Resultado = if ($log -notlike "*$credencial*" -and $log -notlike "*$tokenSuperAdmin*" -and $log -notmatch 'eyJ[A-Za-z0-9_-]{10,}') { "PASS" } else { "FAIL" }; Respuesta = "" }
$rechazos = ([regex]::Matches($log, 'Credencial de sistema (ausente|inv.lida)\. Correlaci.n: [0-9A-Z]+:[0-9]{8}')).Count
$resultados += [pscustomobject]@{ Caso = "8b. Los rechazos quedan en el log con endpoint y correlación"; Esperado = ">= 6"; Obtenido = $rechazos
    Resultado = if ($rechazos -ge 6 -and $log -match 'Endpoint: GET api/integracion/v1/estado-deuda/dni/\{dni\}') { "PASS" } else { "FAIL" }; Respuesta = "" }

# ---- 10. Sin la credencial (vacía o débil) la API no arranca, y el error nombra la configuración sin mostrar valores.
foreach ($mala in @("", "1234")) {
    $a = Iniciar-Api @{ "Jwt__Key" = (Azar 48); "Jwt__Issuer" = "CACC-HU055"; "Jwt__Audience" = "CACC-HU055"; "Integracion__ClienteDeportivo__Clave" = $mala } (Join-Path $Salida "api_sin_credencial.log")
    $termino = $a.Proceso.WaitForExit(60000)
    $texto = Detener-Api $a
    $ok = $termino -and $a.Proceso.ExitCode -ne 0 -and $texto -match 'Integracion:ClienteDeportivo:Clave' -and ($mala -eq "" -or $texto -notmatch [regex]::Escape("=$mala"))
    $resultados += [pscustomobject]@{ Caso = "10. Arranque con la credencial '$(if ($mala) { 'débil' } else { 'vacía' })'"; Esperado = "no arranca"; Obtenido = "terminó=$termino, código=$($a.Proceso.ExitCode)"
        Resultado = if ($ok) { "PASS" } else { "FAIL" }; Respuesta = "" }
}

# ---- 9. Development: sin variables de entorno de la credencial, la API toma la de user-secrets.
$secreto = ((& dotnet user-secrets list --project (Join-Path $repo "ApiGestion")) | Where-Object { $_ -like "Integracion:ClienteDeportivo:Clave = *" }) -replace '^Integracion:ClienteDeportivo:Clave = ', ''
$jwtDev = Azar 48
$dev = Iniciar-Api @{ "Jwt__Key" = $jwtDev; "Jwt__Issuer" = "CACC-HU055"; "Jwt__Audience" = "CACC-HU055" } (Join-Path $Salida "api_development.log")
$arranco = Esperar-Api $dev
$tokenDev = if ($arranco) { (Pedir "POST" "/api/auth/login" $null $null (@{ username = "20111222"; password = $Clave } | ConvertTo-Json)).Cuerpo | ConvertFrom-Json | Select-Object -ExpandProperty token } else { $null }
$conSecreto = if ($arranco) { Pedir "GET" $dni $tokenDev $secreto $null } else { $null }
$sinSecreto = if ($arranco) { Pedir "GET" $dni $tokenDev $null $null } else { $null }
$logDev = Detener-Api $dev
$resultados += [pscustomobject]@{ Caso = "9. Development con user-secrets: arranca, acepta su credencial y rechaza sin ella"; Esperado = "200 / 401"
    Obtenido = "arrancó=$arranco, $($conSecreto.Codigo) / $($sinSecreto.Codigo)"
    Resultado = if ($arranco -and $conSecreto.Codigo -eq 200 -and $sinSecreto.Codigo -eq 401 -and $secreto -and $logDev -notlike "*$secreto*") { "PASS" } else { "FAIL" }; Respuesta = "" }

$resultados | Format-Table -AutoSize -Wrap Caso, Esperado, Obtenido, Resultado
$resultados | Where-Object { $_.Resultado -eq "FAIL" } | Format-List Caso, Respuesta
"Total: $($resultados.Count) | PASS: $(($resultados | Where-Object Resultado -eq 'PASS').Count) | FAIL: $(($resultados | Where-Object Resultado -eq 'FAIL').Count)"
if ($resultados | Where-Object Resultado -eq "FAIL") { exit 1 }
