<#
.SYNOPSIS
    HU-059: pruebas de punta a punta de la integración Portal Deportivo <-> Portal Administrativo, con las dos APIs reales
    y SQL Server real sobre la base unificada de prueba (HU-053).

.DESCRIPTION
    1. Prepara la base: reset_base.ps1 (esquema unificado + migraciones + cuentas ficticias + fixtures). Siempre el mismo estado.
    2. Levanta la API del Administrativo y la del Deportivo con una clave JWT y una credencial de integración generadas al azar
       en memoria para esta corrida (no se imprimen ni se guardan), y espera con healthchecks que lleguen a SQL Server.
    3. Ejecuta los escenarios por los endpoints reales (nunca INSERT/UPDATE como atajo) y valida la respuesta Y lo persistido.
    4. Escribe el resumen (PASS / FAIL / BLOCKED) y la evidencia en -Salida; frena las APIs.
    Código de salida: 0 = todo PASS; 1 = algún FAIL o BLOCKED; 2 = el entorno no se pudo preparar.

    El Deportivo es la COPIA PARCHEADA que arma cacc-unificacion/scripts/armar.ps1 (ver database/InstructivoEquipo.md).

.EXAMPLE
    $env:CACC_E2E_DEPORTIVO_DLL = "C:\...\trabajo\ellos\BackEnd\CACC.API\bin\Debug\net9.0\CACC.API.dll"
    .\qa\HU-059\e2e_hu059.ps1
#>
# PowerShell 7: en Windows PowerShell 5.1 ConvertFrom-Json no enumera "[]" y un .ps1 UTF-8 sin BOM se lee como ANSI, y la
# suite daría resultados falsos sin avisar (verificado el 29/10: NOJ-1 y NOJ-4 en FAIL solo por correrla con 5.1).
#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$Servidor = $(if ($env:CACC_E2E_SQL_SERVIDOR) { $env:CACC_E2E_SQL_SERVIDOR } else { "(localdb)\MSSQLLocalDB" }),
    [string]$Base = $(if ($env:CACC_E2E_BASE) { $env:CACC_E2E_BASE } else { "ClubCamionerosUnificadaV2_HU059_Prueba" }),
    [string]$AdministrativoDll = $env:CACC_E2E_ADMINISTRATIVO_DLL,
    [string]$DeportivoDll = $env:CACC_E2E_DEPORTIVO_DLL,
    [int]$PuertoAdministrativo = $(if ($env:CACC_E2E_PUERTO_ADMINISTRATIVO) { [int]$env:CACC_E2E_PUERTO_ADMINISTRATIVO } else { 5418 }),
    [int]$PuertoDeportivo = $(if ($env:CACC_E2E_PUERTO_DEPORTIVO) { [int]$env:CACC_E2E_PUERTO_DEPORTIVO } else { 5419 }),
    # Clave de las cuentas FICTICIAS de datos/01_cuentas_prueba.sql (publicada en cacc-unificacion; no es un secreto real).
    [string]$ClaveCuentasPrueba = $(if ($env:CACC_E2E_CLAVE_CUENTAS) { $env:CACC_E2E_CLAVE_CUENTAS } else { "Camioneros.Test.2026!" }),
    [int]$TimeoutSegundos = 90,
    [string]$Salida = $(Join-Path $env:TEMP ("cacc-hu059\ejecucion_{0:yyyyMMdd_HHmmss}" -f (Get-Date))),
    [string]$Evidencia = "",
    [switch]$SinReset
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (-not $AdministrativoDll) { $AdministrativoDll = Join-Path $repo "ApiGestion\bin\Debug\net9.0\ApiGestion.dll" }
if (-not $DeportivoDll) { $DeportivoDll = Join-Path $env:TEMP "cacc-hu059\trabajo\ellos\BackEnd\CACC.API\bin\Debug\net9.0\CACC.API.dll" }
New-Item -ItemType Directory -Force -Path $Salida | Out-Null
Add-Type -AssemblyName System.Net.Http
$inicioCorrida = Get-Date

function Fatal([string]$Mensaje) { Write-Host "ENTORNO NO DISPONIBLE: $Mensaje" -ForegroundColor Red; exit 2 }
foreach ($archivo in @($AdministrativoDll, $DeportivoDll)) { if (-not (Test-Path $archivo)) { Fatal "no encuentro $archivo (ver 'Arranque' en database/InstructivoEquipo.md)." } }
if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) { Fatal "falta sqlcmd en el PATH." }

. (Join-Path $PSScriptRoot "comun.ps1")


# ---------------------------------------------------------------- preparación
try {
    if (-not $SinReset) {
        Write-Host "== Reset de la base $Base" -ForegroundColor Cyan
        & (Join-Path $PSScriptRoot "reset_base.ps1") -Servidor $Servidor -Base $Base -AdministrativoDll $AdministrativoDll -Salida $Salida
    }
} catch { Fatal "el reset de la base falló: $($_.Exception.Message)" }

