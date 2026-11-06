<#
.SYNOPSIS
    HU-056: pruebas de punta a punta del cambio de categoría de cobro. El Deportivo lo pide (PUT /api/jugadores/{id}/categoria)
    y el Administrativo lo resuelve (PUT /api/integracion/v1/jugadores/{id}/categoria).

.DESCRIPTION
    Corre sobre el entorno compartido de HU-059: recrea la base de prueba (reset_base.ps1), carga las precondiciones de
    datos/04_fixtures_hu056.sql, levanta las dos APIs y verifica lo persistido con SQL de solo lectura. Reemplaza la prueba
    manual del PUT, que no quedó guardada y terminaba con código 1 en la limpieza: acá no hay limpieza que pueda fallar,
    porque la base se recrea al empezar y los procesos se detienen en el finally.

    Códigos de salida: 0 todo PASS; 1 algún FAIL o BLOCKED; 2 el entorno no está disponible.

.EXAMPLE
    .\qa\HU-056\e2e_hu056_categoria.ps1 -Evidencia qa\HU-056\evidencia_categoria1.json
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
    # Clave de las cuentas FICTICIAS de qa/HU-059/datos/01_cuentas_prueba.sql (publicada en cacc-unificacion; no es un secreto real).
    [string]$ClaveCuentasPrueba = $(if ($env:CACC_E2E_CLAVE_CUENTAS) { $env:CACC_E2E_CLAVE_CUENTAS } else { "Camioneros.Test.2026!" }),
    [int]$TimeoutSegundos = 90,
    [string]$Salida = $(Join-Path $env:TEMP ("cacc-hu056\ejecucion_{0:yyyyMMdd_HHmmss}" -f (Get-Date))),
    [string]$Evidencia = ""
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (-not $AdministrativoDll) { $AdministrativoDll = Join-Path $repo "ApiGestion\bin\Debug\net9.0\ApiGestion.dll" }
if (-not $DeportivoDll) { $DeportivoDll = Join-Path $env:TEMP "cacc-hu059\trabajo\ellos\BackEnd\CACC.API\bin\Debug\net9.0\CACC.API.dll" }
New-Item -ItemType Directory -Force -Path $Salida | Out-Null
Add-Type -AssemblyName System.Net.Http
$inicioCorrida = Get-Date

function Fatal([string]$Mensaje) { Write-Host "ENTORNO NO DISPONIBLE: $Mensaje" -ForegroundColor Red; exit 2 }
foreach ($archivo in @($AdministrativoDll, $DeportivoDll)) { if (-not (Test-Path $archivo)) { Fatal "no encuentro $archivo (ver 'Arranque' en database/InstructivoEquipo.md, §7.2)." } }
if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) { Fatal "falta sqlcmd en el PATH." }

. (Join-Path $repo "qa\HU-059\comun.ps1")

try {
    Write-Host "== Reset de la base $Base (HU-059)" -ForegroundColor Cyan
    & (Join-Path $repo "qa\HU-059\reset_base.ps1") -Servidor $Servidor -Base $Base -AdministrativoDll $AdministrativoDll -Salida $Salida
    $texto = & sqlcmd -S $Servidor -E -C -b -I -W -h -1 -f 65001 -d $Base -i (Join-Path $PSScriptRoot "datos\04_fixtures_hu056.sql") 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "04_fixtures_hu056.sql: $texto" }
    Write-Host "  Precondiciones de HU-056: $($texto.Trim())"
} catch { Fatal "la preparación de la base falló: $($_.Exception.Message)" }

