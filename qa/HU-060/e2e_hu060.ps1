<#
.SYNOPSIS
    HU-060: las altas del Portal Deportivo impactan correctamente en el Portal Administrativo. Pruebas de punta a punta sobre el
    entorno compartido de HU-059: las dos APIs reales, SQL Server real y la base unificada de prueba (HU-053).

.DESCRIPTION
    1. Reset de HU-059 (qa/HU-059/reset_base.ps1) + precondiciones de HU-060 (datos/03_fixtures_hu060.sql). Siempre el mismo estado.
    2. Levanta las dos APIs con clave JWT y credencial de integración generadas al azar en memoria (comun.ps1 de HU-059).
    3. Ejecuta cada alta por el flujo real: POST /api/jugadores del Deportivo, que llama a POST /api/integracion/v1/jugadores del
       Administrativo (HU-056). Valida la respuesta, lo que ve Tesorería por las APIs del Administrativo (padrón, búsqueda,
       cobranzas) y lo persistido (SQL SOLO para verificar: nunca INSERT/UPDATE como atajo).
    4. Los importes esperados NO están escritos en la prueba: salen de la base con la jerarquía de HU-048 (arancel de la categoría
       y, si no hay, el del género, vigente al último día del mes) y del arancel de inscripción vigente (HU-033).
    5. Si una dependencia no está (HU-056 en el Administrativo), el caso es BLOCKED con su evidencia: nunca PASS.
    Código de salida: 0 = todo PASS; 1 = algún FAIL o BLOCKED; 2 = el entorno no se pudo preparar.

    Fecha de negocio: la API no admite un reloj inyectable (RelojNegocio.Hoy = hora de Argentina). La suite la lee de SQL Server y
    calcula lo esperado para ESE día; la rama del calendario que no corresponde a hoy queda BLOCKED (ver QA-HU-060.md).

.EXAMPLE
    .\qa\HU-060\e2e_hu060.ps1 -Evidencia qa\HU-060\evidencia_ejecucion1.json
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
    # Fuentes del frontend del Deportivo, para comprobar que la credencial no llega al navegador.
    [string]$DeportivoFrontEnd = $(if ($env:CACC_E2E_DEPORTIVO_FRONTEND) { $env:CACC_E2E_DEPORTIVO_FRONTEND } else { Join-Path $env:TEMP "cacc-hu059\deportivo-afa4e60\FrontEnd" }),
    [int]$PuertoAdministrativo = $(if ($env:CACC_E2E_PUERTO_ADMINISTRATIVO) { [int]$env:CACC_E2E_PUERTO_ADMINISTRATIVO } else { 5418 }),
    [int]$PuertoDeportivo = $(if ($env:CACC_E2E_PUERTO_DEPORTIVO) { [int]$env:CACC_E2E_PUERTO_DEPORTIVO } else { 5419 }),
    # Clave de las cuentas FICTICIAS de qa/HU-059/datos/01_cuentas_prueba.sql (publicada en cacc-unificacion; no es un secreto real).
    [string]$ClaveCuentasPrueba = $(if ($env:CACC_E2E_CLAVE_CUENTAS) { $env:CACC_E2E_CLAVE_CUENTAS } else { "Camioneros.Test.2026!" }),
    [int]$TimeoutSegundos = 90,
    [string]$Salida = $(Join-Path $env:TEMP ("cacc-hu060\ejecucion_{0:yyyyMMdd_HHmmss}" -f (Get-Date))),
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

# ---------------------------------------------------------------- preparación (HU-059 + precondiciones de HU-060)
try {
    Write-Host "== Reset de la base $Base (HU-059)" -ForegroundColor Cyan
    & (Join-Path $repo "qa\HU-059\reset_base.ps1") -Servidor $Servidor -Base $Base -AdministrativoDll $AdministrativoDll -Salida $Salida
    $texto = & sqlcmd -S $Servidor -E -C -b -I -W -h -1 -f 65001 -d $Base -i (Join-Path $PSScriptRoot "datos\03_fixtures_hu060.sql") 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "03_fixtures_hu060.sql: $texto" }
    Write-Host "  Precondiciones de HU-060: $($texto.Trim())"
} catch { Fatal "la preparación de la base falló: $($_.Exception.Message)" }

# Fecha de negocio: el día del club según SQL Server (la misma regla que RelojNegocio/SqlReloj).
$hoy = [datetime]::ParseExact((Sql "SELECT CONVERT(CHAR(10), CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Argentina Standard Time' AS DATE), 23)"), "yyyy-MM-dd", $null)
$hoyTxt = $hoy.ToString("yyyy-MM-dd")
$mesActual = New-Object DateTime $hoy.Year, $hoy.Month, 1
$mesTxt = $mesActual.ToString("yyyy-MM-dd")
# HU-083: desde el día 10 la cuota del mes ya se generó para todos, así que el alta la emite; antes del 10 la recibe ese día.
$emiteCuota = $hoy.Day -ge 10
Write-Host "Fecha de negocio (club): $hoyTxt | ¿el alta emite la cuota de $($mesActual.ToString('MM/yyyy'))? $emiteCuota | salida: $Salida"

# ---------------------------------------------------------------- oráculos (leen la base: ningún importe escrito en la prueba)
# HU-048: el arancel de la categoría si tiene uno vigente al último día del mes; si no, el de su género. Vacío = sin arancel.
function CuotaEsperada([string]$Genero, [int]$Categoria) {
    Sql "SELECT TOP (1) CONVERT(VARCHAR(20), monto) FROM ARANCELES WHERE vigente_desde <= EOMONTH('$mesTxt')
         AND (FK_id_categoria = $Categoria OR (FK_id_categoria IS NULL AND genero = '$Genero'))
         ORDER BY CASE WHEN FK_id_categoria IS NULL THEN 1 ELSE 0 END, vigente_desde DESC"
}
# HU-033: la inscripción vigente de su género; sin arancel o en $0 no se cobra.
function InscripcionEsperada([string]$Genero) {
    $monto = Sql "SELECT TOP (1) CONVERT(VARCHAR(20), monto) FROM ARANCELES_INSCRIPCION WHERE genero = '$Genero' AND vigente_desde <= '$hoyTxt' ORDER BY vigente_desde DESC"
    if ($monto -and [decimal]$monto -gt 0) { $monto } else { "" }
}
# personas|jugadores|cuotas|inscripciones de un DNI.
function Cuentas([string]$Dni) {
    Sql "SELECT CONCAT((SELECT COUNT(*) FROM PERSONAS WHERE dni = '$Dni'), '|',
        (SELECT COUNT(*) FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni = '$Dni'), '|',
        (SELECT COUNT(*) FROM PAGOS g JOIN JUGADORES j ON j.PK_id_jugador = g.FK_id_jugador JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni = '$Dni' AND g.concepto = 'Cuota'), '|',
        (SELECT COUNT(*) FROM PAGOS g JOIN JUGADORES j ON j.PK_id_jugador = g.FK_id_jugador JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni = '$Dni' AND g.concepto = 'Inscripcion'))"
}
function IdJugador([string]$Dni) { Sql "SELECT ISNULL(MAX(j.PK_id_jugador), 0) FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni = '$Dni'" }
function Montos([int]$IdJug, [string]$Concepto) {
    Sql "SELECT ISNULL(STRING_AGG(CONCAT(CONVERT(VARCHAR(20), monto_base), '@', CONVERT(CHAR(10), fecha_vencimiento, 23)), ','), '') FROM PAGOS WHERE FK_id_jugador = $IdJug AND concepto = '$Concepto'"
}