$hoy = [datetime]::ParseExact((Sql "SELECT CONVERT(CHAR(10), CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Argentina Standard Time' AS DATE), 23)"), "yyyy-MM-dd", $null)
$periodoActual = if ($hoy.Day -ge 10) { New-Object DateTime $hoy.Year, $hoy.Month, 1 } else { (New-Object DateTime $hoy.Year, $hoy.Month, 1).AddMonths(-1) }
$periodoTxt = $periodoActual.ToString("yyyy-MM-dd")
Write-Host "Hoy (club): $($hoy.ToString('yyyy-MM-dd')) | último período emitible: $periodoTxt | salida: $Salida"

$ids = @{}
foreach ($dni in "98590001", "98590002", "98590003", "98590006") { $ids[$dni] = [int](Sql "SELECT j.PK_id_jugador FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni = '$dni'") }
$personaExistente = [int](Sql "SELECT PK_id_persona FROM PERSONAS WHERE dni = '98590004'")
$idSuperAdmin = [int](Sql "SELECT u.PK_id_usuario FROM USUARIOS u JOIN PERSONAS p ON p.PK_id_persona = u.FK_id_persona WHERE p.dni = '20111222'")

$administrativo = $null; $deportivo = $null
try {
    Write-Host "== Levantando las APIs" -ForegroundColor Cyan
    $administrativo = Iniciar "administrativo" $AdministrativoDll $PuertoAdministrativo (EntornoAdministrativo $false)
    $deportivo = Iniciar "deportivo" $DeportivoDll $PuertoDeportivo (EntornoDeportivo $credencial)

    $saludSql = Esperar "SQL" { [pscustomobject]@{ Ok = ((Sql "SELECT COUNT(*) FROM USUARIOS") -eq "5"); Detalle = "USUARIOS" } } $null
    Verificar "Entorno" "ENV-1" "SQL Server responde y la base de prueba tiene los datos iniciales" $saludSql.Ok "5 usuarios" $saludSql.Detalle
    $saludAdm = Esperar "Administrativo" { Login } $administrativo
    Verificar "Entorno" "ENV-2" "API Administrativa lista (login: lee USUARIOS en SQL)" $saludAdm.Ok "200" $saludAdm.Detalle
    if (-not $saludAdm.Ok) { throw "La API Administrativa no quedó lista: $($saludAdm.Detalle)" }
    $token = $saludAdm.Token
    $saludDep = Esperar "Deportivo" { $r = Pedir "GET" "$urlDeportivo/api/jugadores" $token $null $null; [pscustomobject]@{ Ok = ($r.Codigo -eq 200); Detalle = "GET /api/jugadores -> $($r.Codigo)" } } $deportivo
    Verificar "Entorno" "ENV-3" "API Deportiva lista (GET /api/jugadores: lee JUGADORES en SQL)" $saludDep.Ok "200" $saludDep.Detalle
    if (-not $saludDep.Ok) { throw "La API Deportiva no quedó lista: $($saludDep.Detalle)" }
    $auditoriaInicial = [int](Sql "SELECT ISNULL(MAX(PK_id_evento), 0) FROM AUDITORIA_CAMBIOS")

    # ============================================================ HU-055 autenticación entre sistemas
    $g = "HU-055 autenticación"
    $rutaEstado = "$urlAdministrativo/api/integracion/v1/estado-deuda/dni/98590003"
    $sinClave = Pedir "GET" $rutaEstado $token $null $null
    Verificar $g "AUT-1" "Sin credencial de sistema: 401 con el contrato de error y sin datos" `
        ($sinClave.Codigo -eq 401 -and $sinClave.Cuerpo -match '"errorCode":"UNAUTHORIZED"' -and $sinClave.Cuerpo -notmatch 'idJugador|accesoPermitido|"estado"') 401 $sinClave.Codigo $sinClave.Cuerpo
    $claveMala = Pedir "GET" $rutaEstado $token (Azar 32) $null
    Verificar $g "AUT-2" "Credencial inválida: 401 sin datos" `
        ($claveMala.Codigo -eq 401 -and $claveMala.Cuerpo -match '"errorCode":"UNAUTHORIZED"' -and $claveMala.Cuerpo -notmatch 'idJugador') 401 $claveMala.Codigo $claveMala.Cuerpo
    $claveBuena = Pedir "GET" $rutaEstado $token $credencial $null
    Verificar $g "AUT-3" "Credencial válida + token: 200 con el estado" `
        ($claveBuena.Codigo -eq 200 -and (Json $claveBuena.Cuerpo).estado -eq "habilitado") "200 habilitado" "$($claveBuena.Codigo) $((Json $claveBuena.Cuerpo).estado)" $claveBuena.Cuerpo
    $soloClave = Pedir "GET" $rutaEstado $null $credencial $null
    Verificar $g "AUT-4" "Credencial válida SIN token de usuario: 401 (la credencial no es un usuario)" ($soloClave.Codigo -eq 401) 401 $soloClave.Codigo $soloClave.Cuerpo
    $api2api = Pedir "GET" "$urlDeportivo/api/jugadores/$($ids['98590001'])" $token $null $null
    $estadoDep = (Json $api2api.Cuerpo).estadoCuota
    Verificar $g "AUT-5" "API -> API: el Deportivo (con su credencial) obtiene del Administrativo el estado de un jugador con 2 cuotas vencidas" `
        ($api2api.Codigo -eq 200 -and $estadoDep -eq "INHABILITADO") "200 INHABILITADO" "$($api2api.Codigo) $estadoDep"
    # Un segundo Deportivo con una credencial equivocada: el Administrativo lo rechaza y el Deportivo no puede verificar el estado.
    $deportivoMalo = Iniciar "deportivo_credencial_mala" $DeportivoDll ($PuertoDeportivo + 2) (EntornoDeportivo (Azar 32))
    $saludMalo = Esperar "Deportivo (credencial mala)" { $r = Pedir "GET" "http://localhost:$($PuertoDeportivo + 2)/api/jugadores" $token $null $null; [pscustomobject]@{ Ok = ($r.Codigo -eq 200); Detalle = "$($r.Codigo)" } } $deportivoMalo
    $conClaveMala = if ($saludMalo.Ok) { Pedir "GET" "http://localhost:$($PuertoDeportivo + 2)/api/jugadores/$($ids['98590001'])" $token $null $null } else { $null }
    $estadoMalo = if ($conClaveMala) { (Json $conClaveMala.Cuerpo).estadoCuota } else { "no arrancó: $($saludMalo.Detalle)" }
    Verificar $g "AUT-6" "API -> API con credencial equivocada: el Administrativo no entrega el estado (SIN VERIFICAR)" ($estadoMalo -eq "SIN VERIFICAR") "SIN VERIFICAR" $estadoMalo
    Detener $deportivoMalo | Out-Null

    # ============================================================ HU-054 contrato (formato de errores en los endpoints de escritura)
    $g = "HU-054 contrato"
    $fechaMala = Pedir "POST" "$urlAdministrativo/api/integracion/v1/jugadores/$($ids['98590003'])/baja" $token $credencial @{ fechaBaja = "20/09/2026" }
    $cuerpo = Json $fechaMala.Cuerpo
    Verificar $g "CON-1" "Dato inválido: 400 INVALID_INPUT con field" ($fechaMala.Codigo -eq 400 -and $cuerpo.errorCode -eq "INVALID_INPUT" -and $cuerpo.field -eq "fechaBaja" -and $cuerpo.message) `
        "400 INVALID_INPUT fechaBaja" "$($fechaMala.Codigo) $($cuerpo.errorCode) $($cuerpo.field)" $fechaMala.Cuerpo
    $noExiste = Pedir "POST" "$urlAdministrativo/api/integracion/v1/jugadores/999999999/baja" $token $credencial @{ fechaBaja = "2026-09-20" }
    Verificar $g "CON-2" "Jugador inexistente: 404 NOT_FOUND con cuerpo" ($noExiste.Codigo -eq 404 -and (Json $noExiste.Cuerpo).errorCode -eq "NOT_FOUND") "404 NOT_FOUND" "$($noExiste.Codigo) $((Json $noExiste.Cuerpo).errorCode)" $noExiste.Cuerpo
    $rolSinPermiso = Pedir "POST" "$urlAdministrativo/api/auth/login" $null $null @{ username = "30000003"; password = $ClaveCuentasPrueba }
    $tokenMedica = (Json $rolSinPermiso.Cuerpo).token
    $prohibido = Pedir "POST" "$urlAdministrativo/api/integracion/v1/jugadores/$($ids['98590003'])/baja" $tokenMedica $credencial @{ fechaBaja = "2026-09-20" }
    Verificar $g "CON-3" "Rol sin permiso (Médica): 403 FORBIDDEN" ($prohibido.Codigo -eq 403 -and (Json $prohibido.Cuerpo).errorCode -eq "FORBIDDEN") "403 FORBIDDEN" "$($prohibido.Codigo)" $prohibido.Cuerpo
    Verificar $g "CON-4" "Ningún rechazo del contrato modificó al jugador" ((Sql "SELECT COUNT(*) FROM JUGADORES WHERE fecha_baja IS NOT NULL") -eq "0") "0 bajas" (Sql "SELECT COUNT(*) FROM JUGADORES WHERE fecha_baja IS NOT NULL")

    # ============================================================ HU-056 alta de jugador desde el Deportivo
    # Dependencia: la API de integración del Administrativo tiene que ofrecer POST /api/integracion/v1/jugadores.
    $sonda = Pedir "POST" "$urlAdministrativo/api/integracion/v1/jugadores" $token $credencial @{}
    $altaDisponible = -not (($sonda.Codigo -eq 404 -and $sonda.Cuerpo -notmatch '"errorCode"') -or $sonda.Codigo -in 405, 500, 501)
    $motivoBloqueo = "POST /api/integracion/v1/jugadores en el Administrativo responde $($sonda.Codigo) $($sonda.Cuerpo): HU-056 no está implementada en el Administrativo"
    function Alta([string]$Dni, [string]$Apellido) {
        Pedir "POST" "$urlDeportivo/api/jugadores" $token $null @{ dni = $Dni; nombre = "prueba"; apellido = $Apellido; fechaNacimiento = "2012-05-20"; genero = "Masculino"; categoria = "3" }
    }
    function Cuentas([string]$Dni) {
        Sql "SELECT CONCAT((SELECT COUNT(*) FROM PERSONAS WHERE dni = '$Dni'), '|', (SELECT COUNT(*) FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni = '$Dni'), '|',
            (SELECT COUNT(*) FROM PAGOS g JOIN JUGADORES j ON j.PK_id_jugador = g.FK_id_jugador JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni = '$Dni' AND g.concepto = 'Cuota'), '|',
            (SELECT COUNT(*) FROM PAGOS g JOIN JUGADORES j ON j.PK_id_jugador = g.FK_id_jugador JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni = '$Dni' AND g.concepto = 'Inscripcion'))"
    }
    $g = "HU-056 alta"
    $alta = Alta "98590005" "nueva"
    $cuentasAlta = Cuentas "98590005"
    if (-not $altaDisponible) {
        Registrar $g "ALT-1" "Alta de una persona nueva desde el Deportivo (persona, jugador, categoría, cargos)" "BLOCKED" "201 + 1 persona, 1 jugador, inscripción y cuota según calendario" "$($alta.Codigo); persona|jugador|cuotas|inscripciones = $cuentasAlta" $motivoBloqueo
        Verificar $g "ALT-1b" "El alta rechazada no dejó datos a medias" ($cuentasAlta -eq "0|0|0|0") "0|0|0|0" $cuentasAlta $alta.Cuerpo
        Registrar $g "ALT-2" "El jugador aparece en el Administrativo y disponible para cobranzas" "BLOCKED" "visible" "no se creó" $motivoBloqueo
        Registrar $g "ALT-3" "Auditoría del alta con origen PortalDeportivo" "BLOCKED" "PortalDeportivo" "no se creó" $motivoBloqueo
    } else {
        $idNuevo = [int](Sql "SELECT ISNULL(MAX(j.PK_id_jugador), 0) FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni = '98590005'")
        $cuotaEsperada = if ($hoy.Day -ge 10) { 1 } else { 0 }
        $fila = Sql "SELECT CONCAT(j.FK_id_categoria, '|', CONVERT(CHAR(10), j.fecha_alta, 23), '|', (SELECT COUNT(*) FROM JUGADORES_CATEGORIAS c WHERE c.FK_id_jugador = j.PK_id_jugador AND c.FK_id_categoria = 3)) FROM JUGADORES j WHERE j.PK_id_jugador = $idNuevo"
        Verificar $g "ALT-1" "Alta de una persona nueva desde el Deportivo: 1 persona, 1 jugador en categoría 3, inscripción y cuota según calendario" `
            ($alta.Codigo -eq 201 -and $cuentasAlta -eq "1|1|$cuotaEsperada|1" -and $fila -eq "3|$($hoy.ToString('yyyy-MM-dd'))|1") "201; 1|1|$cuotaEsperada|1; 3|hoy|1" "$($alta.Codigo); $cuentasAlta; $fila" $alta.Cuerpo
        $enAdm = Pedir "GET" "$urlAdministrativo/api/jugadores/$idNuevo" $token $null $null
        $enEstado = Pedir "GET" "$urlAdministrativo/api/integracion/v1/estado-deuda/dni/98590005" $token $credencial $null
        Verificar $g "ALT-2" "El jugador aparece en el Administrativo (perfil) y en estado-deuda" ($enAdm.Codigo -eq 200 -and $enEstado.Codigo -eq 200) "200 / 200" "$($enAdm.Codigo) / $($enEstado.Codigo)"
        $auditAlta = Sql "SELECT CONCAT(COUNT(*), '|', SUM(CASE WHEN origen_sistema = 'PortalDeportivo' THEN 1 ELSE 0 END)) FROM AUDITORIA_CAMBIOS WHERE PK_id_evento > $auditoriaInicial AND accion = 'INSERT' AND ((entidad = N'JUGADORES' AND id_entidad = '$idNuevo') OR entidad = N'PERSONAS')"
        $partes = $auditAlta.Split('|')
        Verificar $g "ALT-3" "Auditoría del alta (INSERT de persona y jugador) con origen PortalDeportivo" ([int]$partes[0] -ge 2 -and $partes[0] -eq $partes[1]) "todos PortalDeportivo" $auditAlta
    }

    $g = "HU-056 DNI existente"
    $altaExistente = Alta "98590004" "existente"
    $cuentasExistente = Cuentas "98590004"
    $personaDespues = Sql "SELECT CONCAT(COUNT(*), '|', MIN(PK_id_persona)) FROM PERSONAS WHERE dni = '98590004'"
    if (-not $altaDisponible) {
        Registrar $g "DNI-1" "Alta con un DNI cuya persona ya existe: misma persona, un jugador nuevo" "BLOCKED" "1 persona ($personaExistente), 1 jugador" "$($altaExistente.Codigo); $cuentasExistente" $motivoBloqueo
        Verificar $g "DNI-1b" "La persona existente no se duplicó ni se modificó" ($personaDespues -eq "1|$personaExistente") "1|$personaExistente" $personaDespues
    } else {
        Verificar $g "DNI-1" "Alta con un DNI cuya persona ya existe: misma persona, un jugador nuevo" `
            ($altaExistente.Codigo -in 200, 201 -and $personaDespues -eq "1|$personaExistente" -and $cuentasExistente.StartsWith("1|1|")) "1|$personaExistente; 1 jugador" "$($altaExistente.Codigo); $personaDespues; $cuentasExistente" $altaExistente.Cuerpo
    }

    $g = "HU-056 reintento"
    $reintentoAlta = Alta "98590005" "nueva"
    $cuentasReintento = Cuentas "98590005"
    if (-not $altaDisponible) {
        Registrar $g "REA-1" "Repetir el alta no duplica persona, jugador, cuota ni inscripción" "BLOCKED" "200 creado=false; mismas cuentas" "$($reintentoAlta.Codigo); $cuentasReintento" $motivoBloqueo
    } else {
        Verificar $g "REA-1" "Repetir el alta no duplica persona, jugador, cuota ni inscripción" `
            ($reintentoAlta.Codigo -eq 200 -and (Json $reintentoAlta.Cuerpo).creado -eq $false -and $cuentasReintento -eq $cuentasAlta) "200 creado=false; $cuentasAlta" "$($reintentoAlta.Codigo); $cuentasReintento" $reintentoAlta.Cuerpo
    }

    # ============================================================ HU-057 baja por el contrato (lo que el backend del Deportivo tiene que llamar)
    $g = "HU-057 baja (contrato)"
    $f1 = $ids["98590001"]
    $historiaF1 = Sql "SELECT STRING_AGG(CONCAT(PK_id_pago, ':', CONVERT(CHAR(10), fecha_vencimiento, 23), ':', monto_final, ':', estado), '|') FROM PAGOS WHERE FK_id_jugador = $f1"
    $baja = Pedir "POST" "$urlAdministrativo/api/integracion/v1/jugadores/$f1/baja" $token $credencial @{ fechaBaja = "2026-09-20" }
    $filaF1 = Sql "SELECT CONCAT(j.PK_id_jugador, '|', p.dni, '|', CONVERT(CHAR(10), j.fecha_baja, 23), '|', CAST(j.activo AS INT)) FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE j.PK_id_jugador = $f1"
    Verificar $g "BAJ-1" "Baja informada con fecha efectiva 2026-09-20: 200 y baja lógica (el jugador y su persona siguen)" `
        ($baja.Codigo -eq 200 -and (Json $baja.Cuerpo).yaEstabaDadoDeBaja -eq $false -and $filaF1 -eq "$f1|98590001|2026-09-20|0") "200; $f1|98590001|2026-09-20|0" "$($baja.Codigo); $filaF1" $baja.Cuerpo
    $eventoF1 = Sql "SELECT CONCAT(COUNT(*), '|', MIN(origen_sistema), '|', MIN(id_usuario), '|', MIN(accion)) FROM AUDITORIA_CAMBIOS WHERE entidad = N'JUGADORES' AND id_entidad = '$f1' AND PK_id_evento > $auditoriaInicial AND JSON_VALUE(datos_despues, '$.fecha_baja') = '2026-09-20' AND JSON_VALUE(datos_antes, '$.fecha_baja') IS NULL"
    Verificar $g "BAJ-2" "Auditoría: un evento UPDATE con origen PortalDeportivo y el operador del token" ($eventoF1 -eq "1|PortalDeportivo|$idSuperAdmin|UPDATE") "1|PortalDeportivo|$idSuperAdmin|UPDATE" $eventoF1
    $reintentoBaja = Pedir "POST" "$urlAdministrativo/api/integracion/v1/jugadores/$f1/baja" $token $credencial @{ fechaBaja = "2026-09-20" }
    $eventosF1 = Sql "SELECT COUNT(*) FROM AUDITORIA_CAMBIOS WHERE entidad = N'JUGADORES' AND id_entidad = '$f1' AND PK_id_evento > $auditoriaInicial"
    Verificar $g "BAJ-3" "Reintento de la misma baja: 200 yaEstabaDadoDeBaja, sin un segundo evento" `
        ($reintentoBaja.Codigo -eq 200 -and (Json $reintentoBaja.Cuerpo).yaEstabaDadoDeBaja -eq $true -and $eventosF1 -eq "1") "200 true; 1 evento" "$($reintentoBaja.Codigo) $((Json $reintentoBaja.Cuerpo).yaEstabaDadoDeBaja); $eventosF1 evento(s)"
    $otraFecha = Pedir "POST" "$urlAdministrativo/api/integracion/v1/jugadores/$f1/baja" $token $credencial @{ fechaBaja = "2026-09-25" }
    Verificar $g "BAJ-4" "Otra fecha sobre una baja registrada: 409 CONFLICT, se conserva la primera" `
        ($otraFecha.Codigo -eq 409 -and (Sql "SELECT CONVERT(CHAR(10), fecha_baja, 23) FROM JUGADORES WHERE PK_id_jugador = $f1") -eq "2026-09-20") "409; 2026-09-20" "$($otraFecha.Codigo)"
    $historiaF1Despues = Sql "SELECT STRING_AGG(CONCAT(PK_id_pago, ':', CONVERT(CHAR(10), fecha_vencimiento, 23), ':', monto_final, ':', estado), '|') FROM PAGOS WHERE FK_id_jugador = $f1"

    # ============================================================ HU-057 baja iniciada en la API del Deportivo
    $g = "HU-057 baja (desde el Deportivo)"
    $f2 = $ids["98590002"]
    $historiaF2 = Sql "SELECT STRING_AGG(CONCAT(PK_id_pago, ':', CONVERT(CHAR(10), fecha_vencimiento, 23), ':', monto_final, ':', estado), '|') FROM PAGOS WHERE FK_id_jugador = $f2"
    $bajaDep = Pedir "DELETE" "$urlDeportivo/api/jugadores/$f2" $token $null $null
    $filaF2 = Sql "SELECT CONCAT(COUNT(*), '|', ISNULL(CONVERT(VARCHAR(10), MAX(fecha_baja), 23), 'NULL')) FROM JUGADORES WHERE PK_id_jugador = $f2"
    Verificar $g "BJD-1" "DELETE /api/jugadores/{id} del Deportivo: baja lógica (el jugador sigue, con fecha de baja)" ($bajaDep.Codigo -eq 200 -and $filaF2 -eq "1|$($hoy.ToString('yyyy-MM-dd'))") "200; 1|hoy" "$($bajaDep.Codigo); $filaF2" $bajaDep.Cuerpo
    $eventoF2 = Sql "SELECT CONCAT(COUNT(*), '|', ISNULL(MIN(origen_sistema), 'NULL')) FROM AUDITORIA_CAMBIOS WHERE entidad = N'JUGADORES' AND id_entidad = '$f2' AND PK_id_evento > $auditoriaInicial AND JSON_VALUE(datos_antes, '$.fecha_baja') IS NULL AND JSON_VALUE(datos_despues, '$.fecha_baja') IS NOT NULL"
    Verificar $g "BJD-2" "La baja la procesa el Administrativo por el contrato HU-057 (auditoría con origen PortalDeportivo)" ($eventoF2 -eq "1|PortalDeportivo") "1|PortalDeportivo" $eventoF2 `
        "Si el origen es NULL, el Deportivo escribió fecha_baja directo en la base compartida en lugar de llamar a POST /api/integracion/v1/jugadores/{id}/baja."
    $reintentoDep = Pedir "DELETE" "$urlDeportivo/api/jugadores/$f2" $token $null $null
    $eventosF2 = Sql "SELECT COUNT(*) FROM AUDITORIA_CAMBIOS WHERE entidad = N'JUGADORES' AND id_entidad = '$f2' AND PK_id_evento > $auditoriaInicial"
    Verificar $g "BJD-3" "Reintento de la baja desde el Deportivo: sin segunda baja ni otro evento" ($reintentoDep.Codigo -eq 200 -and $eventosF2 -eq "1" -and (Sql "SELECT COUNT(*) FROM JUGADORES WHERE PK_id_jugador = $f2") -eq "1") "200; 1 evento" "$($reintentoDep.Codigo); $eventosF2 evento(s)"

    # ============================================================ HU-057 deuda histórica
    $g = "HU-057 deuda histórica"
    $historiaF2Despues = Sql "SELECT STRING_AGG(CONCAT(PK_id_pago, ':', CONVERT(CHAR(10), fecha_vencimiento, 23), ':', monto_final, ':', estado), '|') FROM PAGOS WHERE FK_id_jugador = $f2"
    Verificar $g "DEU-1" "Las cuotas anteriores (agosto y septiembre) siguen iguales después de la baja (contrato y Deportivo)" `
        ($historiaF1Despues -eq $historiaF1 -and $historiaF2Despues -eq $historiaF2 -and $historiaF1 -match '2026-08-01.*2026-09-01') "sin cambios" $(if ($historiaF1Despues -eq $historiaF1 -and $historiaF2Despues -eq $historiaF2) { "sin cambios" } else { "F1: $historiaF1Despues / F2: $historiaF2Despues" })
    $pendientes = Pedir "GET" "$urlAdministrativo/api/pagos/pendientes" $token $null $null
    $lista = @(Json $pendientes.Cuerpo)
    $deudorF1 = $lista | Where-Object { $_.idJugador -eq $f1 }
    $deudorF2 = $lista | Where-Object { $_.idJugador -eq $f2 }
    Verificar $g "DEU-2" "Deudas y Morosidad sigue mostrando a los dos dados de baja con sus 2 cuotas y la fecha de baja" `
        ($pendientes.Codigo -eq 200 -and $deudorF1.cantidadCuotas -eq 2 -and $deudorF1.montoTotal -eq 2000 -and $deudorF1.fechaBaja -and $deudorF2.cantidadCuotas -eq 2 -and $deudorF2.fechaBaja) `
        "F1 y F2: 2 cuotas, con fechaBaja" "F1: $($deudorF1.cantidadCuotas) $($deudorF1.fechaBaja) | F2: $($deudorF2.cantidadCuotas) $($deudorF2.fechaBaja)"
    $estadoF1 = Pedir "GET" "$urlAdministrativo/api/integracion/v1/estado-deuda/dni/98590001" $token $credencial $null
    Verificar $g "DEU-3" "estado-deuda informa 'baja' al Deportivo (QR)" ($estadoF1.Codigo -eq 200 -and (Json $estadoF1.Cuerpo).estado -eq "baja") "200 baja" "$($estadoF1.Codigo) $((Json $estadoF1.Cuerpo).estado)"

    # ============================================================ generación de cuotas después de la baja
    $g = "HU-057 generación de cuotas"
    function CuotasPeriodo([int]$Id, [string]$Periodo = $periodoTxt) { Sql "SELECT COUNT(*) FROM PAGOS WHERE FK_id_jugador = $Id AND concepto = 'Cuota' AND fecha_vencimiento = '$Periodo'" }
    $f2Elegible = $hoy.Month -eq $periodoActual.Month   # F2 se dio de baja hoy: el mes de la baja le corresponde (estuvo parte del mes)
    # F5 se da de baja (por el contrato) con fecha 15/07: agosto y septiembre son meses CERRADOS posteriores a su baja, que la
    # generación manual de faltantes (solo meses cerrados; el mes en curso lo emite el generador) puede intentar emitir.
    $f5 = $ids["98590006"]; $f3 = $ids["98590003"]
    $bajaF5 = Pedir "POST" "$urlAdministrativo/api/integracion/v1/jugadores/$f5/baja" $token $credencial @{ fechaBaja = "2026-07-15" }
    $faltantes = Pedir "GET" "$urlAdministrativo/api/cuotas-faltantes/2026/9" $token $null $null
    $dnisFaltantes = @((Json $faltantes.Cuerpo) | ForEach-Object { $_.dni })
    Verificar $g "GEN-1" "Detección de faltantes de septiembre (mes cerrado): incluye al activo de control y no al dado de baja el 15/07" `
        ($bajaF5.Codigo -eq 200 -and $faltantes.Codigo -eq 200 -and "98590003" -in $dnisFaltantes -and "98590006" -notin $dnisFaltantes) "baja 200; incluye 98590003, no 98590006" "baja $($bajaF5.Codigo); $($dnisFaltantes -join ',')" $faltantes.Cuerpo
    $manualAgo = Pedir "POST" "$urlAdministrativo/api/cuotas-faltantes/2026/8/generar" $token $null $null
    $manualSep = Pedir "POST" "$urlAdministrativo/api/cuotas-faltantes/2026/9/generar" $token $null $null
    $manualControl = "$(CuotasPeriodo $f3 '2026-08-01')|$(CuotasPeriodo $f3 '2026-09-01')"
    $manualBaja = "$(CuotasPeriodo $f5 '2026-08-01')|$(CuotasPeriodo $f5 '2026-09-01')"
    Verificar $g "GEN-1b" "Generación manual de faltantes (agosto y septiembre): el activo las recibe y el dado de baja no" `
        ($manualAgo.Codigo -eq 200 -and $manualSep.Codigo -eq 200 -and $manualControl -eq "1|1" -and $manualBaja -eq "0|0") "control 1|1, baja 0|0" "$($manualAgo.Codigo)/$($manualSep.Codigo); control $manualControl, baja $manualBaja"
    Verificar $g "GEN-1c" "La deuda anterior del dado de baja (julio) sigue pendiente" ((CuotasPeriodo $f5 '2026-07-01') -eq "1") "1" (CuotasPeriodo $f5 '2026-07-01')
    # Generador automático (HU-049): se reinicia el Administrativo con la generación habilitada y se espera (polling) a que emita.
    Detener $administrativo | Out-Null
    $administrativo = Iniciar "administrativo_generador" $AdministrativoDll $PuertoAdministrativo (EntornoAdministrativo $true)
    $generado = Esperar "generador" { [pscustomobject]@{ Ok = ((CuotasPeriodo $ids["98590003"]) -eq "1"); Detalle = "cuota de control = $(CuotasPeriodo $ids['98590003'])" } } $administrativo
    $relogin = Esperar "Administrativo" { Login } $administrativo
    $token = $relogin.Token
    Verificar $g "GEN-2" "Generador automático: emite la cuota de $periodoTxt al activo y NO a los dados de baja (20/09 y 15/07)" `
        ($generado.Ok -and (CuotasPeriodo $f1) -eq "0" -and (CuotasPeriodo $f5) -eq "0") "control 1, bajas 0|0" "control $(CuotasPeriodo $f3), bajas $(CuotasPeriodo $f1)|$(CuotasPeriodo $f5)" $generado.Detalle
    # Emisión manual de un mes futuro: cobrar noviembre por adelantado emite la cuota primero (PagosService); a un dado de baja no.
    $adelantado = Pedir "POST" "$urlAdministrativo/api/pagos/registrar" $token $null @{ idJugador = $f1; periodo = "Noviembre"; anio = 2026; monto = 1000; metodoPago = "Efectivo" }
    $noviembre = Sql "SELECT COUNT(*) FROM PAGOS WHERE FK_id_jugador = $f1 AND fecha_vencimiento = '2026-11-01'"
    Verificar $g "GEN-3" "Cobro por adelantado de noviembre a un dado de baja: rechazado (400) y sin cuota emitida" `
        ($adelantado.Codigo -eq 400 -and $noviembre -eq "0") "400; 0 filas" "$($adelantado.Codigo); $noviembre filas" $adelantado.Cuerpo
    $posteriores = Sql "SELECT CONCAT((SELECT COUNT(*) FROM PAGOS WHERE FK_id_jugador = $f1 AND fecha_vencimiento > '2026-09-01'), '|', (SELECT COUNT(*) FROM PAGOS WHERE FK_id_jugador = $f5 AND fecha_vencimiento > '2026-07-01'))"
    Verificar $g "GEN-4" "Ninguna cuota posterior al mes de la baja para los dados de baja por contrato" ($posteriores -eq "0|0") "0|0" $posteriores
    $cuotaF2 = CuotasPeriodo $f2
    Verificar $g "GEN-5" "El dado de baja hoy desde el Deportivo conserva solo lo que le corresponde por la regla del mes de la baja" `
        ($cuotaF2 -eq $(if ($f2Elegible) { "1" } else { "0" })) $(if ($f2Elegible) { "1 (mes de la baja)" } else { "0" }) $cuotaF2
}
catch {
    Registrar "Entorno" "ENV-X" "La ejecución se interrumpió" "BLOCKED" "sin error técnico" "error" $_.Exception.Message
}
finally {
    $logs = foreach ($api in $procesos) { Detener $api }
    $textoLogs = $logs -join "`n"
    $secretos = ([regex]::Matches($textoLogs, [regex]::Escape($credencial))).Count + ([regex]::Matches($textoLogs, [regex]::Escape($claveJwt))).Count +
        ([regex]::Matches($textoLogs, 'eyJ[A-Za-z0-9_-]{20,}')).Count
    Verificar "Logs" "LOG-1" "Los logs de las dos APIs no contienen la credencial, la clave JWT ni tokens" ($secretos -eq 0) 0 $secretos
}