function IdJugador([string]$Dni) { Sql "SELECT ISNULL(MAX(j.PK_id_jugador), 0) FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni = '$Dni'" }
# Categoría de cobro | categorías de pertenencia (N:M) | fecha de baja.
function Estado([int]$IdJug) {
    Sql "SELECT CONCAT(j.FK_id_categoria, '|', (SELECT STRING_AGG(CONVERT(VARCHAR(10), FK_id_categoria), ',') WITHIN GROUP (ORDER BY FK_id_categoria) FROM JUGADORES_CATEGORIAS WHERE FK_id_jugador = j.PK_id_jugador), '|', ISNULL(CONVERT(CHAR(10), j.fecha_baja, 23), '-'))
         FROM JUGADORES j WHERE j.PK_id_jugador = $IdJug"
}
# Huella del historial financiero del jugador: cada cargo con su monto, estado, período y pago.
function Historial([int]$IdJug) {
    Sql "SELECT ISNULL(STRING_AGG(CONCAT(PK_id_pago, ':', concepto, ':', CONVERT(VARCHAR(20), monto_base), ':', CONVERT(VARCHAR(20), monto_final), ':', estado, ':', CONVERT(CHAR(10), fecha_vencimiento, 23), ':', ISNULL(CONVERT(CHAR(10), fecha_pago, 23), '-')), ',') WITHIN GROUP (ORDER BY PK_id_pago), '')
         FROM PAGOS WHERE FK_id_jugador = $IdJug"
}
function AuditoriasJugador([int]$IdJug) { Sql "SELECT COUNT(*) FROM AUDITORIA_CAMBIOS WHERE entidad = 'JUGADORES' AND id_entidad = $IdJug" }
function Rechazos { [int](Sql "SELECT COUNT(*) FROM INTEGRACION_RECHAZOS") }
function UltimoRechazo { Sql "SELECT TOP (1) CONCAT(operacion, '|', error_code, '|', origen_sistema, '|', ISNULL(CONVERT(VARCHAR(10), id_jugador), '-')) FROM INTEGRACION_RECHAZOS ORDER BY PK_id_rechazo DESC" }
function PutDeportivo([int]$IdJug, $Categoria) { Pedir "PUT" "$urlDeportivo/api/jugadores/$IdJug/categoria" $token $null @{ idCategoria = $Categoria } }
function Resumen($Respuesta) { "$($Respuesta.Codigo) $($Respuesta.Cuerpo)" }

$idSuperAdmin = [int](Sql "SELECT u.PK_id_usuario FROM USUARIOS u JOIN PERSONAS p ON p.PK_id_persona = u.FK_id_persona WHERE p.dni = '20111222'")
$idActivo = [int](IdJugador "98560001")
$idBaja = [int](IdJugador "98560002")
$idInexistente = [int](Sql "SELECT ISNULL(MAX(PK_id_jugador), 0) + 100000 FROM JUGADORES")
$ids = [ordered]@{ superAdmin = $idSuperAdmin; jugadorActivo98560001 = $idActivo; jugadorBaja98560002 = $idBaja; jugadorInexistente = $idInexistente }