# ---------------------------------------------------------------- el alta por el flujo real del Deportivo
function AltaDeportivo($Dni, [string]$Nombre, [string]$Apellido, [string]$Genero, $Categoria, [string]$Nacimiento = "2010-06-15") {
    $cuerpo = [ordered]@{ nombre = $Nombre; apellido = $Apellido; fechaNacimiento = $Nacimiento; genero = $Genero }
    if ($null -ne $Dni) { $cuerpo.dni = $Dni }
    if ($null -ne $Categoria) { $cuerpo.categoria = "$Categoria" }
    Pedir "POST" "$urlDeportivo/api/jugadores" $token $null $cuerpo
}
function Resumen($Respuesta) { "$($Respuesta.Codigo) $($Respuesta.Cuerpo)" }

# Un caso que necesita HU-056 en el Administrativo: sin ella es BLOCKED con la respuesta real como evidencia, nunca PASS.
function SiHu056([string]$Grupo, [string]$Id, [string]$Caso, $Respuesta, [scriptblock]$Verificar) {
    if (-not $hu056) {
        Registrar $Grupo $Id $Caso "BLOCKED" "HU-056 disponible" "HU-056 no disponible" "$motivoBloqueo. Respuesta del flujo: $(Resumen $Respuesta)"
        return
    }
    & $Verificar
}

$idSuperAdmin = [int](Sql "SELECT u.PK_id_usuario FROM USUARIOS u JOIN PERSONAS p ON p.PK_id_persona = u.FK_id_persona WHERE p.dni = '20111222'")
$personaPrevia = [int](Sql "SELECT PK_id_persona FROM PERSONAS WHERE dni = '98600002'")
$claveStaff = Azar 18   # contraseña de las cuentas de staff que se crean: al azar, nunca se imprime ni se guarda
$ids = [ordered]@{ superAdmin = $idSuperAdmin; personaPrevia98600002 = $personaPrevia; jugadorPrevio98600003 = [int](IdJugador "98600003") }