# ---------------------------------------------------------------- resumen
$duracion = [int]((Get-Date) - $inicioCorrida).TotalSeconds
Write-Host "`n== Resumen por historia" -ForegroundColor Cyan
$grupos = $resultados | Group-Object Grupo | ForEach-Object {
    $estados = $_.Group.Estado
    $estado = if ($estados -contains "FAIL") { "FAIL" } elseif ($estados -contains "BLOCKED") { "BLOCKED" } else { "PASS" }
    [pscustomobject]@{ Grupo = $_.Name; Estado = $estado; Casos = $_.Count }
}
foreach ($grupo in $grupos) { Write-Host ("{0}: {1}" -f $grupo.Grupo, $grupo.Estado) -ForegroundColor @{ PASS = "Green"; FAIL = "Red"; BLOCKED = "Yellow" }[$grupo.Estado] }
$pass = @($resultados | Where-Object Estado -eq "PASS").Count
$fail = @($resultados | Where-Object Estado -eq "FAIL").Count
$blocked = @($resultados | Where-Object Estado -eq "BLOCKED").Count
Write-Host "`nTotal: $pass PASS | $fail FAIL | $blocked BLOCKED   ($duracion s, hoy $($hoy.ToString('yyyy-MM-dd')), base $Base)"

$informe = [pscustomobject]@{
    fecha = (Get-Date).ToString("s"); hoyClub = $hoy.ToString("yyyy-MM-dd"); duracionSegundos = $duracion; base = $Base; servidor = $Servidor
    administrativoDll = $AdministrativoDll; deportivoDll = $DeportivoDll
    totales = [pscustomobject]@{ pass = $pass; fail = $fail; blocked = $blocked }; historias = $grupos; casos = $resultados
}
$json = $informe | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $Salida "resultado.json"), $json, [Text.UTF8Encoding]::new($false))
if ($Evidencia) { [IO.File]::WriteAllText($Evidencia, $json, [Text.UTF8Encoding]::new($false)) }
Write-Host "Evidencia: $Salida"
if ($fail -gt 0 -or $blocked -gt 0) { exit 1 }
exit 0