$administrativo = $null; $deportivo = $null
try {
    Write-Host "== Levantando las APIs" -ForegroundColor Cyan
    $administrativo = Iniciar "administrativo" $AdministrativoDll $PuertoAdministrativo (EntornoAdministrativo $false)
    $deportivo = Iniciar "deportivo" $DeportivoDll $PuertoDeportivo (EntornoDeportivo $credencial)
    Verificar "Entorno" "ENV-1" "SQL Server responde y la base tiene las precondiciones 9856xxxx" ($idActivo -gt 0 -and $idBaja -gt 0) "2 jugadores" "activo $idActivo, baja $idBaja"
    $saludAdm = Esperar "Administrativo" { Login } $administrativo
    Verificar "Entorno" "ENV-2" "API Administrativa lista (login: lee USUARIOS en SQL)" $saludAdm.Ok "200" $saludAdm.Detalle
    if (-not $saludAdm.Ok) { throw "La API Administrativa no quedó lista: $($saludAdm.Detalle)" }
    $token = $saludAdm.Token
    $saludDep = Esperar "Deportivo" { $r = Pedir "GET" "$urlDeportivo/api/jugadores" $token $null $null; [pscustomobject]@{ Ok = ($r.Codigo -eq 200); Detalle = "GET /api/jugadores -> $($r.Codigo)" } } $deportivo
    Verificar "Entorno" "ENV-3" "API Deportiva lista (GET /api/jugadores: lee JUGADORES en SQL)" $saludDep.Ok "200" $saludDep.Detalle
    if (-not $saludDep.Ok) { throw "La API Deportiva no quedó lista: $($saludDep.Detalle)" }

    $g = "Cambio de categoría"
    $historialInicial = Historial $idActivo
    Verificar "Entorno" "ENV-4" "El jugador activo arranca en la cat. 3 con historial (1 pagado, 1 pendiente)" `
        ((Estado $idActivo) -eq "3|3|-" -and @($historialInicial -split ',').Count -eq 2) "3|3|-; 2 cargos" "$(Estado $idActivo); $historialInicial"

    # PUT-1: el cambio real, pedido por el Deportivo.
    $audAntes = [int](AuditoriasJugador $idActivo)
    $cambio = PutDeportivo $idActivo 4
    $c = Json $cambio.Cuerpo
    $estado1 = Estado $idActivo
    $aud = Sql "SELECT COUNT(*) FROM AUDITORIA_CAMBIOS WHERE entidad = 'JUGADORES' AND accion = 'UPDATE' AND id_entidad = $idActivo AND origen_sistema = 'PortalDeportivo' AND id_usuario = $idSuperAdmin"
    Verificar $g "PUT-1" "Deportivo PUT categoría 3 -> 4: 200 cambiado; cobro en 4, pertenencia conserva 3 y suma 4; auditado PortalDeportivo con el operador" `
        ($cambio.Codigo -eq 200 -and $c.idJugador -eq $idActivo -and $c.idCategoria -eq 4 -and $c.cambiado -eq $true -and $estado1 -eq "4|3,4|-" -and [int]$aud -ge 1) `
        "200; cambiado; 4|3,4|-; >=1 UPDATE auditado" "$($cambio.Codigo); cambiado $($c.cambiado); $estado1; $aud auditado(s)" $cambio.Cuerpo

    # PUT-2: el historial financiero no se toca (ni montos, ni estados, ni períodos, ni pagos).
    $historial1 = Historial $idActivo
    Verificar $g "PUT-2" "El cambio no modifica el historial financiero (cuota pagada y pendiente intactas)" ($historial1 -eq $historialInicial) $historialInicial $historial1

    # PUT-3: repetir el mismo pedido es idempotente: 200 sin cambio y sin nueva escritura auditada.
    $audDespues1 = [int](AuditoriasJugador $idActivo)
    $repetido = PutDeportivo $idActivo 4
    $r = Json $repetido.Cuerpo
    Verificar $g "PUT-3" "Repetir el mismo cambio: 200 cambiado=false, sin escrituras nuevas ni cambios" `
        ($repetido.Codigo -eq 200 -and $r.cambiado -eq $false -and (Estado $idActivo) -eq "4|3,4|-" -and [int](AuditoriasJugador $idActivo) -eq $audDespues1 -and (Historial $idActivo) -eq $historialInicial) `
        "200; cambiado=false; 4|3,4|-; auditoría $audDespues1" "$($repetido.Codigo); cambiado $($r.cambiado); $(Estado $idActivo); auditoría $(AuditoriasJugador $idActivo)" $repetido.Cuerpo

    # PUT-4: categoría inexistente: 422 en las dos APIs, rechazo registrado sin datos personales, nada cambia.
    $rechazosAntes = Rechazos
    $inexistente = PutDeportivo $idActivo 999999
    Verificar $g "PUT-4" "Categoría inexistente: 422, rechazo CambioCategoria registrado con origen PortalDeportivo, nada cambia" `
        ($inexistente.Codigo -eq 422 -and (Rechazos) -eq $rechazosAntes + 1 -and (UltimoRechazo) -eq "CambioCategoria|UNPROCESSABLE|PortalDeportivo|$idActivo" -and (Estado $idActivo) -eq "4|3,4|-" -and (Historial $idActivo) -eq $historialInicial) `
        "422; +1 rechazo CambioCategoria|UNPROCESSABLE|PortalDeportivo|$idActivo; 4|3,4|-" "$($inexistente.Codigo); rechazos $rechazosAntes -> $(Rechazos); $(UltimoRechazo); $(Estado $idActivo)" $inexistente.Cuerpo

    # PUT-5: categoría inválida (0): el Deportivo la rechaza antes de llamar; el contrato también la rechaza con field.
    $invalidaDep = PutDeportivo $idActivo 0
    $invalidaAdm = Pedir "PUT" "$urlAdministrativo/api/integracion/v1/jugadores/$idActivo/categoria" $token $credencial @{ idCategoria = 0 }
    $ia = Json $invalidaAdm.Cuerpo
    Verificar $g "PUT-5" "Categoría 0: 400 en el Deportivo y 400 INVALID_INPUT (field idCategoria) en el contrato; nada cambia" `
        ($invalidaDep.Codigo -eq 400 -and $invalidaAdm.Codigo -eq 400 -and $ia.errorCode -eq "INVALID_INPUT" -and $ia.field -eq "idCategoria" -and (Estado $idActivo) -eq "4|3,4|-") `
        "400 / 400 INVALID_INPUT idCategoria" "$($invalidaDep.Codigo) / $($invalidaAdm.Codigo) $($ia.errorCode) $($ia.field)"

    # PUT-6: jugador inexistente: 404 NOT_FOUND en el contrato y 404 en el Deportivo (no 503: ver BUG-059-3).
    $noExisteAdm = Pedir "PUT" "$urlAdministrativo/api/integracion/v1/jugadores/$idInexistente/categoria" $token $credencial @{ idCategoria = 4 }
    $noExisteDep = PutDeportivo $idInexistente 4
    Verificar $g "PUT-6" "Jugador inexistente: 404 NOT_FOUND en el contrato y 404 en el Deportivo" `
        ($noExisteAdm.Codigo -eq 404 -and (Json $noExisteAdm.Cuerpo).errorCode -eq "NOT_FOUND" -and $noExisteDep.Codigo -eq 404) "404 NOT_FOUND / 404" "$($noExisteAdm.Codigo) $((Json $noExisteAdm.Cuerpo).errorCode) / $($noExisteDep.Codigo)" (Resumen $noExisteDep)

    # PUT-7: jugador dado de baja: 422 y no se le cambia la categoría.
    $estadoBaja = Estado $idBaja
    $baja = PutDeportivo $idBaja 4
    Verificar $g "PUT-7" "Jugador dado de baja: 422 y la categoría no cambia" ($baja.Codigo -eq 422 -and (Estado $idBaja) -eq $estadoBaja -and $estadoBaja -eq "3|3|2026-09-30") `
        "422; 3|3|2026-09-30" "$($baja.Codigo); $(Estado $idBaja)" $baja.Cuerpo

    # PUT-8: HU-055: sin credencial o con una inválida, 401 y nada cambia.
    $sinClave = Pedir "PUT" "$urlAdministrativo/api/integracion/v1/jugadores/$idActivo/categoria" $token $null @{ idCategoria = 3 }
    $claveMala = Pedir "PUT" "$urlAdministrativo/api/integracion/v1/jugadores/$idActivo/categoria" $token ("x" * 44) @{ idCategoria = 3 }
    Verificar $g "PUT-8" "Contrato sin credencial o con una inválida: 401 y nada cambia" `
        ($sinClave.Codigo -eq 401 -and $claveMala.Codigo -eq 401 -and (Estado $idActivo) -eq "4|3,4|-") "401/401; 4|3,4|-" "$($sinClave.Codigo)/$($claveMala.Codigo); $(Estado $idActivo)"

    # PUT-9: volver a la categoría anterior también funciona y la pertenencia no se duplica.
    $vuelta = PutDeportivo $idActivo 3
    Verificar $g "PUT-9" "Volver a la cat. 3: 200 cambiado; cobro en 3, pertenencia 3,4 sin duplicados; historial intacto" `
        ($vuelta.Codigo -eq 200 -and (Json $vuelta.Cuerpo).cambiado -eq $true -and (Estado $idActivo) -eq "3|3,4|-" -and (Historial $idActivo) -eq $historialInicial) `
        "200; 3|3,4|-" "$($vuelta.Codigo); $(Estado $idActivo)" $vuelta.Cuerpo
}
catch {
    Registrar "Entorno" "ENV-X" "La ejecución se interrumpió" "BLOCKED" "sin error técnico" "error" "$($_.Exception.Message) @ línea $($_.InvocationInfo.ScriptLineNumber)"
}
finally {
    $logs = foreach ($api in $procesos) { Detener $api }
    $textoLogs = $logs -join "`n"
    $secretos = ([regex]::Matches($textoLogs, [regex]::Escape($credencial))).Count + ([regex]::Matches($textoLogs, [regex]::Escape($claveJwt))).Count +
        ([regex]::Matches($textoLogs, 'eyJ[A-Za-z0-9_-]{20,}')).Count
    Verificar "Logs" "LOG-1" "Los logs de las dos APIs no contienen la credencial, la clave JWT ni tokens" ($secretos -eq 0) 0 $secretos
}

$duracion = [int]((Get-Date) - $inicioCorrida).TotalSeconds
$pass = @($resultados | Where-Object Estado -eq "PASS").Count
$fail = @($resultados | Where-Object Estado -eq "FAIL").Count
$blocked = @($resultados | Where-Object Estado -eq "BLOCKED").Count
Write-Host "`nTotal: $pass PASS | $fail FAIL | $blocked BLOCKED   ($duracion s, base $Base)"

$informe = [pscustomobject]@{
    fecha = (Get-Date).ToString("s"); duracionSegundos = $duracion; base = $Base; servidor = $Servidor
    administrativoDll = $AdministrativoDll; deportivoDll = $DeportivoDll; ids = $ids
    totales = [pscustomobject]@{ pass = $pass; fail = $fail; blocked = $blocked }; casos = $resultados
}
$json = $informe | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $Salida "resultado.json"), $json, [Text.UTF8Encoding]::new($false))
if ($Evidencia) { [IO.File]::WriteAllText($Evidencia, $json, [Text.UTF8Encoding]::new($false)) }
Write-Host "Evidencia: $Salida"
if ($fail -gt 0 -or $blocked -gt 0) { exit 1 }
exit 0