$administrativo = $null; $deportivo = $null
try {
    Write-Host "== Levantando las APIs" -ForegroundColor Cyan
    $administrativo = Iniciar "administrativo" $AdministrativoDll $PuertoAdministrativo (EntornoAdministrativo $false)
    $deportivo = Iniciar "deportivo" $DeportivoDll $PuertoDeportivo (EntornoDeportivo $credencial)
    $saludSql = Esperar "SQL" { [pscustomobject]@{ Ok = ((Sql "SELECT COUNT(*) FROM PERSONAS WHERE dni LIKE '9860____'") -eq "2"); Detalle = "precondiciones 9860xxxx" } } $null
    Verificar "Entorno" "ENV-1" "SQL Server responde y la base tiene las precondiciones de HU-059 y HU-060" $saludSql.Ok "2 personas 9860xxxx" $saludSql.Detalle
    $saludAdm = Esperar "Administrativo" { Login } $administrativo
    Verificar "Entorno" "ENV-2" "API Administrativa lista (login: lee USUARIOS en SQL)" $saludAdm.Ok "200" $saludAdm.Detalle
    if (-not $saludAdm.Ok) { throw "La API Administrativa no quedó lista: $($saludAdm.Detalle)" }
    $token = $saludAdm.Token
    $saludDep = Esperar "Deportivo" { $r = Pedir "GET" "$urlDeportivo/api/jugadores" $token $null $null; [pscustomobject]@{ Ok = ($r.Codigo -eq 200); Detalle = "GET /api/jugadores -> $($r.Codigo)" } } $deportivo
    Verificar "Entorno" "ENV-3" "API Deportiva lista (GET /api/jugadores: lee JUGADORES en SQL)" $saludDep.Ok "200" $saludDep.Detalle
    if (-not $saludDep.Ok) { throw "La API Deportiva no quedó lista: $($saludDep.Detalle)" }
    $precondiciones = "$(CuotaEsperada 'Masculino' 4)|$(CuotaEsperada 'Masculino' 3)|$(CuotaEsperada 'Femenino' 13)|$(CuotaEsperada 'Femenino' 12)|$(InscripcionEsperada 'Masculino')|$(InscripcionEsperada 'Femenino')"
    Verificar "Entorno" "ENV-4" "Los fixtures cubren la jerarquía: cat. con arancel | fallback de género | cat. femenina | sin arancel | inscripción M | inscripción F" `
        ($precondiciones -match '^\d+\.\d+\|\d+\.\d+\|\d+\.\d+\|\|\d+\.\d+\|$') "monto|monto|monto|(vacío)|monto|(vacío)" $precondiciones

    # ============================================================ dependencias
    $g = "Dependencias"
    $sonda = Pedir "POST" "$urlAdministrativo/api/integracion/v1/jugadores" $token $credencial @{}
    $errorSonda = (Json $sonda.Cuerpo).errorCode
    $hu056 = -not (($sonda.Codigo -eq 404 -and -not $errorSonda) -or $sonda.Codigo -in 0, 405, 500, 501)
    $motivoBloqueo = "POST /api/integracion/v1/jugadores en el Administrativo responde $($sonda.Codigo) $($sonda.Cuerpo): HU-056 no está implementada"
    Registrar $g "DEP-1" "HU-056: el Administrativo ofrece POST /api/integracion/v1/jugadores" $(if ($hu056) { "PASS" } else { "BLOCKED" }) "ruta existente (400 con errorCode)" "$($sonda.Codigo) $errorSonda" $sonda.Cuerpo
    $contrato = [IO.File]::ReadAllText((Join-Path $repo "docs\integracion\README.md"))
    $documentado = $contrato.Contains('## `POST /api/integracion/v1/jugadores`')
    Registrar $g "DEP-2" "HU-054: el contrato documenta el alta (POST /api/integracion/v1/jugadores)" $(if ($documentado) { "PASS" } else { "BLOCKED" }) "sección en docs/integracion/README.md" $(if ($documentado) { "documentado" } else { "no documentado" })

    # ============================================================ HU-055: el alta viaja con la credencial, nunca desde el navegador
    $g = "HU-055 autenticación"
    $personasAntes = Sql "SELECT COUNT(*) FROM PERSONAS"
    $sinClave = Pedir "POST" "$urlAdministrativo/api/integracion/v1/jugadores" $token $null @{ dni = "98600099"; nombre = "Sin"; apellido = "Clave"; genero = "Masculino"; fechaNacimiento = "2010-01-01"; idCategoria = 3 }
    $claveMala = Pedir "POST" "$urlAdministrativo/api/integracion/v1/jugadores" $token ("x" * 44) @{ dni = "98600099"; nombre = "Clave"; apellido = "Mala"; genero = "Masculino"; fechaNacimiento = "2010-01-01"; idCategoria = 3 }
    Verificar $g "AUT-1" "El alta del contrato sin credencial o con una inválida: 401 y no crea nada" `
        ($sinClave.Codigo -eq 401 -and $claveMala.Codigo -eq 401 -and (Sql "SELECT COUNT(*) FROM PERSONAS") -eq $personasAntes) "401/401; personas sin cambio" "$($sinClave.Codigo)/$($claveMala.Codigo); personas $personasAntes -> $(Sql 'SELECT COUNT(*) FROM PERSONAS')"
    $patron = 'X-Integration-Key|ClaveIntegracion|integracion/v1'
    $fuentes = @((Join-Path $repo "frontend-cacc\src"), $DeportivoFrontEnd)
    if (-not (Test-Path $DeportivoFrontEnd)) {
        Registrar $g "AUT-2" "Ningún frontend tiene la credencial ni llama a la API de integración" "BLOCKED" "fuentes de los dos frontends" "no encuentro $DeportivoFrontEnd"
    } else {
        $hallazgos = @(Get-ChildItem $fuentes -Recurse -File -Include *.ts, *.tsx, *.js, *.jsx, *.html, *.json -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\node_modules\\|\\dist\\' } | Select-String -Pattern $patron -List | ForEach-Object { $_.Path })
        Verificar $g "AUT-2" "Ningún frontend (Administrativo ni Deportivo) tiene la credencial ni llama a /api/integracion: el alta va backend a backend" `
            ($hallazgos.Count -eq 0) "0 archivos" "$($hallazgos.Count) archivos" ($hallazgos -join "; ")
    }

    # ============================================================ Caso 1: alta completa + identidad compartida + padrón y cobranzas
    $g = "Caso 1 alta completa"
    $alta1 = AltaDeportivo "98600001" "Alta" "Completa Hu" "Masculino" 4
    $id1 = [int](IdJugador "98600001")
    $ids.jugador98600001 = $id1
    if (-not $hu056) {
        Verificar $g "ALT-0" "Sin el alta del Administrativo, el Deportivo no deja una identidad a medias ni la da por completada" `
            ($alta1.Codigo -ge 400 -and (Cuentas "98600001") -eq "0|0|0|0") ">=400; 0|0|0|0" "$($alta1.Codigo); $(Cuentas '98600001')" $alta1.Cuerpo
    }
    SiHu056 $g "ALT-1" "POST /api/jugadores del Deportivo: 201 con el id del jugador" $alta1 {
        $cuerpo = Json $alta1.Cuerpo
        Verificar $g "ALT-1" "POST /api/jugadores del Deportivo: 201 con el id del jugador" ($alta1.Codigo -eq 201 -and $cuerpo.idJugador -eq $id1 -and $cuerpo.creado) "201; id $id1; creado" "$($alta1.Codigo); id $($cuerpo.idJugador); creado $($cuerpo.creado)" $alta1.Cuerpo
    }
    SiHu056 $g "ALT-2" "Base: una PERSONA y un JUGADOR, con nombre, apellido, DNI, género, categoría y FK correctos" $alta1 {
        $fila = Sql "SELECT CONCAT(p.nombre, '|', p.apellido, '|', p.dni, '|', p.genero, '|', j.FK_id_categoria, '|', CONVERT(CHAR(10), j.fecha_alta, 23), '|',
            (SELECT COUNT(*) FROM JUGADORES_CATEGORIAS jc WHERE jc.FK_id_jugador = j.PK_id_jugador AND jc.FK_id_categoria = 4), '|', CASE WHEN j.FK_id_persona = p.PK_id_persona THEN 1 ELSE 0 END)
            FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE j.PK_id_jugador = $id1"
        $cuentas = Cuentas "98600001"
        Verificar $g "ALT-2" "Base: una PERSONA y un JUGADOR, con nombre, apellido, DNI, género, categoría y FK correctos" `
            ($fila -eq "Alta|Completa Hu|98600001|Masculino|4|$hoyTxt|1|1" -and $cuentas.StartsWith("1|1|")) "Alta|Completa Hu|98600001|Masculino|4|$hoyTxt|1|1; 1|1" "$fila; $cuentas"
    }
    SiHu056 $g "ALT-3" "Identidad compartida: el Deportivo y el Administrativo devuelven el MISMO jugador; no hay una persona equivalente duplicada" $alta1 {
        $enDep = Pedir "GET" "$urlDeportivo/api/jugadores/$id1" $token $null $null
        $enAdm = Pedir "GET" "$urlAdministrativo/api/jugadores/$id1" $token $null $null
        $gemelas = Sql "SELECT COUNT(*) FROM PERSONAS WHERE nombre = N'Alta' AND apellido = N'Completa Hu'"
        $ids.persona98600001 = [int](Sql "SELECT FK_id_persona FROM JUGADORES WHERE PK_id_jugador = $id1")
        Verificar $g "ALT-3" "Identidad compartida: el Deportivo y el Administrativo devuelven el MISMO jugador; no hay una persona equivalente duplicada" `
            ($enDep.Codigo -eq 200 -and $enDep.Cuerpo.Contains("98600001") -and $enAdm.Codigo -eq 200 -and $enAdm.Cuerpo.Contains("98600001") -and $gemelas -eq "1") `
            "200/200 con el DNI; 1 persona" "$($enDep.Codigo)/$($enAdm.Codigo); $gemelas persona(s); id jugador $id1, persona $($ids.persona98600001)"
    }
    SiHu056 $g "ALT-4" "Padrón del Administrativo (GET /api/jugadores): aparece activo, con su DNI y categoría" $alta1 {
        $padron = Pedir "GET" "$urlAdministrativo/api/jugadores" $token $null $null
        $item = @(Json $padron.Cuerpo) | Where-Object { $_.idJugador -eq $id1 }
        Verificar $g "ALT-4" "Padrón del Administrativo (GET /api/jugadores): aparece activo, con su DNI y categoría" `
            ($padron.Codigo -eq 200 -and $item -and $item.dni -eq "98600001" -and $item.activo -and $item.genero -eq "Masculino") "200; activo, DNI, Masculino" "$($padron.Codigo); $($item | ConvertTo-Json -Compress)"
    }
    SiHu056 $g "ALT-5" "Búsqueda de Cuotas y Pagos (GET /api/players/search) lo encuentra por DNI" $alta1 {
        $busqueda = Pedir "GET" "$urlAdministrativo/api/players/search?term=98600001" $token $null $null
        Verificar $g "ALT-5" "Búsqueda de Cuotas y Pagos (GET /api/players/search) lo encuentra por DNI" `
            ($busqueda.Codigo -eq 200 -and @(Json $busqueda.Cuerpo | Where-Object { $_.id -eq $id1 }).Count -eq 1) "200; 1 resultado" "$($busqueda.Codigo); $($busqueda.Cuerpo)"
    }
    SiHu056 $g "ALT-6" "Cobranzas: está en el padrón de cuentas (GET /api/pagos/player-accounts) y su cuenta se puede consultar" $alta1 {
        $cuentasTes = Pedir "GET" "$urlAdministrativo/api/pagos/player-accounts" $token $null $null
        $cuenta = Pedir "GET" "$urlAdministrativo/api/pagos/jugador/$id1/estado-de-cuenta" $token $null $null
        Verificar $g "ALT-6" "Cobranzas: está en el padrón de cuentas (GET /api/pagos/player-accounts) y su cuenta se puede consultar" `
            ($cuentasTes.Codigo -eq 200 -and @(Json $cuentasTes.Cuerpo | Where-Object { $_.playerId -eq $id1 }).Count -eq 1 -and $cuenta.Codigo -eq 200) "200 con el jugador; 200" "$($cuentasTes.Codigo); $($cuenta.Codigo)"
    }

    # ============================================================ Caso 2: cuota del mes, calendario (HU-083), jerarquía (HU-048) y monto histórico
    $g = "Caso 2 cuota y aranceles"
    $esperada4 = CuotaEsperada "Masculino" 4
    $alta4 = AltaDeportivo "98600004" "Fallback" "Genero Hu" "Masculino" 3
    $id4 = [int](IdJugador "98600004")
    $ids.jugador98600004 = $id4
    $esperada3 = CuotaEsperada "Masculino" 3
    $cuotaDelMes = if ($emiteCuota) { "1 cuota de $mesTxt" } else { "0 cuotas (antes del día 10)" }
    SiHu056 $g "CUO-1" "Calendario HU-083 (alta el $hoyTxt): $cuotaDelMes" $alta1 {
        $cuotas1 = Montos $id1 "Cuota"
        $ok = if ($emiteCuota) { $cuotas1 -match "^[\d.]+@$mesTxt$" } else { $cuotas1 -eq "" }
        Verificar $g "CUO-1" "Calendario HU-083 (alta el $hoyTxt): $cuotaDelMes" $ok $cuotaDelMes $cuotas1
    }
    # La otra rama del calendario: el Administrativo se reinicia con una fecha de negocio fija (Pruebas:FechaNegocio, solo
    # Development/Testing) del mismo mes, del otro lado del día 10. El Deportivo sigue apuntando al mismo puerto. No se cambia
    # la hora de la máquina; después se vuelve al reloj real.
    $fechaOtraRama = New-Object DateTime $hoy.Year, $hoy.Month, $(if ($emiteCuota) { 5 } else { 15 })
    $fechaOtraTxt = $fechaOtraRama.ToString("yyyy-MM-dd")
    $otraRama = if ($emiteCuota) { "antes del día 10 ($fechaOtraTxt): no emite la cuota" } else { "desde el día 10 ($fechaOtraTxt): emite la cuota" }
    Detener $administrativo | Out-Null
    $entornoReloj = EntornoAdministrativo $false
    $entornoReloj["Pruebas__FechaNegocio"] = $fechaOtraTxt
    $administrativo = Iniciar "administrativo_reloj" $AdministrativoDll $PuertoAdministrativo $entornoReloj
    $listoReloj = Esperar "Administrativo" { Login } $administrativo
    if (-not $listoReloj.Ok) { throw "El Administrativo con fecha fija no quedó listo: $($listoReloj.Detalle)" }
    $alta9 = AltaDeportivo "98600009" "Otra" "Rama Hu" "Masculino" 3
    $id9 = [int](IdJugador "98600009")
    $ids.jugador98600009 = $id9
    $fila9 = Sql "SELECT CONCAT(CONVERT(CHAR(10), fecha_alta, 23), '|', FK_id_categoria) FROM JUGADORES WHERE PK_id_jugador = $id9"
    $cuotas9 = Montos $id9 "Cuota"
    $insc9 = Montos $id9 "Inscripcion"
    $cuotaOtra = if ($emiteCuota) { "" } else { "$esperada3@$mesTxt" }
    Verificar $g "CUO-2" "Calendario HU-083, la otra rama: alta $otraRama" `
        ($alta9.Codigo -eq 201 -and $fila9 -eq "$fechaOtraTxt|3" -and $cuotas9 -eq $cuotaOtra -and $insc9 -eq "$(InscripcionEsperada "Masculino")@$fechaOtraTxt") `
        "201; alta $fechaOtraTxt|3; cuota '$cuotaOtra'; inscripción $(InscripcionEsperada "Masculino")@$fechaOtraTxt" "$($alta9.Codigo); alta $fila9; cuota '$cuotas9'; inscripción $insc9" (Resumen $alta9)
    Detener $administrativo | Out-Null
    $administrativo = Iniciar "administrativo_real" $AdministrativoDll $PuertoAdministrativo (EntornoAdministrativo $false)
    $listoReal = Esperar "Administrativo" { Login } $administrativo
    if (-not $listoReal.Ok) { throw "El Administrativo con el reloj real no volvió: $($listoReal.Detalle)" }
    # La fecha fija nunca llega a Producción: con ASPNETCORE_ENVIRONMENT=Production la API no arranca.
    $entornoProd = EntornoAdministrativo $false
    $entornoProd["Pruebas__FechaNegocio"] = $fechaOtraTxt
    $entornoProd["ASPNETCORE_ENVIRONMENT"] = "Production"
    $prod = Iniciar "administrativo_produccion_reloj" $AdministrativoDll ($PuertoAdministrativo + 3) $entornoProd
    $termino = $prod.Proceso.WaitForExit(60000)
    $logProd = Detener $prod
    Verificar $g "REL-1" "Con Pruebas:FechaNegocio en Production la API no arranca (la fecha fija no puede llegar a Producción)" `
        ($termino -and $prod.Proceso.ExitCode -ne 0 -and $logProd.Contains("Pruebas:FechaNegocio solo se admite")) "termina con error; mensaje de la guarda" "terminó: $termino; código $($prod.Proceso.ExitCode); mensaje: $($logProd.Contains('Pruebas:FechaNegocio solo se admite'))"
    SiHu056 $g "CUO-3" "HU-048 caso A: categoría con arancel propio -> el de la categoría" $alta1 {
        $cuotas1 = Montos $id1 "Cuota"
        $ok = if ($emiteCuota) { $cuotas1 -eq "$esperada4@$mesTxt" } else { $cuotas1 -eq "" }
        Verificar $g "CUO-3" "HU-048 caso A: categoría con arancel propio -> el de la categoría" $ok $(if ($emiteCuota) { "$esperada4@$mesTxt" } else { "sin cuota" }) $cuotas1
    }
    SiHu056 $g "CUO-4" "HU-048 caso B: categoría sin arancel propio -> el de su género" $alta4 {
        $cuotas4 = Montos $id4 "Cuota"
        $ok = if ($emiteCuota) { $cuotas4 -eq "$esperada3@$mesTxt" } else { $cuotas4 -eq "" }
        Verificar $g "CUO-4" "HU-048 caso B: categoría sin arancel propio -> el de su género" $ok $(if ($emiteCuota) { "$esperada3@$mesTxt" } else { "sin cuota" }) $cuotas4 (Resumen $alta4)
    }
    $ultimoArancel4 = Sql "SELECT TOP (1) CONVERT(VARCHAR(20), monto) FROM ARANCELES WHERE FK_id_categoria = 4 ORDER BY vigente_desde DESC"
    SiHu056 $g "CUO-5" "La cuota lleva el arancel vigente al emitir, no el último cargado ($ultimoArancel4, futuro)" $alta1 {
        $cuotas1 = Montos $id1 "Cuota"
        Verificar $g "CUO-5" "La cuota lleva el arancel vigente al emitir, no el último cargado ($ultimoArancel4, futuro)" `
            ((-not $emiteCuota) -or ($cuotas1 -ne "" -and -not $cuotas1.StartsWith("$ultimoArancel4@"))) "distinto de $ultimoArancel4" $cuotas1
    }

    # ============================================================ Caso 3: inscripción masculina (única) y reintentos
    $g = "Caso 3 inscripción masculina"
    $inscM = InscripcionEsperada "Masculino"
    SiHu056 $g "INS-1" "Varón: UNA inscripción del jugador, con el arancel de inscripción vigente, separada de la cuota" $alta1 {
        $insc1 = Montos $id1 "Inscripcion"
        Verificar $g "INS-1" "Varón: UNA inscripción del jugador, con el arancel de inscripción vigente, separada de la cuota" ($insc1 -eq "$inscM@$hoyTxt") "$inscM@$hoyTxt" $insc1
    }
    SiHu056 $g "INS-2" "Varón por el fallback de género: también UNA inscripción" $alta4 {
        $insc4 = Montos $id4 "Inscripcion"
        Verificar $g "INS-2" "Varón por el fallback de género: también UNA inscripción" ($insc4 -eq "$inscM@$hoyTxt") "$inscM@$hoyTxt" $insc4
    }

    $g = "Reintentos"
    $antes1 = Cuentas "98600001"
    $reintentos = @(1..2 | ForEach-Object { AltaDeportivo "98600001" "Alta" "Completa Hu" "Masculino" 4 })
    SiHu056 $g "REI-1" "La misma alta nueva dos veces más: 200 sin duplicar persona, jugador, cuota ni inscripción" $reintentos[0] {
        $despues1 = Cuentas "98600001"
        Verificar $g "REI-1" "La misma alta nueva dos veces más: 200 sin duplicar persona, jugador, cuota ni inscripción" `
            (($reintentos | Where-Object Codigo -ne 200).Count -eq 0 -and $despues1 -eq $antes1 -and $antes1 -match '^1\|1\|') "200/200; $antes1" "$(($reintentos | ForEach-Object Codigo) -join '/'); $despues1"
    }

    # ============================================================ Caso 4: jugadora
    $g = "Caso 4 jugadora"
    $alta5 = AltaDeportivo "98600005" "Jugadora" "Femenina Hu" "Femenino" 13
    $id5 = [int](IdJugador "98600005")
    $ids.jugador98600005 = $id5
    $esperada13 = CuotaEsperada "Femenino" 13
    SiHu056 $g "FEM-1" "Jugadora: persona Femenino en la categoría 13" $alta5 {
        $fila = Sql "SELECT CONCAT(p.genero, '|', j.FK_id_categoria) FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE j.PK_id_jugador = $id5"
        Verificar $g "FEM-1" "Jugadora: persona Femenino en la categoría 13" ($alta5.Codigo -eq 201 -and $fila -eq "Femenino|13") "201; Femenino|13" "$($alta5.Codigo); $fila"
    }
    SiHu056 $g "FEM-2" "Jugadora: NO recibe inscripción (ni masculina ni en `$0)" $alta5 {
        $insc5 = Montos $id5 "Inscripcion"
        Verificar $g "FEM-2" "Jugadora: NO recibe inscripción (ni masculina ni en `$0)" ($insc5 -eq "") "ninguna" $(if ($insc5) { $insc5 } else { "ninguna" })
    }
    SiHu056 $g "FEM-3" "Jugadora: la cuota sigue el calendario y el arancel de su categoría" $alta5 {
        $cuotas5 = Montos $id5 "Cuota"
        $ok = if ($emiteCuota) { $cuotas5 -eq "$esperada13@$mesTxt" } else { $cuotas5 -eq "" }
        Verificar $g "FEM-3" "Jugadora: la cuota sigue el calendario y el arancel de su categoría" $ok $(if ($emiteCuota) { "$esperada13@$mesTxt" } else { "sin cuota" }) $cuotas5
    }

    # ============================================================ Caso 5: DNI repetido
    $g = "Caso 5 DNI repetido"
    $alta2 = AltaDeportivo "98600002" "Otro" "Nombre Distinto" "Masculino" 3
    $id2 = [int](IdJugador "98600002")
    $ids.jugador98600002 = $id2
    SiHu056 $g "DNI-1" "PERSONA existente sin JUGADOR: se reutiliza (misma PK, sin pisar sus datos) y el JUGADOR apunta a ella" $alta2 {
        $fila = Sql "SELECT CONCAT((SELECT COUNT(*) FROM PERSONAS WHERE dni = '98600002'), '|', p.PK_id_persona, '|', p.nombre, '|', p.apellido, '|', j.FK_id_persona)
            FROM PERSONAS p JOIN JUGADORES j ON j.FK_id_persona = p.PK_id_persona WHERE p.dni = '98600002'"
        Verificar $g "DNI-1" "PERSONA existente sin JUGADOR: se reutiliza (misma PK, sin pisar sus datos) y el JUGADOR apunta a ella" `
            ($fila -eq "1|$personaPrevia|Previa|Sin Jugador|$personaPrevia") "1|$personaPrevia|Previa|Sin Jugador|$personaPrevia" "$fila" (Resumen $alta2)
    }
    $antes3 = Cuentas "98600003"
    $reenvio3 = AltaDeportivo "98600003" "Previo" "Con Jugador" "Masculino" 3
    SiHu056 $g "DNI-2" "PERSONA + JUGADOR ya existentes: reenviar el alta no duplica persona, jugador, cuota ni inscripción" $reenvio3 {
        $despues3 = Cuentas "98600003"
        $cuerpo = Json $reenvio3.Cuerpo
        Verificar $g "DNI-2" "PERSONA + JUGADOR ya existentes: reenviar el alta no duplica persona, jugador, cuota ni inscripción" `
            ($reenvio3.Codigo -eq 200 -and $cuerpo.idJugador -eq $ids.jugadorPrevio98600003 -and $despues3 -eq $antes3) "200; id $($ids.jugadorPrevio98600003); $antes3" "$($reenvio3.Codigo); id $($cuerpo.idJugador); $despues3"
    }
    $g = "Reintentos"
    $antes2 = Cuentas "98600002"
    $reintento2 = AltaDeportivo "98600002" "Otro" "Nombre Distinto" "Masculino" 3
    SiHu056 $g "REI-2" "Reintento del alta con DNI existente: 200 y nada duplicado" $reintento2 {
        $despues2 = Cuentas "98600002"
        Verificar $g "REI-2" "Reintento del alta con DNI existente: 200 y nada duplicado" ($reintento2.Codigo -eq 200 -and $despues2 -eq $antes2 -and $antes2 -match '^1\|1\|') "200; $antes2" "$($reintento2.Codigo); $despues2"
    }

    # ============================================================ Casos 6 y 7: alta sin categoría y sin DNI
    $g = "Caso 6 sin categoría"
    $pagosAntes = Sql "SELECT COUNT(*) FROM PAGOS"
    $sinCat = AltaDeportivo "98600007" "Sin" "Categoria Hu" "Masculino" $null
    Verificar $g "CAT-1" "Deportivo: 400 (campo idCategoria), sin identidad ni cargos" `
        ($sinCat.Codigo -eq 400 -and (Json $sinCat.Cuerpo).field -eq "idCategoria" -and (Cuentas "98600007") -eq "0|0|0|0" -and (Sql "SELECT COUNT(*) FROM PAGOS") -eq $pagosAntes) `
        "400 idCategoria; 0|0|0|0" "$($sinCat.Codigo) $((Json $sinCat.Cuerpo).field); $(Cuentas '98600007')" $sinCat.Cuerpo
    $rechazosAntes = [int](Sql "SELECT COUNT(*) FROM INTEGRACION_RECHAZOS")
    $contratoSinCat = Pedir "POST" "$urlAdministrativo/api/integracion/v1/jugadores" $token $credencial @{ dni = "98600007"; nombre = "Sin"; apellido = "Categoria Hu"; genero = "Masculino"; fechaNacimiento = "2010-06-15" }
    SiHu056 $g "CAT-2" "Contrato del Administrativo sin idCategoria: 400 INVALID_INPUT (field idCategoria), rechazo registrado, sin datos" $contratoSinCat {
        $c = Json $contratoSinCat.Cuerpo
        $rechazo = Sql "SELECT TOP (1) CONCAT(operacion, '|', error_code, '|', origen_sistema, '|', CASE WHEN motivo LIKE '%98600007%' THEN 'con DNI' ELSE 'sin DNI' END) FROM INTEGRACION_RECHAZOS ORDER BY PK_id_rechazo DESC"
        Verificar $g "CAT-2" "Contrato del Administrativo sin idCategoria: 400 INVALID_INPUT (field idCategoria), rechazo registrado, sin datos" `
            ($contratoSinCat.Codigo -eq 400 -and $c.errorCode -eq "INVALID_INPUT" -and $c.field -eq "idCategoria" -and [int](Sql "SELECT COUNT(*) FROM INTEGRACION_RECHAZOS") -eq $rechazosAntes + 1 -and $rechazo -eq "AltaJugador|INVALID_INPUT|PortalDeportivo|sin DNI" -and (Cuentas "98600007") -eq "0|0|0|0") `
            "400 INVALID_INPUT idCategoria; AltaJugador|INVALID_INPUT|PortalDeportivo|sin DNI; 0|0|0|0" "$($contratoSinCat.Codigo) $($c.errorCode) $($c.field); $rechazo; $(Cuentas '98600007')"
    }

    $g = "Caso 7 sin DNI"
    $personasAntes = Sql "SELECT COUNT(*) FROM PERSONAS"
    $sinDni = AltaDeportivo $null "Sin" "Documento Hu" "Masculino" 3
    Verificar $g "SDN-1" "Deportivo: 400, sin crear una identidad sin DNI ni cargos" `
        ($sinDni.Codigo -eq 400 -and (Sql "SELECT COUNT(*) FROM PERSONAS") -eq $personasAntes -and (Sql "SELECT COUNT(*) FROM PERSONAS WHERE apellido = N'Documento Hu'") -eq "0" -and (Sql "SELECT COUNT(*) FROM PAGOS") -eq $pagosAntes) `
        "400; personas sin cambio" "$($sinDni.Codigo); personas $personasAntes -> $(Sql 'SELECT COUNT(*) FROM PERSONAS')" $sinDni.Cuerpo
    $rechazosAntes = [int](Sql "SELECT COUNT(*) FROM INTEGRACION_RECHAZOS")
    $contratoSinDni = Pedir "POST" "$urlAdministrativo/api/integracion/v1/jugadores" $token $credencial @{ nombre = "Sin"; apellido = "Documento Hu"; genero = "Masculino"; fechaNacimiento = "2010-06-15"; idCategoria = 3 }
    SiHu056 $g "SDN-2" "Contrato del Administrativo sin DNI: 400 INVALID_INPUT (field dni), rechazo registrado, sin datos" $contratoSinDni {
        $c = Json $contratoSinDni.Cuerpo
        Verificar $g "SDN-2" "Contrato del Administrativo sin DNI: 400 INVALID_INPUT (field dni), rechazo registrado, sin datos" `
            ($contratoSinDni.Codigo -eq 400 -and $c.errorCode -eq "INVALID_INPUT" -and $c.field -eq "dni" -and [int](Sql "SELECT COUNT(*) FROM INTEGRACION_RECHAZOS") -eq $rechazosAntes + 1 -and (Sql "SELECT COUNT(*) FROM PERSONAS") -eq $personasAntes) `
            "400 INVALID_INPUT dni; +1 rechazo; personas sin cambio" "$($contratoSinDni.Codigo) $($c.errorCode) $($c.field); rechazos $rechazosAntes -> $(Sql 'SELECT COUNT(*) FROM INTEGRACION_RECHAZOS')"
    }

    # ============================================================ Caso 9: sin arancel aplicable + no afectar a las demás altas
    $g = "Caso 9 sin arancel"
    $alta6 = AltaDeportivo "98600006" "Sin" "Arancel Hu" "Femenino" 12
    $id6 = [int](IdJugador "98600006")
    $ids.jugador98600006 = $id6
    SiHu056 $g "SAR-1" "Jugadora en categoría sin arancel y género sin arancel: se registra (política de HU-056) sin cuota inventada ni cargos en `$0" $alta6 {
        $cuentas6 = Cuentas "98600006"
        $enCero = Sql "SELECT COUNT(*) FROM PAGOS WHERE FK_id_jugador = $id6 AND monto_base <= 0"
        Verificar $g "SAR-1" "Jugadora en categoría sin arancel y género sin arancel: se registra (política de HU-056) sin cuota inventada ni cargos en `$0" `
            ($alta6.Codigo -eq 201 -and $cuentas6 -eq "1|1|0|0" -and $enCero -eq "0") "201; 1|1|0|0; 0 en `$0" "$($alta6.Codigo); $cuentas6; $enCero en `$0"
    }
    $alta8 = AltaDeportivo "98600008" "Despues" "Sin Arancel Hu" "Masculino" 3
    $id8 = [int](IdJugador "98600008")
    $ids.jugador98600008 = $id8
    SiHu056 $g "SAR-2" "Después del caso sin arancel, otra alta válida e independiente se procesa completa" $alta8 {
        $cuotas8 = Montos $id8 "Cuota"
        $ok = $alta8.Codigo -eq 201 -and $(if ($emiteCuota) { $cuotas8 -eq "$esperada3@$mesTxt" } else { $cuotas8 -eq "" })
        Verificar $g "SAR-2" "Después del caso sin arancel, otra alta válida e independiente se procesa completa" $ok "201; $(if ($emiteCuota) { "$esperada3@$mesTxt" } else { 'sin cuota' })" "$($alta8.Codigo); $cuotas8"
    }

    # ============================================================ Auditoría
    $g = "Auditoría"
    SiHu056 $g "AUD-1" "Alta exitosa auditada: PERSONAS y JUGADORES INSERT, origen PortalDeportivo, operador del token, fecha de hoy" $alta1 {
        $aud = Sql "SELECT CONCAT((SELECT COUNT(*) FROM AUDITORIA_CAMBIOS WHERE entidad = 'JUGADORES' AND accion = 'INSERT' AND id_entidad = $id1 AND origen_sistema = 'PortalDeportivo' AND id_usuario = $idSuperAdmin
                AND CAST(SWITCHOFFSET(CAST(fecha_utc AS DATETIMEOFFSET), '-03:00') AS DATE) = '$hoyTxt'), '|',
            (SELECT COUNT(*) FROM AUDITORIA_CAMBIOS WHERE entidad = 'PERSONAS' AND accion = 'INSERT' AND id_entidad = $($ids.persona98600001) AND origen_sistema = 'PortalDeportivo'))"
        Verificar $g "AUD-1" "Alta exitosa auditada: PERSONAS y JUGADORES INSERT, origen PortalDeportivo, operador del token, fecha de hoy" ($aud -eq "1|1") "1|1" $aud
    }
    SiHu056 $g "AUD-2" "Altas rechazadas: ningún INSERT auditado como alta completada" $contratoSinCat {
        $fantasma = Sql "SELECT COUNT(*) FROM AUDITORIA_CAMBIOS WHERE entidad IN ('PERSONAS', 'JUGADORES') AND accion = 'INSERT' AND datos_despues LIKE '%98600007%'"
        Verificar $g "AUD-2" "Altas rechazadas: ningún INSERT auditado como alta completada" ($fantasma -eq "0") "0" $fantasma
    }

    # ============================================================ Caso 8: perfiles no jugadores (Deportivo crea el staff, Administrativo habilita)
    $g = "Caso 8 no jugadores"
    $staff = [ordered]@{ "98600010" = "Director Técnico"; "98600011" = "Médico"; "98600012" = "Administrador" }
    $creados = foreach ($dni in $staff.Keys) {
        $r = Pedir "POST" "$urlDeportivo/api/staff" $token $null @{ nombre = "Staff"; apellido = "Hu Sesenta $dni"; dni = $dni; email = "hu060.$dni@example.com"
            contrasenia = $claveStaff; rol = $staff[$dni]; genero = "Masculino"; fechaNacimiento = "1990-01-01"; idCategoria = 3 }
        [pscustomobject]@{ Dni = $dni; Rol = $staff[$dni]; Codigo = $r.Codigo; Cuerpo = $r.Cuerpo }
    }
    $usuarios = Sql "SELECT STRING_AGG(CONCAT(p.dni, ':', u.FK_id_rol), ',') WITHIN GROUP (ORDER BY p.dni) FROM USUARIOS u JOIN PERSONAS p ON p.PK_id_persona = u.FK_id_persona WHERE p.dni IN ('98600010', '98600011', '98600012')"
    Verificar $g "NOJ-1" "El Deportivo crea DT, Médico y Administrador (POST /api/staff): PERSONA + USUARIO con su rol" `
        (($creados | Where-Object Codigo -ne 201).Count -eq 0 -and $usuarios -eq "98600010:4,98600011:3,98600012:1") "201 x3; 98600010:4,98600011:3,98600012:1" "$(($creados | ForEach-Object Codigo) -join '/'); $usuarios" (($creados | ForEach-Object { "$($_.Dni) $($_.Codigo) $($_.Cuerpo)" }) -join " || ")
    $idAdmin = Sql "SELECT ISNULL(MAX(u.PK_id_usuario), 0) FROM USUARIOS u JOIN PERSONAS p ON p.PK_id_persona = u.FK_id_persona WHERE p.dni = '98600012'"
    $candidatos = Pedir "GET" "$urlAdministrativo/api/usuarios/candidatos" $token $null $null
    $habilitar = Pedir "POST" "$urlAdministrativo/api/usuarios/$idAdmin/habilitar" $token $null @{ email = "hu060.98600012@example.com"; rolPortal = 2 }
    $acceso = Sql "SELECT CONCAT(acceso_portal, '|', rol_portal) FROM USUARIOS WHERE PK_id_usuario = $idAdmin"
    Verificar $g "NOJ-2" "El Administrativo habilita al Administrador (candidatos + POST /api/usuarios/{id}/habilitar)" `
        ($candidatos.Codigo -eq 200 -and @(Json $candidatos.Cuerpo | Where-Object { $_.idUsuario -eq [int]$idAdmin -and $_.dni -eq "98600012" }).Count -ge 1 -and $habilitar.Codigo -eq 200 -and $acceso -eq "1|2") `
        "200 (candidato); 200; acceso 1|2" "$($candidatos.Codigo); $($habilitar.Codigo); acceso $acceso" $habilitar.Cuerpo
    $ids.usuarioAdministrador98600012 = [int]$idAdmin
    # Para que "no reciben cargos" se pruebe de verdad: corre la generación manual de un mes cerrado y el generador automático.
    $mesCerrado = $mesActual.AddMonths(-1)
    $manual = Pedir "POST" "$urlAdministrativo/api/cuotas-faltantes/$($mesCerrado.Year)/$($mesCerrado.Month)/generar" $token $null $null
    Detener $administrativo | Out-Null
    $administrativo = Iniciar "administrativo_generador" $AdministrativoDll $PuertoAdministrativo (EntornoAdministrativo $true)
    $control = [int](Sql "SELECT j.PK_id_jugador FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni = '98590003'")
    $periodoEmitible = if ($emiteCuota) { $mesTxt } else { $mesActual.AddMonths(-1).ToString("yyyy-MM-dd") }
    $generado = Esperar "generador" { $n = Sql "SELECT COUNT(*) FROM PAGOS WHERE FK_id_jugador = $control AND concepto = 'Cuota' AND fecha_vencimiento = '$periodoEmitible'"; [pscustomobject]@{ Ok = ($n -eq "1"); Detalle = "cuota de control = $n" } } $administrativo
    $relogin = Esperar "Administrativo" { Login } $administrativo
    $token = $relogin.Token
    $dnisNoJugadores = @("98600010", "98600011", "98600012", "20111222", "30000002", "30000003", "30000004", "30000005")
    $lista = ($dnisNoJugadores | ForEach-Object { "'$_'" }) -join ","
    $comoJugador = Sql "SELECT CONCAT((SELECT COUNT(*) FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni IN ($lista)), '|',
        (SELECT COUNT(*) FROM PAGOS g JOIN JUGADORES j ON j.PK_id_jugador = g.FK_id_jugador JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni IN ($lista)))"
    Verificar $g "NOJ-3" "Staff y cuentas de portal no son JUGADORES ni tienen cuotas o inscripciones, después de la generación manual y la automática" `
        ($manual.Codigo -eq 200 -and $generado.Ok -and $comoJugador -eq "0|0") "generación 200 + automática; 0 jugadores|0 cargos" "$($manual.Codigo) + $($generado.Detalle); $comoJugador"
    $padron = Pedir "GET" "$urlAdministrativo/api/jugadores" $token $null $null
    $cuentasTes = Pedir "GET" "$urlAdministrativo/api/pagos/player-accounts" $token $null $null
    $enPadron = @(Json $padron.Cuerpo | Where-Object { $_.dni -in $dnisNoJugadores }).Count
    $enCuentas = @(Json $cuentasTes.Cuerpo | Where-Object { $_.dni -in $dnisNoJugadores }).Count
    $enBusqueda = ($dnisNoJugadores | ForEach-Object { @(Json (Pedir "GET" "$urlAdministrativo/api/players/search?term=$_" $token $null $null).Cuerpo).Count } | Measure-Object -Sum).Sum
    Verificar $g "NOJ-4" "No aparecen en el padrón, en la búsqueda de Cuotas y Pagos ni en el padrón de cobranzas" `
        ($padron.Codigo -eq 200 -and $cuentasTes.Codigo -eq 200 -and $enPadron -eq 0 -and $enCuentas -eq 0 -and $enBusqueda -eq 0) "0|0|0" "$enPadron|$enCuentas|$enBusqueda"

    # ============================================================ después de las generaciones: nada duplicado, monto histórico intacto
    $g = "Caso 2 cuota y aranceles"
    SiHu056 $g "CUO-6" "Tras la generación manual y la automática: la cuota del alta no se duplica y conserva su monto" $alta1 {
        $cuotas1 = Montos $id1 "Cuota"
        Verificar $g "CUO-6" "Tras la generación manual y la automática: la cuota del alta no se duplica y conserva su monto" `
            ($cuotas1 -eq $(if ($emiteCuota) { "$esperada4@$mesTxt" } else { "" })) $(if ($emiteCuota) { "$esperada4@$mesTxt" } else { "sin cuota" }) $cuotas1
    }
}
catch {
    Registrar "Entorno" "ENV-X" "La ejecución se interrumpió" "BLOCKED" "sin error técnico" "error" "$($_.Exception.Message) @ línea $($_.InvocationInfo.ScriptLineNumber)"
}
finally {
    $logs = foreach ($api in $procesos) { Detener $api }
    $textoLogs = $logs -join "`n"
    $secretos = ([regex]::Matches($textoLogs, [regex]::Escape($credencial))).Count + ([regex]::Matches($textoLogs, [regex]::Escape($claveJwt))).Count +
        ([regex]::Matches($textoLogs, [regex]::Escape($claveStaff))).Count + ([regex]::Matches($textoLogs, 'eyJ[A-Za-z0-9_-]{20,}')).Count
    Verificar "Logs" "LOG-1" "Los logs de las dos APIs no contienen la credencial, la clave JWT, contraseñas ni tokens" ($secretos -eq 0) 0 $secretos
}

# ---------------------------------------------------------------- resumen
$duracion = [int]((Get-Date) - $inicioCorrida).TotalSeconds
Write-Host "`n== Resumen por grupo" -ForegroundColor Cyan
$grupos = $resultados | Group-Object Grupo | ForEach-Object {
    $estados = $_.Group.Estado
    $estado = if ($estados -contains "FAIL") { "FAIL" } elseif ($estados -contains "BLOCKED") { "BLOCKED" } else { "PASS" }
    [pscustomobject]@{ Grupo = $_.Name; Estado = $estado; Casos = $_.Count
        Pass = @($estados | Where-Object { $_ -eq "PASS" }).Count; Fail = @($estados | Where-Object { $_ -eq "FAIL" }).Count; Blocked = @($estados | Where-Object { $_ -eq "BLOCKED" }).Count }
}
foreach ($grupo in $grupos) { Write-Host ("{0}: {1} ({2} PASS, {3} FAIL, {4} BLOCKED)" -f $grupo.Grupo, $grupo.Estado, $grupo.Pass, $grupo.Fail, $grupo.Blocked) -ForegroundColor @{ PASS = "Green"; FAIL = "Red"; BLOCKED = "Yellow" }[$grupo.Estado] }
$pass = @($resultados | Where-Object Estado -eq "PASS").Count
$fail = @($resultados | Where-Object Estado -eq "FAIL").Count
$blocked = @($resultados | Where-Object Estado -eq "BLOCKED").Count
Write-Host "`nTotal: $pass PASS | $fail FAIL | $blocked BLOCKED   ($duracion s, fecha de negocio $hoyTxt, base $Base)"

$informe = [pscustomobject]@{
    fecha = (Get-Date).ToString("s"); fechaNegocio = $hoyTxt; altaEmiteCuotaDelMes = $emiteCuota; duracionSegundos = $duracion; base = $Base; servidor = $Servidor
    administrativoDll = $AdministrativoDll; deportivoDll = $DeportivoDll; hu056Disponible = $hu056; ids = $ids
    totales = [pscustomobject]@{ pass = $pass; fail = $fail; blocked = $blocked }; grupos = $grupos; casos = $resultados
}
$json = $informe | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $Salida "resultado.json"), $json, [Text.UTF8Encoding]::new($false))
if ($Evidencia) { [IO.File]::WriteAllText($Evidencia, $json, [Text.UTF8Encoding]::new($false)) }
Write-Host "Evidencia: $Salida"
if ($fail -gt 0 -or $blocked -gt 0) { exit 1 }
exit 0
