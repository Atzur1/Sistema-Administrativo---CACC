<#
.SYNOPSIS
    HU-066: concurrencia e integridad de datos con los dos sistemas escribiendo a la vez en la base compartida. Corre sobre el
    entorno de HU-059: las dos APIs reales, SQL Server real y la base unificada de prueba (HU-053).

.DESCRIPTION
    1. Reset de HU-059 + precondiciones de HU-066 (datos/04_fixtures_hu066.sql). Siempre el mismo estado inicial.
    2. Levanta las dos APIs (comun.ps1 de HU-059) con un Application Name por sistema, para distinguir sus sesiones en SQL Server.
    3. Cada escenario lanza operaciones REALMENTE simultáneas por las APIs (Concurrencia.cs: un hilo por pedido y una Barrier).
       Cuando el escenario necesita que el generador mensual (un segundo proceso del Administrativo) y los pedidos HTTP estén en
       vuelo a la vez, se usa una compuerta: el mismo bloqueo de aplicación que usan las APIs se sostiene hasta que todos están
       esperando, y recién ahí se libera. El orden lo decide SQL Server, no la prueba.
    4. Mientras tanto un monitor muestrea sys.dm_exec_requests / sys.dm_tran_locks: superposición real, bloqueos y su duración,
       nivel de aislamiento de cada sesión de las APIs.
    5. Después de cada escenario: estado final por SQL (SOLO para verificar), integridad referencial y de negocio antes/después,
       auditoría (origen y operador de cada evento), deadlocks (system_health) y errores en los logs.
    6. Cada escenario se repite -Iteraciones veces desde precondiciones nuevas, y los de altas y pagos también con ráfagas de
       -Cargas operaciones.
    Código de salida: 0 = todo PASS; 1 = algún FAIL o BLOCKED; 2 = el entorno no se pudo preparar.

    Las precondiciones por iteración (jugadores sin la cuota del mes, cuotas pendientes para cobrar) se insertan directo, igual
    que los fixtures de HU-059: son el estado ANTES de la prueba. Las operaciones que se prueban van siempre por las APIs.

.EXAMPLE
    pwsh -File qa\HU-066\concurrencia_hu066.ps1 -Evidencia qa\HU-066\evidencia_ejecucion1.json
#>
#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$Servidor = $(if ($env:CACC_E2E_SQL_SERVIDOR) { $env:CACC_E2E_SQL_SERVIDOR } else { "(localdb)\MSSQLLocalDB" }),
    [string]$Base = $(if ($env:CACC_E2E_BASE) { $env:CACC_E2E_BASE } else { "ClubCamionerosUnificadaV2_HU059_Prueba" }),
    [string]$AdministrativoDll = $env:CACC_E2E_ADMINISTRATIVO_DLL,
    [string]$DeportivoDll = $env:CACC_E2E_DEPORTIVO_DLL,
    [int]$PuertoAdministrativo = $(if ($env:CACC_E2E_PUERTO_ADMINISTRATIVO) { [int]$env:CACC_E2E_PUERTO_ADMINISTRATIVO } else { 5418 }),
    [int]$PuertoDeportivo = $(if ($env:CACC_E2E_PUERTO_DEPORTIVO) { [int]$env:CACC_E2E_PUERTO_DEPORTIVO } else { 5419 }),
    # Los generadores mensuales (procesos extra del Administrativo con GeneracionCuotas:Habilitada=true) usan este puerto y el siguiente.
    [int]$PuertoGenerador = 5431,
    [string]$ClaveCuentasPrueba = $(if ($env:CACC_E2E_CLAVE_CUENTAS) { $env:CACC_E2E_CLAVE_CUENTAS } else { "Camioneros.Test.2026!" }),
    [int]$Iteraciones = 10,
    [int[]]$Cargas = @(10, 20, 50),
    [int]$TimeoutSegundos = 90,
    [string]$Salida = $(Join-Path $env:TEMP ("cacc-hu066\ejecucion_{0:yyyyMMdd_HHmmss}" -f (Get-Date))),
    [string]$Evidencia = ""
)

$ErrorActionPreference = "Stop"
# Un error fuera de los escenarios también deja dónde ocurrió (si no, PowerShell muestra solo el mensaje).
trap { Write-Host "ERROR NO CONTROLADO: $($_.Exception.Message)`n$($_.ScriptStackTrace)" -ForegroundColor Red; break }
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (-not $AdministrativoDll) { $AdministrativoDll = Join-Path $repo "ApiGestion\bin\Debug\net9.0\ApiGestion.dll" }
if (-not $DeportivoDll) { $DeportivoDll = Join-Path $env:TEMP "cacc-hu059\trabajo\ellos\BackEnd\CACC.API\bin\Debug\net9.0\CACC.API.dll" }
New-Item -ItemType Directory -Force -Path $Salida | Out-Null
Add-Type -AssemblyName System.Net.Http
$inicioCorrida = Get-Date

function Fatal([string]$Mensaje) { Write-Host "ENTORNO NO DISPONIBLE: $Mensaje" -ForegroundColor Red; exit 2 }
foreach ($archivo in @($AdministrativoDll, $DeportivoDll)) { if (-not (Test-Path $archivo)) { Fatal "no encuentro $archivo (ver 'Arranque' en database/InstructivoEquipo.md, §7.2)." } }
if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) { Fatal "falta sqlcmd en el PATH." }
# Un puerto ocupado (por ejemplo, una API de una corrida anterior que quedó viva) haría que la suite le hable a ESE proceso.
$ocupados = @(@($PuertoAdministrativo, $PuertoDeportivo, $PuertoGenerador, ($PuertoGenerador + 1)) | Where-Object { Get-NetTCPConnection -State Listen -LocalPort $_ -ErrorAction SilentlyContinue })
if ($ocupados.Count) { Fatal "puertos ocupados: $($ocupados -join ', ') (procesos: $((@($ocupados | ForEach-Object { (Get-NetTCPConnection -State Listen -LocalPort $_).OwningProcess } | Sort-Object -Unique)) -join ', '))." }

# Un binario más viejo que su código daría evidencia de OTRO código: cada proyecto se compara con su DLL en la salida de la API.
$bin = Split-Path $AdministrativoDll
foreach ($proyecto in "ApiGestion", "DaoLibrary", "ServiceLibrary", "EntityLibrary") {
    $dll = Get-Item (Join-Path $bin "$proyecto.dll")
    $fuente = Get-ChildItem (Join-Path $repo $proyecto) -Recurse -File -Include *.cs | Where-Object { $_.FullName -notlike "*\bin\*" -and $_.FullName -notlike "*\obj\*" } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($fuente.LastWriteTime -gt $dll.LastWriteTime) { Fatal "$proyecto.dll ($($dll.LastWriteTime)) es más viejo que $($fuente.Name) ($($fuente.LastWriteTime)): compilar antes (dotnet build ApiGestion)." }
}

. (Join-Path $repo "qa\HU-059\comun.ps1")

# ---------------------------------------------------------------- piezas de concurrencia (Concurrencia.cs) con el SqlClient de la API
foreach ($d in "Microsoft.Data.SqlClient.Extensions.Abstractions.dll", "Microsoft.Data.SqlClient.Internal.Logging.dll", "Microsoft.SqlServer.Server.dll") { Add-Type -Path (Join-Path $bin $d) }
$sqlClient = Join-Path $bin "runtimes\win\lib\net9.0\Microsoft.Data.SqlClient.dll"
Add-Type -Path $sqlClient
[void][Runtime.InteropServices.NativeLibrary]::Load((Join-Path $bin "runtimes\win-x64\native\Microsoft.Data.SqlClient.SNI.dll"))
$referencias = @($sqlClient, "System.Net.Http", "System.Net.Primitives", "System.Data.Common", "System.Collections", "System.Collections.Concurrent",
    "System.Linq", "System.Threading", "System.Threading.Thread", "System.Runtime", "System.ComponentModel.Primitives", "System.Console")
Add-Type -TypeDefinition ([IO.File]::ReadAllText((Join-Path $PSScriptRoot "Concurrencia.cs"))) -ReferencedAssemblies $referencias -IgnoreWarnings -WarningAction SilentlyContinue
$versionSqlClient = [Reflection.AssemblyName]::GetAssemblyName($sqlClient).Version.ToString()
$cadenaTecnica = "$($cadena)Application Name=HU066-Tecnica;"
$cadenaAdministrativo = "$($cadena)Application Name=CACC-Administrativo;"
$cadenaGenerador = "$($cadena)Application Name=CACC-Administrativo-Generador;"
$cadenaDeportivo = "$($cadena)Application Name=CACC-Deportivo;"

# ---------------------------------------------------------------- preparación (HU-059 + precondiciones de HU-066)
try {
    Write-Host "== Reset de la base $Base (HU-059)" -ForegroundColor Cyan
    & (Join-Path $repo "qa\HU-059\reset_base.ps1") -Servidor $Servidor -Base $Base -AdministrativoDll $AdministrativoDll -Salida $Salida
    $texto = & sqlcmd -S $Servidor -E -C -b -I -W -h -1 -f 65001 -d $Base -i (Join-Path $PSScriptRoot "datos\04_fixtures_hu066.sql") 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "04_fixtures_hu066.sql: $texto" }
    Write-Host "  Precondiciones de HU-066: $($texto.Trim())"
} catch { Fatal "la preparación de la base falló: $($_.Exception.Message)" }

$hoy = [datetime]::ParseExact((Sql "SELECT CONVERT(CHAR(10), CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Argentina Standard Time' AS DATE), 23)"), "yyyy-MM-dd", $null)
$mesActual = New-Object DateTime $hoy.Year, $hoy.Month, 1
$mesAnterior = $mesActual.AddMonths(-1)
$mesTxt = $mesActual.ToString("yyyy-MM-dd"); $mesAntTxt = $mesAnterior.ToString("yyyy-MM-dd")
$nombresMes = @("", "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio", "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre")
$periodoActual = $nombresMes[$mesActual.Month]; $periodoAnterior = $nombresMes[$mesAnterior.Month]
Write-Host "Fecha de negocio (club, SQL Server): $($hoy.ToString('yyyy-MM-dd')) | salida: $Salida"

# ---------------------------------------------------------------- registro de operaciones (evidencia: tiempos, códigos, ids)
$operaciones = New-Object Collections.Generic.List[object]
$rafagas = New-Object Collections.Generic.List[object]
$dniSiguiente = 0
function NuevoDni { $script:dniSiguiente++; "9866{0:0000}" -f $script:dniSiguiente }
$claveStaff = Azar 18

function NuevoPedido([string]$Etiqueta, [string]$Metodo, [string]$Url, [string]$Token, $Cuerpo, $Clave = $null) {
    $p = [Hu066.Pedido]::new(); $p.Etiqueta = $Etiqueta; $p.Metodo = $Metodo; $p.Url = $Url; $p.Token = $Token; $p.Clave = $Clave
    if ($null -ne $Cuerpo) { $p.Cuerpo = $Cuerpo | ConvertTo-Json -Depth 5 -Compress }
    $p
}
function AltaPortal([string]$Dni, [int]$Categoria = 3) {
    NuevoPedido "alta-portal:$Dni" "POST" "$urlAdministrativo/api/players" $tokenAlta @{ firstName = "Prueba"; lastName = "Concurrencia $Dni"; dni = $Dni; birthDate = "2012-05-05"; gender = "Masculino"; categoryId = $Categoria }
}
function AltaDeportivo([string]$Dni, [int]$Categoria = 3, [string]$Genero = "Masculino") {
    NuevoPedido "alta-deportivo:$Dni" "POST" "$urlDeportivo/api/jugadores" $tokenAlta @{ nombre = "Prueba"; apellido = "Concurrencia $Dni"; fechaNacimiento = "2012-05-05"; genero = $Genero; dni = $Dni; categoria = "$Categoria" }
}
function StaffDeportivo([string]$Dni) {
    NuevoPedido "staff-deportivo:$Dni" "POST" "$urlDeportivo/api/staff" $tokenAlta @{ nombre = "Staff"; apellido = "Concurrencia $Dni"; dni = $Dni; email = "hu066.$Dni.$(Get-Random)@example.com"
        contrasenia = $claveStaff; rol = "Director Técnico"; genero = "Masculino"; fechaNacimiento = "1990-01-01"; idCategoria = 3 }
}
# Escribe PERSONAS y después falla (el email ya es de otra cuenta: UX_USUARIOS_email) y revierte: la operación que se usa para
# comprobar que un rollback bajo concurrencia no deja la persona ni su evento de auditoría.
function StaffQueRevierte([string]$Dni) {
    NuevoPedido "rechazo-staff:$Dni" "POST" "$urlDeportivo/api/staff" $tokenAlta @{ nombre = "Staff"; apellido = "Revierte $Dni"; dni = $Dni; email = "superadmin.prueba@ejemplo.com"
        contrasenia = $claveStaff; rol = "Director Técnico"; genero = "Masculino"; fechaNacimiento = "1990-01-01"; idCategoria = 3 }
}
# Las etiquetas llevan J<jugador> y P<pago>: un id de pago puede coincidir con uno de jugador.
function Registrar_Pago([int]$IdJugador, [string]$Periodo, [int]$Anio, [decimal]$Monto) {
    NuevoPedido "registrar:J$IdJugador`:$Periodo`:$Monto" "POST" "$urlAdministrativo/api/pagos/registrar" $tokenPagos @{ idJugador = $IdJugador; periodo = $Periodo; anio = $Anio; monto = $Monto; metodoPago = "Efectivo" }
}
function Cobro([int]$IdJugador, [int[]]$IdsPago) {
    NuevoPedido "cobro:J$IdJugador`:P$($IdsPago -join ',P')" "POST" "$urlAdministrativo/api/pagos/cobro" $tokenPagos @{ idJugador = $IdJugador; idsPago = @($IdsPago); metodoPago = "Efectivo" }
}
function GenerarFaltantes([datetime]$Mes) {
    NuevoPedido "faltantes:$($Mes.ToString('yyyy-MM'))" "POST" "$urlAdministrativo/api/cuotas-faltantes/$($Mes.Year)/$($Mes.Month)/generar" $tokenAlta $null
}
function BajaDeportivo([int]$IdJugador) { NuevoPedido "baja-deportivo:$IdJugador" "DELETE" "$urlDeportivo/api/jugadores/$IdJugador" $tokenAlta $null }

# Precondición: N jugadores (masculino, categoría 3) con fecha de alta dada y, opcionalmente, cuotas PENDIENTES de los meses
# indicados. Devuelve [ordered] dni -> id de jugador.
function Precondicion([int]$N, [string]$Alta, [string[]]$Pendientes = @()) {
    $dnis = 1..$N | ForEach-Object { NuevoDni }
    $valores = ($dnis | ForEach-Object { "(N'Pre', N'Condicion $_', '$_', 'Masculino', '2012-03-03')" }) -join ","
    $lista = ($dnis | ForEach-Object { "'$_'" }) -join ","
    $cuotas = if ($Pendientes.Count -gt 0) {
        "INSERT PAGOS (FK_id_jugador, monto_base, monto_final, fecha_vencimiento, estado, concepto) SELECT j.PK_id_jugador, 1000, 1000, m.mes, 0, 'Cuota'
         FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona CROSS JOIN (VALUES $(($Pendientes | ForEach-Object { "(CAST('$_' AS date))" }) -join ',')) m(mes) WHERE p.dni IN ($lista);"
    } else { "" }
    $filas = Sql "SET XACT_ABORT ON; BEGIN TRANSACTION;
        INSERT PERSONAS (nombre, apellido, dni, genero, fecha_de_nacimiento) VALUES $valores;
        INSERT JUGADORES (FK_id_persona, FK_id_categoria, fecha_alta) SELECT PK_id_persona, 3, '$Alta' FROM PERSONAS WHERE dni IN ($lista);
        INSERT JUGADORES_CATEGORIAS (FK_id_jugador, FK_id_categoria) SELECT j.PK_id_jugador, 3 FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni IN ($lista);
        $cuotas
        COMMIT;
        SELECT CONCAT(p.dni, ':', j.PK_id_jugador) FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni IN ($lista) ORDER BY p.dni;"
    $mapa = [ordered]@{}
    foreach ($f in ($filas -split "`r?`n")) { $par = $f.Trim() -split ":"; if ($par.Count -eq 2) { $mapa[$par[0]] = [int]$par[1] } }
    $mapa
}

# ---------------------------------------------------------------- ráfagas
function Tiempos($Resultados) {
    $ms = @($Resultados | ForEach-Object { $_.Ms } | Sort-Object)
    if ($ms.Count -eq 0) { return [pscustomobject]@{ n = 0 } }
    $inicios = @($Resultados | ForEach-Object { $_.InicioUtc }); $fines = @($Resultados | ForEach-Object { $_.FinUtc })
    $maxInicio = ($inicios | Measure-Object -Maximum).Maximum; $minFin = ($fines | Measure-Object -Minimum).Minimum
    [pscustomobject]@{
        n = $ms.Count; p50 = [math]::Round($ms[[int][math]::Floor(($ms.Count - 1) * 0.5)], 1); p95 = [math]::Round($ms[[int][math]::Floor(($ms.Count - 1) * 0.95)], 1)
        max = [math]::Round($ms[-1], 1); dispersionInicioMs = [math]::Round((($maxInicio - ($inicios | Measure-Object -Minimum).Minimum).TotalMilliseconds), 2)
        # Todas en vuelo a la vez en algún momento: la última en arrancar arrancó antes de que terminara la primera.
        todasSuperpuestas = ($maxInicio -lt $minFin)
        inicioUtc = (($inicios | Measure-Object -Minimum).Minimum).ToString("HH:mm:ss.fff"); finUtc = (($fines | Measure-Object -Maximum).Maximum).ToString("HH:mm:ss.fff")
    }
}
# Lanza la ráfaga con el monitor encendido. -Compuerta: sostiene el bloqueo de cobros hasta que esperan $Esperados sesiones
# (o 10 s); -AntesDeLanzar: arranca algo antes de la ráfaga (el generador) y devuelve cuántas sesiones extra va a sumar.
# -RetenerMs: con compuerta, la sostiene al menos ese tiempo aunque ya esperen todas (para que entre antes una operación que no
# toma el bloqueo).
function Rafaga([string]$Caso, [int]$Iteracion, [object[]]$Pedidos, [switch]$Compuerta, [scriptblock]$AntesDeLanzar, [scriptblock]$Esperar, [int]$RetenerMs = 0) {
    $monitor = [Hu066.Monitor]::new($cadenaTecnica, $Base)
    $esperandoAlLiberar = ""; $msCompuerta = 0
    try {
        $tomada = [Threading.ManualResetEventSlim]::new($false); $liberar = [Threading.ManualResetEventSlim]::new($false)
        $compuertaTarea = $null
        if ($Compuerta) {
            $compuertaTarea = [Hu066.Compuerta]::Sostener($cadenaTecnica, 15000, $tomada, $liberar)
            if (-not $tomada.Wait(15000)) { throw "la compuerta no tomó el bloqueo" }
        }
        $extra = 0
        if ($AntesDeLanzar) {
            $extra = [int](& $AntesDeLanzar)
            # Si hay compuerta, se espera a que el proceso extra (generador) ya esté esperando el bloqueo: así llega a la par.
            if ($Compuerta -and $extra -gt 0) {
                $limite = (Get-Date).AddSeconds(45)
                while ((Get-Date) -lt $limite -and @(([Hu066.Compuerta]::Esperando($cadenaTecnica, $Base) -split ",") | Where-Object { $_ -like "CACC-Administrativo-Generador*" }).Count -lt $extra) { Start-Sleep -Milliseconds 50 }
            }
            # Sin compuerta: se dispara apenas el generador abre su conexión a la base (lo más cerca posible de su transacción).
            if (-not $Compuerta -and $extra -gt 0) {
                $limite = (Get-Date).AddSeconds(45)
                while ((Get-Date) -lt $limite -and [Hu066.Compuerta]::Conectadas($cadenaTecnica, $Base, "CACC-Administrativo-Generador") -lt 1) { Start-Sleep -Milliseconds 5 }
            }
        }
        $tarea = [Hu066.Rafaga]::Iniciar([Hu066.Pedido[]]$Pedidos)
        if ($Compuerta) {
            # Las operaciones que escriben cobros (todas las del Administrativo) quedan esperando; las del Deportivo que no lo
            # toman siguen solas. Se libera cuando esperan todas las que lo usan, o a los 3 s.
            $usanBloqueo = @($Pedidos | Where-Object { $_.Url.StartsWith($urlAdministrativo) -or $_.Etiqueta -like "alta-deportivo*" }).Count + $extra
            $desde = Get-Date; $limite = $desde.AddSeconds(3)
            do { Start-Sleep -Milliseconds 20; $esperandoAlLiberar = [Hu066.Compuerta]::Esperando($cadenaTecnica, $Base) }
            while ((Get-Date) -lt $limite -and (@($esperandoAlLiberar -split "," | Where-Object { $_ }).Count -lt $usanBloqueo -or ((Get-Date) - $desde).TotalMilliseconds -lt $RetenerMs))
            $liberar.Set()
            $msCompuerta = [math]::Round($compuertaTarea.GetAwaiter().GetResult(), 0)
        }
        $resultados = $tarea.GetAwaiter().GetResult()
        if ($Esperar) { & $Esperar | Out-Null }
    } finally { $monitor.Dispose() }
    foreach ($r in $resultados) {
        $operaciones.Add([pscustomobject]@{ caso = $Caso; iteracion = $Iteracion; etiqueta = $r.Etiqueta; codigo = $r.Codigo; ms = [math]::Round($r.Ms, 1)
            inicioUtc = $r.InicioUtc.ToString("HH:mm:ss.fff"); finUtc = $r.FinUtc.ToString("HH:mm:ss.fff"); error = $r.Error
            cuerpo = $(if ($r.Cuerpo) { $r.Cuerpo.Substring(0, [Math]::Min(240, $r.Cuerpo.Length)) }) })
    }
    $esperando = @($esperandoAlLiberar -split "," | Where-Object { $_ })
    $info = [pscustomobject]@{ caso = $Caso; iteracion = $Iteracion; operaciones = $Pedidos.Count; compuerta = [bool]$Compuerta; compuertaMs = $msCompuerta
        esperandoAlLiberar = $esperando.Count; esperandoLista = ($esperando | Group-Object | ForEach-Object { "$($_.Name) x$($_.Count)" }) -join ", "; tiempos = (Tiempos $resultados); monitorMuestras = $monitor.Muestras; maxEsperandoBloqueoCobros = $monitor.MaxEsperandoApplock
        maxBloqueadas = $monitor.MaxBloqueadas; maxEsperaMs = $monitor.MaxEsperaMs; tiposEspera = @($monitor.TiposEspera); bloqueosCruzados = @($monitor.BloqueosCruzados)
        aislamiento = @($monitor.Aislamiento); erroresMonitor = @($monitor.Errores) }
    $rafagas.Add($info)
    [pscustomobject]@{ Resultados = $resultados; Info = $info }
}
function Codigos($Resultados, [string]$Prefijo) { (@($Resultados | Where-Object { $_.Etiqueta -like "$Prefijo*" } | ForEach-Object Codigo | Sort-Object) -join "/") }
function Sin5xx($Resultados) { @($Resultados | Where-Object { $_.Codigo -ge 500 -or $_.Codigo -eq 0 }).Count -eq 0 }
function Errores5xx($Resultados) { (@($Resultados | Where-Object { $_.Codigo -ge 500 -or $_.Codigo -eq 0 } | ForEach-Object { "$($_.Etiqueta) -> $($_.Codigo) $($_.Error) $($_.Cuerpo)" }) -join " || ") }

# ---------------------------------------------------------------- estado por SQL (solo para verificar)
# personas|jugadores|usuarios|cuotas del mes|inscripciones de un DNI, y la categoría del jugador.
function EstadoDni([string]$Dni) {
    Sql "SELECT CONCAT((SELECT COUNT(*) FROM PERSONAS WHERE LTRIM(RTRIM(dni)) = '$Dni'), '|',
        (SELECT COUNT(*) FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni = '$Dni'), '|',
        (SELECT COUNT(*) FROM USUARIOS u JOIN PERSONAS p ON p.PK_id_persona = u.FK_id_persona WHERE p.dni = '$Dni'), '|',
        (SELECT COUNT(*) FROM PAGOS g JOIN JUGADORES j ON j.PK_id_jugador = g.FK_id_jugador JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni = '$Dni' AND g.concepto = 'Cuota' AND g.fecha_vencimiento = '$mesTxt'), '|',
        (SELECT COUNT(*) FROM PAGOS g JOIN JUGADORES j ON j.PK_id_jugador = g.FK_id_jugador JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni = '$Dni' AND g.concepto = 'Inscripcion'), '|',
        ISNULL((SELECT MAX(j.FK_id_categoria) FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni = '$Dni'), 0))"
}
# Una cuota de un mes de un jugador: "pendientes:saldo|abonos:suma|base". Una deuda bien formada: a lo sumo una pendiente y
# saldo + abonos = base (ni cuota duplicada, ni cobro de más, ni abono perdido).
function CuotaMes([int]$IdJugador, [string]$Mes) {
    Sql "SELECT CONCAT(SUM(CASE WHEN estado = 0 THEN 1 ELSE 0 END), ':', CONVERT(int, ISNULL(SUM(CASE WHEN estado = 0 THEN monto_final END), 0)), '|',
        SUM(CASE WHEN estado = 1 THEN 1 ELSE 0 END), ':', CONVERT(int, ISNULL(SUM(CASE WHEN estado = 1 THEN monto_final END), 0)), '|', CONVERT(int, ISNULL(MAX(monto_base), 0)))
        FROM PAGOS WHERE FK_id_jugador = $IdJugador AND concepto = 'Cuota' AND fecha_vencimiento >= '$Mes' AND fecha_vencimiento < DATEADD(MONTH, 1, '$Mes')"
}
function IdsDe([string[]]$Dnis, [string]$Tabla = "JUGADORES") {
    if ($Dnis.Count -eq 0) { return @() }
    $lista = ($Dnis | ForEach-Object { "'$_'" }) -join ","
    $consulta = switch ($Tabla) {
        "JUGADORES" { "SELECT j.PK_id_jugador FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni IN ($lista)" }
        "PERSONAS" { "SELECT PK_id_persona FROM PERSONAS WHERE dni IN ($lista)" }
        "USUARIOS" { "SELECT u.PK_id_usuario FROM USUARIOS u JOIN PERSONAS p ON p.PK_id_persona = u.FK_id_persona WHERE p.dni IN ($lista)" }
    }
    @((Sql $consulta) -split "`r?`n" | Where-Object { $_.Trim() } | ForEach-Object { [int]$_.Trim() })
}

# Integridad referencial y de negocio de TODA la base. Huérfanos por cada FK de una columna del esquema real (SQL dinámico).
$sqlIntegridad = @"
DECLARE @q nvarchar(max) = N'';
SELECT @q = @q + N' UNION ALL SELECT N''' + fk.name + N''', COUNT(*) FROM ' + QUOTENAME(SCHEMA_NAME(h.schema_id)) + N'.' + QUOTENAME(h.name)
    + N' x WHERE x.' + QUOTENAME(hc.name) + N' IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(SCHEMA_NAME(r.schema_id)) + N'.' + QUOTENAME(r.name)
    + N' y WHERE y.' + QUOTENAME(rc.name) + N' = x.' + QUOTENAME(hc.name) + N')'
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns k ON k.constraint_object_id = fk.object_id
JOIN sys.tables h ON h.object_id = fk.parent_object_id JOIN sys.columns hc ON hc.object_id = k.parent_object_id AND hc.column_id = k.parent_column_id
JOIN sys.tables r ON r.object_id = fk.referenced_object_id JOIN sys.columns rc ON rc.object_id = k.referenced_object_id AND rc.column_id = k.referenced_column_id
WHERE (SELECT COUNT(*) FROM sys.foreign_key_columns z WHERE z.constraint_object_id = fk.object_id) = 1;
DECLARE @t TABLE (fk sysname, n int);
SET @q = STUFF(@q, 1, 11, N'');
INSERT @t EXEC (@q);
SELECT CONCAT(
 'fk=', (SELECT COUNT(*) FROM @t),
 ';fkMulticolumna=', (SELECT COUNT(*) FROM sys.foreign_keys fk WHERE (SELECT COUNT(*) FROM sys.foreign_key_columns z WHERE z.constraint_object_id = fk.object_id) > 1),
 ';huerfanosFk=', (SELECT ISNULL(SUM(n), 0) FROM @t),
 ';huerfanosDetalle=', ISNULL((SELECT STRING_AGG(CONCAT(fk, ':', n), ',') FROM @t WHERE n > 0), ''),
 ';fkDeshabilitadasONoConfiables=', (SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled = 1 OR is_not_trusted = 1),
 ';triggers=', (SELECT COUNT(*) FROM sys.triggers WHERE parent_class = 1),
 ';triggersDeshabilitados=', (SELECT COUNT(*) FROM sys.triggers WHERE parent_class = 1 AND is_disabled = 1),
 ';jugadoresSinPersona=', (SELECT COUNT(*) FROM JUGADORES j WHERE j.FK_id_persona IS NULL OR NOT EXISTS (SELECT 1 FROM PERSONAS p WHERE p.PK_id_persona = j.FK_id_persona)),
 ';jugadoresCategoriaInvalida=', (SELECT COUNT(*) FROM JUGADORES j WHERE j.FK_id_categoria IS NULL OR NOT EXISTS (SELECT 1 FROM CATEGORIAS c WHERE c.PK_id_categoria = j.FK_id_categoria)),
 ';pagosSinJugador=', (SELECT COUNT(*) FROM PAGOS g WHERE g.FK_id_jugador IS NULL OR NOT EXISTS (SELECT 1 FROM JUGADORES j WHERE j.PK_id_jugador = g.FK_id_jugador)),
 ';usuariosSinPersona=', (SELECT COUNT(*) FROM USUARIOS u WHERE u.FK_id_persona IS NOT NULL AND NOT EXISTS (SELECT 1 FROM PERSONAS p WHERE p.PK_id_persona = u.FK_id_persona)),
 ';dniDuplicado=', (SELECT COUNT(*) FROM (SELECT LTRIM(RTRIM(dni)) d FROM PERSONAS WHERE dni IS NOT NULL GROUP BY LTRIM(RTRIM(dni)) HAVING COUNT(*) > 1) x),
 ';jugadorDuplicadoPorPersona=', (SELECT COUNT(*) FROM (SELECT FK_id_persona FROM JUGADORES WHERE FK_id_persona IS NOT NULL GROUP BY FK_id_persona HAVING COUNT(*) > 1) x),
 ';cuotaPendienteDuplicada=', (SELECT COUNT(*) FROM (SELECT 1 n FROM PAGOS WHERE estado = 0 AND concepto = 'Cuota' GROUP BY FK_id_jugador, YEAR(fecha_vencimiento), MONTH(fecha_vencimiento) HAVING COUNT(*) > 1) x),
 ';cuotaSobreemitida=', (SELECT COUNT(*) FROM (SELECT 1 n FROM PAGOS WHERE concepto = 'Cuota' AND fecha_vencimiento IS NOT NULL GROUP BY FK_id_jugador, YEAR(fecha_vencimiento), MONTH(fecha_vencimiento) HAVING SUM(monto_final) > MAX(monto_base)) x),
 ';inscripcionSobreemitida=', (SELECT COUNT(*) FROM (SELECT 1 n FROM PAGOS WHERE concepto = 'Inscripcion' GROUP BY FK_id_jugador, fecha_vencimiento HAVING SUM(monto_final) > MAX(monto_base)) x),
 ';periodoAbiertoDuplicado=', (SELECT COUNT(*) FROM (SELECT 1 n FROM JUGADORES_PERIODOS WHERE fecha_baja IS NULL GROUP BY FK_id_jugador HAVING COUNT(*) > 1) x),
 ';activoSinPeriodo=', (SELECT COUNT(*) FROM JUGADORES j WHERE j.fecha_baja IS NULL AND NOT EXISTS (SELECT 1 FROM JUGADORES_PERIODOS p WHERE p.FK_id_jugador = j.PK_id_jugador AND p.fecha_baja IS NULL)),
 ';auditoriaFantasma=', (SELECT COUNT(*) FROM AUDITORIA_CAMBIOS a WHERE a.accion = 'INSERT' AND a.entidad IN ('PERSONAS', 'JUGADORES', 'PAGOS')
        AND NOT EXISTS (SELECT 1 FROM AUDITORIA_CAMBIOS d WHERE d.entidad = a.entidad AND d.id_entidad = a.id_entidad AND d.accion = 'DELETE')
        AND NOT EXISTS (SELECT 1 FROM PERSONAS p WHERE a.entidad = 'PERSONAS' AND p.PK_id_persona = TRY_CONVERT(int, a.id_entidad))
        AND NOT EXISTS (SELECT 1 FROM JUGADORES j WHERE a.entidad = 'JUGADORES' AND j.PK_id_jugador = TRY_CONVERT(int, a.id_entidad))
        AND NOT EXISTS (SELECT 1 FROM PAGOS g WHERE a.entidad = 'PAGOS' AND g.PK_id_pago = TRY_CONVERT(int, a.id_entidad))),
 ';secuenciaJugadoresOk=', (SELECT CASE WHEN CONVERT(bigint, current_value) >= (SELECT ISNULL(MAX(PK_id_jugador), 0) FROM JUGADORES) THEN 1 ELSE 0 END FROM sys.sequences WHERE name = 'SEQ_JUGADORES'),
 ';secuenciaPagosOk=', (SELECT CASE WHEN CONVERT(bigint, current_value) >= (SELECT ISNULL(MAX(PK_id_pago), 0) FROM PAGOS) THEN 1 ELSE 0 END FROM sys.sequences WHERE name = 'SEQ_PAGOS'),
 ';personas=', (SELECT COUNT(*) FROM PERSONAS), ';jugadores=', (SELECT COUNT(*) FROM JUGADORES), ';usuarios=', (SELECT COUNT(*) FROM USUARIOS),
 ';pagos=', (SELECT COUNT(*) FROM PAGOS), ';eventos=', (SELECT COUNT(*) FROM AUDITORIA_CAMBIOS))
"@
function Integridad {
    $h = [ordered]@{}
    foreach ($par in ((Sql $sqlIntegridad) -split ";")) { $kv = $par -split "=", 2; $h[$kv[0]] = $kv[1] }
    $h
}
# Lo que tiene que ser 0 (o 1 en las secuencias) DESPUÉS de cada escenario.
$clavesCero = "huerfanosFk", "fkDeshabilitadasONoConfiables", "triggersDeshabilitados", "jugadoresSinPersona", "jugadoresCategoriaInvalida", "pagosSinJugador",
    "usuariosSinPersona", "dniDuplicado", "jugadorDuplicadoPorPersona", "cuotaPendienteDuplicada", "cuotaSobreemitida", "inscripcionSobreemitida",
    "periodoAbiertoDuplicado", "activoSinPeriodo", "auditoriaFantasma"
function VerificarIntegridad([string]$Grupo, [string]$Id, $Antes) {
    $despues = Integridad
    $nuevos = @($clavesCero | Where-Object { [int]$despues[$_] -gt [int]$Antes[$_] } | ForEach-Object { "$_ $($Antes[$_])->$($despues[$_])" })
    $secuencias = ($despues.secuenciaJugadoresOk -eq "1" -and $despues.secuenciaPagosOk -eq "1")
    Verificar $Grupo $Id "Integridad después del escenario: 0 huérfanos nuevos, 0 duplicados, FK y triggers activos, secuencias por delante de los ids" `
        ($nuevos.Count -eq 0 -and $secuencias) "0 problemas nuevos; secuencias ok" "$(if ($nuevos.Count) { $nuevos -join ', ' } else { '0 nuevos' }); secuencias $($despues.secuenciaJugadoresOk)/$($despues.secuenciaPagosOk)" `
        "huérfanos FK $($despues.huerfanosFk) ($($despues.huerfanosDetalle)) | personas $($Antes.personas)->$($despues.personas), jugadores $($Antes.jugadores)->$($despues.jugadores), pagos $($Antes.pagos)->$($despues.pagos), eventos $($Antes.eventos)->$($despues.eventos)"
    $despues
}

# ---------------------------------------------------------------- generadores mensuales (HU-049): procesos reales del Administrativo
$numeroGenerador = 0
$generadores = New-Object Collections.Generic.List[object]
function EntornoAdm([bool]$Generador, [string]$CadenaApp) {
    $e = EntornoAdministrativo $Generador
    $e["ConnectionStrings__ConexionSQL"] = $CadenaApp
    $e["Logging__Console__FormatterOptions__TimestampFormat"] = "HH:mm:ss.fff "
    $e
}
function IniciarGeneradores([int]$Cantidad) {
    for ($i = 0; $i -lt $Cantidad; $i++) {
        $script:numeroGenerador++
        $g = Iniciar "generador_$($script:numeroGenerador)" $AdministrativoDll ($PuertoGenerador + $i) (EntornoAdm $true $cadenaGenerador)
        $generadores.Add($g)
    }
    $Cantidad
}
# Espera a que los generadores terminen su primer turno (el sitio de cada uno responde) y los detiene. Devuelve sus logs.
function TerminarGeneradores {
    $logs = @()
    foreach ($g in $generadores.ToArray()) {
        $url = "http://localhost:$($g.Puerto)"
        $listo = Esperar "generador" { $r = Pedir "POST" "$url/api/auth/login" $null $null @{ username = "20111222"; password = $ClaveCuentasPrueba }; [pscustomobject]@{ Ok = ($r.Codigo -eq 200); Detalle = "login -> $($r.Codigo)" } } $g
        $texto = Detener $g
        $logs += [pscustomobject]@{ Nombre = $g.Nombre; Listo = $listo.Ok; Verificadas = [regex]::IsMatch($texto, "Cuotas del mes \d\d/\d{4} verificadas")
            Error = [regex]::IsMatch($texto, "No se pudieron generar las cuotas"); Linea = ([regex]::Match($texto, "(\d\d:\d\d:\d\d\.\d{3}) [^\r\n]*\r?\n[^\r\n]*Cuotas del mes[^\r\n]*")).Value -replace "\s+", " " }
    }
    $generadores.Clear()
    $logs
}

# ---------------------------------------------------------------- arranque
$administrativo = $null; $deportivo = $null
$ids = [ordered]@{}
$bugs = New-Object Collections.Generic.List[object]
try {
    Write-Host "== Levantando las APIs" -ForegroundColor Cyan
    $administrativo = Iniciar "administrativo" $AdministrativoDll $PuertoAdministrativo (EntornoAdm $false $cadenaAdministrativo)
    $entornoDep = EntornoDeportivo $credencial
    $entornoDep["ConnectionStrings__DefaultConnection"] = $cadenaDeportivo
    $deportivo = Iniciar "deportivo" $DeportivoDll $PuertoDeportivo $entornoDep
    $saludAdm = Esperar "Administrativo" { Login } $administrativo
    Verificar "Entorno" "ENV-1" "API Administrativa lista (login del SuperAdmin: lee USUARIOS en SQL)" $saludAdm.Ok "200" $saludAdm.Detalle
    if (-not $saludAdm.Ok) { throw "La API Administrativa no quedó lista: $($saludAdm.Detalle)" }
    $tokenAlta = $saludAdm.Token
    $loginPagos = Pedir "POST" "$urlAdministrativo/api/auth/login" $null $null @{ username = "30000005"; password = $ClaveCuentasPrueba }
    $tokenPagos = (Json $loginPagos.Cuerpo).token
    Verificar "Entorno" "ENV-2" "Login del Administrador 30000005 (rol 2): opera los pagos, para distinguir operadores en la auditoría" ($loginPagos.Codigo -eq 200 -and $tokenPagos) "200" $loginPagos.Codigo
    $saludDep = Esperar "Deportivo" { $r = Pedir "GET" "$urlDeportivo/api/jugadores" $tokenAlta $null $null; [pscustomobject]@{ Ok = ($r.Codigo -eq 200); Detalle = "GET /api/jugadores -> $($r.Codigo)" } } $deportivo
    Verificar "Entorno" "ENV-3" "API Deportiva lista (GET /api/jugadores: lee JUGADORES en SQL)" $saludDep.Ok "200" $saludDep.Detalle
    if (-not $saludDep.Ok) { throw "La API Deportiva no quedó lista: $($saludDep.Detalle)" }
    $idSuperAdmin = [int](Sql "SELECT u.PK_id_usuario FROM USUARIOS u JOIN PERSONAS p ON p.PK_id_persona = u.FK_id_persona WHERE p.dni = '20111222'")
    $idAdministrador = [int](Sql "SELECT u.PK_id_usuario FROM USUARIOS u JOIN PERSONAS p ON p.PK_id_persona = u.FK_id_persona WHERE p.dni = '30000005'")
    $ids.superAdmin = $idSuperAdmin; $ids.administradorPagos = $idAdministrador

    # ============================================================ dependencias y configuración real (evidencia)
    $g = "Dependencias"
    $sonda = Pedir "POST" "$urlAdministrativo/api/integracion/v1/jugadores" $tokenAlta $credencial @{}
    Registrar $g "DEP-1" "HU-056: POST /api/integracion/v1/jugadores existe (400 con errorCode)" $(if ($sonda.Codigo -eq 400 -and (Json $sonda.Cuerpo).errorCode) { "PASS" } else { "BLOCKED" }) "400 + errorCode" "$($sonda.Codigo)" $sonda.Cuerpo
    $sinClave = Pedir "POST" "$urlAdministrativo/api/integracion/v1/jugadores" $tokenAlta $null @{}
    Verificar $g "DEP-2" "HU-055: sin X-Integration-Key la integración responde 401 (las pruebas usan el flujo autenticado real)" ($sinClave.Codigo -eq 401) "401" $sinClave.Codigo
    $config = Sql "SELECT CONCAT(@@VERSION, '||', (SELECT CONCAT(is_read_committed_snapshot_on, '|', snapshot_isolation_state_desc) FROM sys.databases WHERE name = DB_NAME()))"
    $version = ($config -split "\|\|")[0] -replace "\s+", " "; $aislamientoBase = ($config -split "\|\|")[1]
    $secuencias = Sql "SELECT STRING_AGG(CONCAT(s.name, ' cache=', s.is_cached, ' actual=', CONVERT(bigint, s.current_value)), ', ') FROM sys.sequences s"
    $defaults = Sql "SELECT STRING_AGG(CONCAT(OBJECT_NAME(d.parent_object_id), '.', c.name, '=', d.definition), ', ') FROM sys.default_constraints d JOIN sys.columns c ON c.object_id = d.parent_object_id AND c.column_id = d.parent_column_id WHERE d.definition LIKE '%NEXT VALUE FOR%'"
    Verificar $g "SEQ-0" "HU-053: PK de JUGADORES y PAGOS con DEFAULT NEXT VALUE FOR (SEQ_JUGADORES / SEQ_PAGOS, NO CACHE)" `
        ($defaults -match "JUGADORES\.PK_id_jugador=\(NEXT VALUE FOR \[dbo\]\.\[SEQ_JUGADORES\]\)" -and $defaults -match "PAGOS\.PK_id_pago=\(NEXT VALUE FOR \[dbo\]\.\[SEQ_PAGOS\]\)" -and $secuencias -notmatch "cache=1") "2 defaults de secuencia, sin caché" "$defaults | $secuencias"
    $trigs = Sql "SELECT STRING_AGG(CONCAT(OBJECT_NAME(parent_id), ':', name, CASE WHEN is_disabled = 1 THEN ' (DESHABILITADO)' ELSE '' END), ', ') WITHIN GROUP (ORDER BY OBJECT_NAME(parent_id), name) FROM sys.triggers WHERE parent_class = 1"
    $xe = Sql "SELECT COUNT(*) FROM sys.dm_xe_sessions WHERE name = 'system_health'"
    Registrar $g "DL-0" "Detección de deadlocks disponible (sesión system_health de Extended Events activa)" $(if ($xe -eq "1") { "PASS" } else { "BLOCKED" }) "1" $xe
    $ids.versionSql = $version; $ids.aislamientoBase = $aislamientoBase; $ids.secuencias = $secuencias; $ids.triggers = $trigs; $ids.sqlClientSuite = $versionSqlClient
    $inicioPruebasUtc = (Sql "SELECT CONVERT(varchar(30), SYSUTCDATETIME(), 126)")
    $esperasAntes = Sql "SELECT CONCAT(SUM(CASE WHEN wait_type LIKE 'LCK_M_%' THEN waiting_tasks_count ELSE 0 END), '|', SUM(CASE WHEN wait_type LIKE 'LCK_M_%' THEN wait_time_ms ELSE 0 END)) FROM sys.dm_os_wait_stats"
    $integridadInicial = Integridad
    $ids.integridadInicial = $integridadInicial
    $historicos = @($clavesCero | Where-Object { $_ -notin "triggersDeshabilitados", "fkDeshabilitadasONoConfiables" -and [int]$integridadInicial[$_] -gt 0 } | ForEach-Object { "$_=$($integridadInicial[$_])" })
    Registrar $g "INT-0" "Línea de base de integridad (problemas históricos previos, se documentan aparte y no se atribuyen a HU-066)" "PASS" "registrado" $(if ($historicos.Count) { $historicos -join ', ' } else { "sin problemas previos" }) "$($integridadInicial.fk) FK de una columna, $($integridadInicial.triggers) triggers"

    # ============================================================ técnicas (SQL directo, separadas de los flujos funcionales)
    $g = "Técnicas SQL"
    foreach ($seq in "dbo.SEQ_JUGADORES", "dbo.SEQ_PAGOS") {
        $valores = [Hu066.Tecnicas]::ValoresDeSecuencia($cadenaTecnica, $seq, 16, 100)
        $distintos = @($valores | Sort-Object -Unique).Count
        Verificar $g "SEQ-T$($seq[-1])" "$seq`: 16 sesiones simultáneas x 100 NEXT VALUE FOR -> 1600 valores distintos" ($valores.Count -eq 1600 -and $distintos -eq 1600) "1600 distintos" "$($valores.Count) valores, $distintos distintos"
    }
    $pool = [Hu066.Tecnicas]::ContextoTrasReutilizar($cadenaTecnica) -split "\|"
    Verificar $g "CTX-1" "Pool de conexiones: la MISMA sesión física reutilizada no conserva idUsuario/origen de la operación anterior" `
        ($pool[0] -eq $pool[1] -and $pool[2] -eq "NULL" -and $pool[3] -eq "NULL") "mismo SPID; NULL|NULL" "SPID $($pool[0])->$($pool[1]); $($pool[2])|$($pool[3])" "SqlClient $versionSqlClient"
    $ctx = [Hu066.Tecnicas]::ContextoBajoConcurrencia($cadenaTecnica, 16, 50) -split "\|", 4
    Verificar $g "CTX-2" "Pool chico (4) con 16 hilos: la mitad setea operador/origen y la otra mitad no; nadie lee el de otra operación" `
        ([int]$ctx[2] -eq 0 -and [int]$ctx[1] -gt 0) "0 contaminadas" "$($ctx[0]) aperturas, $($ctx[1]) reutilizaciones, $($ctx[2]) contaminadas" $ctx[3]

    # ============================================================ Caso 1: altas de personas distintas simultáneas (Administrativo + Deportivo)
    $g = "Caso 1 altas distintas"
    $antes = Integridad
    $c1Ok = 0; $c1Detalle = @(); $c1Ids = @(); $revertidas = @(); $codigosRevertidas = @{}
    for ($it = 1; $it -le $Iteraciones; $it++) {
        $dP = NuevoDni; $dD = NuevoDni; $dS = NuevoDni; $dR = NuevoDni
        $r = Rafaga "C1" $it @((AltaPortal $dP), (AltaDeportivo $dD), (StaffDeportivo $dS), (StaffQueRevierte $dR))
        $eP = EstadoDni $dP; $eD = EstadoDni $dD; $eS = EstadoDni $dS; $eR = EstadoDni $dR
        $validas = @($r.Resultados | Where-Object { $_.Etiqueta -notlike "rechazo-*" })
        $rechazo = @($r.Resultados | Where-Object { $_.Etiqueta -like "rechazo-*" })[0]
        $codigosRevertidas["$($rechazo.Codigo)"] = 1 + [int]$codigosRevertidas["$($rechazo.Codigo)"]
        $revertidas += $dR
        $ok = (Codigos $validas "") -eq "201/201/201" -and $eP -eq "1|1|0|1|1|3" -and $eD -eq "1|1|0|1|1|3" -and $eS -eq "1|0|1|0|0|0" -and $rechazo.Codigo -ne 201 -and $eR -eq "0|0|0|0|0|0"
        if ($ok) { $c1Ok++ } else { $c1Detalle += "it$it $(Codigos $r.Resultados ''): $dP=$eP $dD=$eD $dS=$eS revierte $dR=$eR $(Errores5xx $validas)" }
        $c1Ids += IdsDe @($dP, $dD) "JUGADORES"
    }
    Verificar $g "C1-1" "$Iteraciones iteraciones: alta del portal + alta del Deportivo + staff del Deportivo + un staff que revierte, a la vez -> 201 x3, una persona por DNI, jugador con categoría y cuota del mes, staff sin jugador; el que revierte no deja nada" `
        ($c1Ok -eq $Iteraciones) "$Iteraciones/$Iteraciones" "$c1Ok/$Iteraciones" ($c1Detalle -join " || ")
    $ids.rechazoStaffCodigos = ($codigosRevertidas.GetEnumerator() | ForEach-Object { "$($_.Key) x$($_.Value)" }) -join ", "
    foreach ($n in $Cargas) {
        $dnis = 1..$n | ForEach-Object { NuevoDni }
        $pedidos = for ($i = 0; $i -lt $n; $i++) { switch ($i % 3) { 0 { AltaPortal $dnis[$i] } 1 { AltaDeportivo $dnis[$i] } 2 { StaffDeportivo $dnis[$i] } } }
        $r = Rafaga "C1-carga" $n $pedidos
        $malos = @(for ($i = 0; $i -lt $n; $i++) { $e = EstadoDni $dnis[$i]; $esp = if ($i % 3 -eq 2) { "1|0|1|0|0|0" } else { "1|1|0|1|1|3" }; if ($e -ne $esp) { "$($dnis[$i])=$e" } })
        $idsJ = IdsDe @($dnis | Where-Object { $dnis.IndexOf($_) % 3 -ne 2 }) "JUGADORES"
        $idsP = IdsDe $dnis "PERSONAS"
        $c1Ids += $idsJ
        $ok = (@($r.Resultados | Where-Object Codigo -ne 201).Count -eq 0) -and $malos.Count -eq 0 -and @($idsP | Sort-Object -Unique).Count -eq $n -and @($idsJ | Sort-Object -Unique).Count -eq ($n - [math]::Floor($n / 3))
        Verificar $g "C1-$n" "Ráfaga de $n altas simultáneas (portal / Deportivo / staff): todas 201, $n personas distintas, ningún registro perdido" $ok "$n x 201; 0 estados incorrectos" `
            "$(@($r.Resultados | Where-Object Codigo -eq 201).Count) x 201; $($malos.Count) incorrectos; personas $(@($idsP | Sort-Object -Unique).Count), jugadores $(@($idsJ | Sort-Object -Unique).Count)" "$($malos -join ', ') $(Errores5xx $r.Resultados)"
    }
    $distintos = @($c1Ids | Sort-Object -Unique).Count
    Verificar $g "SEQ-1" "Los PK_id_jugador de todas las altas simultáneas son distintos y salen de SEQ_JUGADORES (<= current_value)" `
        ($distintos -eq $c1Ids.Count -and ([long](Sql "SELECT CONVERT(bigint, current_value) FROM sys.sequences WHERE name = 'SEQ_JUGADORES'")) -ge ($c1Ids | Measure-Object -Maximum).Maximum) "$($c1Ids.Count) distintos" "$distintos distintos de $($c1Ids.Count)" ("ids: " + (($c1Ids | Sort-Object) -join ","))
    $ids.c1Jugadores = ($c1Ids | Sort-Object) -join ","
    VerificarIntegridad $g "C1-INT" $antes | Out-Null

    # ============================================================ Caso 2: el mismo DNI a la vez desde los dos sistemas
    $g = "Caso 2 mismo DNI"
    $antes = Integridad
    $variantes = @(
        @{ Nombre = "portal + Deportivo"; Arma = { param($d) @((AltaPortal $d), (AltaDeportivo $d)) }; Jugador = 1; Usuario = 0 },
        @{ Nombre = "staff del Deportivo (SQL directo) + alta del Deportivo"; Arma = { param($d) @((StaffDeportivo $d), (AltaDeportivo $d)) }; Jugador = 1; Usuario = 1 },
        # El portal rechaza un DNI que ya existe (400 "Ya hay una persona registrada", política explícita): si el staff gana, queda
        # la persona del staff sin jugador. El staff hashea la contraseña antes de escribir, así que con la compuerta normal llega
        # tarde siempre: en las iteraciones con compuerta se la sostiene 1,5 s para que el staff escriba mientras el portal espera.
        @{ Nombre = "alta del portal + staff del Deportivo"; Arma = { param($d) @((AltaPortal $d), (StaffDeportivo $d)) }; Jugador = -1; Usuario = 1; RetenerMs = 1500 }
    )
    foreach ($v in $variantes) {
        $ok = 0; $detalle = @(); $codigosVistos = @{}
        for ($it = 1; $it -le $Iteraciones; $it++) {
            $d = NuevoDni
            # Iteraciones impares con compuerta: las del Administrativo esperan juntas y el staff (que no toma el bloqueo) entra primero.
            $r = Rafaga "C2:$($v.Nombre)" $it (& $v.Arma $d) -Compuerta:($it % 2 -eq 1) -RetenerMs ([int]$v.RetenerMs)
            $e = (EstadoDni $d) -split "\|"
            $codigos = ($r.Resultados | ForEach-Object { "$(($_.Etiqueta -split ':')[0])=$($_.Codigo)" }) -join ","
            $codigosVistos[$codigos] = 1 + [int]$codigosVistos[$codigos]
            $jug = $v.Jugador
            if ($jug -lt 0) {
                $portal = @($r.Resultados | Where-Object { $_.Etiqueta -like "alta-portal*" })[0].Codigo
                $jug = if ($portal -eq 201) { 1 } elseif ($portal -in 400, 409 -and $e[2] -eq "1") { 0 } else { 99 }
            }
            $bien = (Sin5xx $r.Resultados) -and $e[0] -eq "1" -and $e[1] -eq "$jug" -and [int]$e[2] -le $v.Usuario -and $e[3] -eq "$jug" -and $e[4] -eq "$jug"
            if ($bien) { $ok++ } else { $detalle += "it$it $codigos estado=$($e -join '|') $(Errores5xx $r.Resultados)" }
        }
        $id = "C2-" + ($variantes.IndexOf($v) + 1)
        Verificar $g $id "$($v.Nombre), mismo DNI nuevo ($Iteraciones iteraciones): UNA persona, $(if ($v.Jugador -lt 0) { 'un jugador si el portal gana (si no, 400 y la persona del staff)' } else { 'un jugador' }) con una cuota y una inscripción, sin errores 5xx (conflicto controlado o reutilización)" `
            ($ok -eq $Iteraciones) "$Iteraciones/$Iteraciones" "$ok/$Iteraciones" ("resultados: " + (($codigosVistos.GetEnumerator() | ForEach-Object { "$($_.Key) x$($_.Value)" }) -join "; ") + " || " + ($detalle -join " || "))
    }
    foreach ($n in $Cargas | Where-Object { $_ -le 20 }) {
        $d = NuevoDni
        $pedidos = for ($i = 0; $i -lt $n; $i++) { if ($i % 2) { AltaPortal $d } else { AltaDeportivo $d } }
        $r = Rafaga "C2-carga" $n $pedidos
        $e = EstadoDni $d
        $creados = @($r.Resultados | Where-Object Codigo -eq 201).Count
        Verificar $g "C2-$n" "$n altas simultáneas del MISMO DNI (mitad portal, mitad Deportivo): una persona, un jugador, una cuota, una inscripción; un solo 201" `
            ((Sin5xx $r.Resultados) -and $e -eq "1|1|0|1|1|3" -and $creados -eq 1) "1|1|0|1|1|3; un 201" "$e; $creados x 201; códigos $(Codigos $r.Resultados '')" (Errores5xx $r.Resultados)
    }
    VerificarIntegridad $g "C2-INT" $antes | Out-Null

    # ============================================================ Caso 3: misma identidad existente -> un solo JUGADOR
    $g = "Caso 3 jugadores"
    $antes = Integridad
    $ok = 0; $detalle = @(); $vistos = @{}
    for ($it = 1; $it -le [math]::Min($Iteraciones, 20); $it++) {
        $d = "9869{0:0000}" -f $it
        $persona = Sql "SELECT PK_id_persona FROM PERSONAS WHERE dni = '$d'"
        $r = Rafaga "C3" $it @((AltaDeportivo $d), (AltaDeportivo $d), (AltaPortal $d)) -Compuerta:($it % 2 -eq 0)
        $e = EstadoDni $d
        $fk = Sql "SELECT CONCAT(COUNT(*), '|', MAX(j.FK_id_persona), '|', MAX(j.FK_id_categoria)) FROM JUGADORES j WHERE j.FK_id_persona = $persona"
        $codigos = Codigos $r.Resultados ""
        $vistos[$codigos] = 1 + [int]$vistos[$codigos]
        if ((Sin5xx $r.Resultados) -and $e -eq "1|1|0|1|1|3" -and $fk -eq "1|$persona|3") { $ok++ } else { $detalle += "it$it $codigos $e $fk $(Errores5xx $r.Resultados)" }
    }
    $n3 = [math]::Min($Iteraciones, 20)
    Verificar $g "C3-1" "Persona existente SIN jugador: 2 altas del Deportivo + 1 del portal a la vez -> 1 jugador de ESA persona, categoría 3, una cuota y una inscripción ($n3 iteraciones)" `
        ($ok -eq $n3) "$n3/$n3" "$ok/$n3" ("códigos: " + (($vistos.GetEnumerator() | ForEach-Object { "$($_.Key) x$($_.Value)" }) -join "; ") + " || " + ($detalle -join " || "))
    VerificarIntegridad $g "C3-INT" $antes | Out-Null

    # ============================================================ Caso 4: altas mientras corre la generación mensual (HU-049 + HU-083)
    $g = "Caso 4 alta + generación"
    $antes = Integridad
    $ok = 0; $detalle = @(); $superpuestas = 0; $soloGenerador = @()
    for ($it = 1; $it -le $Iteraciones; $it++) {
        $sinCuota = Precondicion 6 $mesTxt               # el generador les tiene que emitir la cuota del mes
        $adelanto = Precondicion 2 $mesTxt               # se pagan el mes en curso mientras corre el generador
        $nuevos = @(NuevoDni; NuevoDni; NuevoDni; NuevoDni; NuevoDni; NuevoDni)
        $pedidos = @((AltaPortal $nuevos[0]), (AltaPortal $nuevos[1]), (AltaPortal $nuevos[2]), (AltaDeportivo $nuevos[3]), (AltaDeportivo $nuevos[4]), (AltaDeportivo $nuevos[5])) +
            @($adelanto.Values | ForEach-Object { Registrar_Pago $_ $periodoActual $mesActual.Year 1000 })
        $conCompuerta = ($it % 2 -eq 1)
        $r = Rafaga "C4" $it $pedidos -Compuerta:$conCompuerta -AntesDeLanzar { IniciarGeneradores 1 }
        $logs = TerminarGeneradores
        $malos = @()
        foreach ($dni in $sinCuota.Keys) { $c = CuotaMes $sinCuota[$dni] $mesTxt; if ($c -ne "1:1000|0:0|1000") { $malos += "gen $dni=$c" } }
        foreach ($dni in $adelanto.Keys) { $c = CuotaMes $adelanto[$dni] $mesTxt; if ($c -ne "0:0|1:1000|1000") { $malos += "adelanto $dni=$c" } }
        foreach ($dni in $nuevos) { $e = EstadoDni $dni; if ($e -ne "1|1|0|1|1|3") { $malos += "alta $dni=$e" } }
        $genOk = @($logs | Where-Object { $_.Verificadas -and -not $_.Error }).Count -eq 1
        # Superposición: con compuerta, el generador y las altas esperaban juntos; sin ella, la cuota emitida por el generador
        # quedó auditada dentro de la ventana de la ráfaga.
        $soloGenerador += @($sinCuota.Values)
        $ventana = Sql "SELECT CONCAT(CONVERT(varchar(12), CAST(MIN(fecha_utc) AS time(3))), '|', CONVERT(varchar(12), CAST(MAX(fecha_utc) AS time(3)))) FROM AUDITORIA_CAMBIOS WHERE entidad = 'PAGOS' AND accion = 'INSERT' AND id_usuario IS NULL AND origen_sistema = 'PortalAdministrativo' AND JSON_VALUE(datos_despues, '$.idJugador') IN ($(($sinCuota.Values) -join ','))"
        $sup = if ($conCompuerta) { $r.Info.esperandoAlLiberar -ge 2 -and $r.Info.esperandoLista -like "*CACC-Administrativo-Generador*" }
               else { $gv = ($ventana -split "\|")[0]; $gv -ge $r.Info.tiempos.inicioUtc -and $gv -le $r.Info.tiempos.finUtc }
        if ($sup) { $superpuestas++ }
        if ((Sin5xx $r.Resultados) -and @($r.Resultados | Where-Object { $_.Codigo -notin 200, 201 }).Count -eq 0 -and $malos.Count -eq 0 -and $genOk) { $ok++ }
        else { $detalle += "it$it $(Codigos $r.Resultados '') gen=$($logs.Verificadas)/$($logs.Error) $($malos -join ', ') $(Errores5xx $r.Resultados)" }
        $rafagas[-1] | Add-Member -NotePropertyName generador -NotePropertyValue (($logs | ForEach-Object { "$($_.Nombre): $($_.Linea)" }) -join "; ") -Force
        $rafagas[-1] | Add-Member -NotePropertyName cuotasGeneradorUtc -NotePropertyValue $ventana -Force
    }
    Verificar $g "C4-1" "Generación mensual (otro proceso del Administrativo) + 3 altas del portal + 3 del Deportivo + 2 pagos adelantados del mes, a la vez ($Iteraciones iteraciones): exactamente UNA cuota del mes para cada uno" `
        ($ok -eq $Iteraciones) "$Iteraciones/$Iteraciones" "$ok/$Iteraciones" ($detalle -join " || ")
    Verificar $g "C4-2" "Las operaciones de C4 se superpusieron de verdad (compuerta: esperaban juntas; libre: la cuota del generador cae dentro de la ráfaga)" `
        ($superpuestas -ge [math]::Ceiling($Iteraciones / 2)) ">= $([math]::Ceiling($Iteraciones / 2)) de $Iteraciones" "$superpuestas de $Iteraciones"
    VerificarIntegridad $g "C4-INT" $antes | Out-Null

    # ============================================================ Caso 5: dos generaciones simultáneas del mismo período
    $g = "Caso 5 generación + generación"
    $antes = Integridad
    $ok = 0; $detalle = @(); $n5 = [math]::Max(3, [math]::Ceiling($Iteraciones / 2))
    for ($it = 1; $it -le $n5; $it++) {
        $sinActual = Precondicion 8 $mesTxt
        $sinAnterior = Precondicion 4 "2026-08-01"
        $soloGenerador += @($sinActual.Values)
        $esperadasAnt = @(Json (Pedir "GET" "$urlAdministrativo/api/cuotas-faltantes/$($mesAnterior.Year)/$($mesAnterior.Month)" $tokenAlta $null $null).Cuerpo).Count
        $r = Rafaga "C5" $it @((GenerarFaltantes $mesAnterior), (GenerarFaltantes $mesAnterior), (GenerarFaltantes $mesAnterior)) -Compuerta -AntesDeLanzar { IniciarGeneradores 2 }
        $logs = TerminarGeneradores
        $creadas = ($r.Resultados | ForEach-Object { [int](Json $_.Cuerpo).creadas } | Measure-Object -Sum).Sum
        $malos = @()
        foreach ($id in $sinActual.Values) { $c = CuotaMes $id $mesTxt; if ($c -ne "1:1000|0:0|1000") { $malos += "$mesTxt $id=$c" } }
        foreach ($id in $sinAnterior.Values) { foreach ($m in $mesAntTxt, $mesTxt) { $c = CuotaMes $id $m; if ($c -ne "1:1000|0:0|1000") { $malos += "$m $id=$c" } } }
        $genOk = @($logs | Where-Object { $_.Verificadas -and -not $_.Error }).Count -eq 2
        if ((Codigos $r.Resultados "") -eq "200/200/200" -and $creadas -eq $esperadasAnt -and $malos.Count -eq 0 -and $genOk) { $ok++ }
        else { $detalle += "it$it $(Codigos $r.Resultados '') creadas=$creadas esperadas=$esperadasAnt gen=$(($logs | ForEach-Object { "$($_.Verificadas)/$($_.Error)" }) -join ',') $($malos -join ', ')" }
        $rafagas[-1] | Add-Member -NotePropertyName generador -NotePropertyValue (($logs | ForEach-Object { "$($_.Nombre): $($_.Linea)" }) -join "; ") -Force
        $rafagas[-1] | Add-Member -NotePropertyName creadasManual -NotePropertyValue "$creadas de $esperadasAnt" -Force
    }
    Verificar $g "C5-1" "2 generadores mensuales + 3 generaciones manuales del mes anterior a la vez ($n5 iteraciones): UNA cuota por jugador y período, y la suma de 'creadas' = faltantes detectados" `
        ($ok -eq $n5) "$n5/$n5" "$ok/$n5" ($detalle -join " || ")
    VerificarIntegridad $g "C5-INT" $antes | Out-Null

    # ============================================================ Caso 6: pagos durante la generación (+ baja del Deportivo por SQL directo)
    $g = "Caso 6 pago + generación"
    $antes = Integridad
    $ok = 0; $detalle = @(); $n6 = [math]::Max(3, [math]::Ceiling($Iteraciones / 2))
    for ($it = 1; $it -le $n6; $it++) {
        $aRegistrar = Precondicion 4 "2026-08-01" @($mesAntTxt)   # pagan el mes anterior completo (registrar): abono + se borra la pendiente
        $aCobrar = Precondicion 3 "2026-08-01" @($mesAntTxt)      # cobran la cuota pendiente del mes anterior (cobro por id)
        $adelanto = Precondicion 3 $mesTxt                        # pagan el mes en curso mientras el generador lo emite
        $sinAnt = Precondicion 3 "2026-08-01"                     # sin cuota del mes anterior: la emite la generación manual; uno la paga a la vez
        $baja = Precondicion 1 "2026-08-01" @($mesAntTxt)         # el Deportivo le da de baja (UPDATE directo, sin bloqueo de cobros)
        $pendientesCobro = @{}; foreach ($id in $aCobrar.Values) { $pendientesCobro[$id] = [int](Sql "SELECT PK_id_pago FROM PAGOS WHERE FK_id_jugador = $id AND estado = 0 AND fecha_vencimiento = '$mesAntTxt'") }
        $primerSinAnt = @($sinAnt.Values)[0]
        $pedidos = @(GenerarFaltantes $mesAnterior) +
            @($aRegistrar.Values | ForEach-Object { Registrar_Pago $_ $periodoAnterior $mesAnterior.Year 1000 }) +
            @($aCobrar.Values | ForEach-Object { Cobro $_ @($pendientesCobro[$_]) }) +
            @($adelanto.Values | ForEach-Object { Registrar_Pago $_ $periodoActual $mesActual.Year 1000 }) +
            @(Registrar_Pago $primerSinAnt $periodoAnterior $mesAnterior.Year 1000) +
            @(BajaDeportivo @($baja.Values)[0])
        $r = Rafaga "C6" $it $pedidos -Compuerta:($it % 2 -eq 1) -AntesDeLanzar { IniciarGeneradores 1 }
        $logs = TerminarGeneradores
        $malos = @()
        foreach ($id in $aRegistrar.Values) { foreach ($m in @(@($mesAntTxt, "0:0|1:1000|1000"), @($mesTxt, "1:1000|0:0|1000"))) { $c = CuotaMes $id $m[0]; if ($c -ne $m[1]) { $malos += "registrar $id $($m[0])=$c" } } }
        foreach ($id in $aCobrar.Values) { foreach ($m in @(@($mesAntTxt, "0:0|1:1000|1000"), @($mesTxt, "1:1000|0:0|1000"))) { $c = CuotaMes $id $m[0]; if ($c -ne $m[1]) { $malos += "cobro $id $($m[0])=$c" } } }
        foreach ($id in $adelanto.Values) { $c = CuotaMes $id $mesTxt; if ($c -ne "0:0|1:1000|1000") { $malos += "adelanto $id=$c" } }
        foreach ($id in $sinAnt.Values) {
            $esperadoAnt = if ($id -eq $primerSinAnt) { "0:0|1:1000|1000" } else { "1:1000|0:0|1000" }
            foreach ($m in @(@($mesAntTxt, $esperadoAnt), @($mesTxt, "1:1000|0:0|1000"))) { $c = CuotaMes $id $m[0]; if ($c -ne $m[1]) { $malos += "sinAnt $id $($m[0])=$c" } }
        }
        $idBaja = @($baja.Values)[0]
        $estadoBaja = Sql "SELECT CONCAT(CASE WHEN fecha_baja IS NULL THEN 'activo' ELSE CONVERT(char(10), fecha_baja, 23) END, '|', (SELECT COUNT(*) FROM JUGADORES_PERIODOS WHERE FK_id_jugador = $idBaja AND fecha_baja IS NULL)) FROM JUGADORES WHERE PK_id_jugador = $idBaja"
        foreach ($m in @(@($mesAntTxt, "1:1000|0:0|1000"), @($mesTxt, "1:1000|0:0|1000"))) { $c = CuotaMes $idBaja $m[0]; if ($c -ne $m[1]) { $malos += "baja $idBaja $($m[0])=$c" } }
        if ($estadoBaja -ne "$($hoy.ToString('yyyy-MM-dd'))|0") { $malos += "baja $idBaja estado=$estadoBaja" }
        $genOk = @($logs | Where-Object { $_.Verificadas -and -not $_.Error }).Count -eq 1
        $noOk = @($r.Resultados | Where-Object { $_.Codigo -notin 200, 201, 204 })
        if ($noOk.Count -eq 0 -and $malos.Count -eq 0 -and $genOk) { $ok++ }
        else { $detalle += "it$it $(($noOk | ForEach-Object { "$($_.Etiqueta)=$($_.Codigo) $($_.Cuerpo)" }) -join '; ') gen=$($logs.Verificadas)/$($logs.Error) $($malos -join ', ')" }
        $rafagas[-1] | Add-Member -NotePropertyName generador -NotePropertyValue (($logs | ForEach-Object { "$($_.Nombre): $($_.Linea)" }) -join "; ") -Force
    }
    Verificar $g "C6-1" "Generador mensual + generación manual + 8 pagos (registrar, cobro, adelantado) + baja del Deportivo a la vez ($n6 iteraciones): ningún pago perdido, ninguna cuota duplicada, ningún error" `
        ($ok -eq $n6) "$n6/$n6" "$ok/$n6" ($detalle -join " || ")
    $pagosC6 = @($operaciones | Where-Object { $_.caso -eq "C6" -and $_.etiqueta -match "^(registrar|cobro)" })
    $maxPago = ($pagosC6 | Measure-Object ms -Maximum).Maximum
    Verificar $g "C6-2" "Bloqueo de los pagos durante la generación: ninguno llega al límite técnico más bajo de la cadena (5 s del Deportivo -> Administrativo; el bloqueo de cobros y SqlCommand esperan 30 s)" `
        ($maxPago -lt 5000) "< 5000 ms" "máx. $([math]::Round($maxPago)) ms; p95 $((Tiempos ($pagosC6 | ForEach-Object { [pscustomobject]@{ Ms = $_.ms; InicioUtc = [datetime]::MinValue; FinUtc = [datetime]::MaxValue } })).p95) ms ($($pagosC6.Count) pagos)"
    VerificarIntegridad $g "C6-INT" $antes | Out-Null

    # ============================================================ Caso 7: pago + pago sobre la misma cuota (HU-025 / HU-063)
    $g = "Caso 7 pago + pago"
    $antes = Integridad
    $ok = 0; $detalle = @()
    for ($it = 1; $it -le $Iteraciones; $it++) {
        $p = Precondicion 4 "2026-08-01" @($mesAntTxt)
        $j = @($p.Values)
        $pend = @{}; foreach ($id in $j) { $pend[$id] = [int](Sql "SELECT PK_id_pago FROM PAGOS WHERE FK_id_jugador = $id AND estado = 0 AND fecha_vencimiento = '$mesAntTxt'") }
        $pedidos = @(
            (Registrar_Pago $j[0] $periodoAnterior $mesAnterior.Year 1000), (Registrar_Pago $j[0] $periodoAnterior $mesAnterior.Year 1000),   # mismo total dos veces
            (Cobro $j[1] @($pend[$j[1]])), (Cobro $j[1] @($pend[$j[1]])),                                                               # misma cuota por id dos veces
            (Registrar_Pago $j[2] $periodoAnterior $mesAnterior.Year 1000), (Cobro $j[2] @($pend[$j[2]])),                              # registrar + cobro de la misma
            (Registrar_Pago $j[3] $periodoAnterior $mesAnterior.Year 400), (Registrar_Pago $j[3] $periodoAnterior $mesAnterior.Year 400))  # dos abonos parciales
        $r = Rafaga "C7" $it $pedidos -Compuerta:($it % 2 -eq 0)
        $cod = { param($id) (@($r.Resultados | Where-Object { $_.Etiqueta -like "*:J$($id):*" } | ForEach-Object Codigo | Sort-Object) -join "/") }
        $esperado = [ordered]@{ "$($j[0])" = @("200/400", "0:0|1:1000|1000"); "$($j[1])" = @("200/400", "0:0|1:1000|1000"); "$($j[2])" = @("200/400", "0:0|1:1000|1000"); "$($j[3])" = @("200/200", "1:200|2:800|1000") }
        $malos = @(foreach ($id in $esperado.Keys) { $c = CuotaMes ([int]$id) $mesAntTxt; $k = & $cod $id; if ($c -ne $esperado[$id][1] -or $k -ne $esperado[$id][0]) { "$id códigos $k cuota $c" } })
        if ($malos.Count -eq 0) { $ok++ } else { $detalle += "it$it $($malos -join ', ') $(Errores5xx $r.Resultados)" }
    }
    Verificar $g "C7-1" "Misma cuota a la vez ($Iteraciones iteraciones): total x2 / cobro x2 / registrar+cobro -> un 200 y un 400, cobrada UNA vez; dos parciales de 400 -> saldo 200 (sin actualización perdida)" `
        ($ok -eq $Iteraciones) "$Iteraciones/$Iteraciones" "$ok/$Iteraciones" ($detalle -join " || ")
    foreach ($n in $Cargas) {
        $p = Precondicion 1 "2026-08-01" @($mesAntTxt)
        $id = @($p.Values)[0]
        $r = Rafaga "C7-carga" $n @(1..$n | ForEach-Object { Registrar_Pago $id $periodoAnterior $mesAnterior.Year (1000 / $n) })
        $c = CuotaMes $id $mesAntTxt
        Verificar $g "C7-$n" "$n abonos parciales simultáneos de `$$(1000 / $n) sobre una cuota de `$1000: todos 200, la cuota queda saldada exacta (ningún abono perdido ni de más)" `
            (@($r.Resultados | Where-Object Codigo -ne 200).Count -eq 0 -and $c -eq "0:0|$($n):1000|1000") "$n x 200; 0:0|$($n):1000|1000" "$(@($r.Resultados | Where-Object Codigo -eq 200).Count) x 200; $c" (Errores5xx $r.Resultados)
    }
    VerificarIntegridad $g "C7-INT" $antes | Out-Null

    # ============================================================ auditoría: origen y operador de cada evento de la ventana de prueba
    $g = "Auditoría"
    # Solo las altas que CREARON (201): un 200 del Deportivo reutilizó la persona y el jugador que creó otro.
    $dep = (@($operaciones | Where-Object { $_.etiqueta -like "alta-deportivo*" -and $_.codigo -eq 201 } | ForEach-Object { ($_.etiqueta -split ":")[1] } | Sort-Object -Unique) | ForEach-Object { "'$_'" }) -join ","
    $por = (@($operaciones | Where-Object { $_.etiqueta -like "alta-portal*" -and $_.codigo -eq 201 } | ForEach-Object { ($_.etiqueta -split ":")[1] } | Sort-Object -Unique) | ForEach-Object { "'$_'" }) -join ","
    $stf = (@($operaciones | Where-Object { $_.etiqueta -like "staff-deportivo*" -and $_.codigo -eq 201 } | ForEach-Object { ($_.etiqueta -split ":")[1] } | Sort-Object -Unique) | ForEach-Object { "'$_'" }) -join ","
    # Quién escribió cada evento, según su origen y su operador.
    $resumen = Sql "SELECT STRING_AGG(CONCAT(ISNULL(origen_sistema, 'NULL'), '/', CASE id_usuario WHEN $idSuperAdmin THEN 'SuperAdmin' WHEN $idAdministrador THEN 'Administrador' ELSE ISNULL(CONVERT(varchar(10), id_usuario), 'sin operador') END, ':', n), ', ')
        FROM (SELECT origen_sistema, id_usuario, COUNT(*) n FROM AUDITORIA_CAMBIOS WHERE fecha_utc >= '$inicioPruebasUtc' GROUP BY origen_sistema, id_usuario) x"
    # Contaminación: un evento con el origen o el operador de OTRA operación.
    $contaminacion = Sql @"
SELECT CONCAT(
 (SELECT COUNT(*) FROM AUDITORIA_CAMBIOS WHERE fecha_utc >= '$inicioPruebasUtc' AND origen_sistema = 'PortalDeportivo' AND ISNULL(id_usuario, -1) <> $idSuperAdmin), '|',
 (SELECT COUNT(*) FROM AUDITORIA_CAMBIOS WHERE fecha_utc >= '$inicioPruebasUtc' AND id_usuario = $idAdministrador AND ISNULL(origen_sistema, '') <> 'PortalAdministrativo'), '|',
 (SELECT COUNT(*) FROM AUDITORIA_CAMBIOS a JOIN PAGOS g ON g.PK_id_pago = TRY_CONVERT(int, a.id_entidad) WHERE a.fecha_utc >= '$inicioPruebasUtc' AND a.entidad = 'PAGOS' AND g.estado = 1 AND g.FK_id_usuario_registro = $idAdministrador
    AND JSON_VALUE(a.datos_despues, '$.estado') = 'true' AND (ISNULL(a.origen_sistema, '') <> 'PortalAdministrativo' OR ISNULL(a.id_usuario, -1) <> $idAdministrador)), '|',
 (SELECT COUNT(*) FROM AUDITORIA_CAMBIOS a WHERE a.fecha_utc >= '$inicioPruebasUtc' AND a.entidad IN ('JUGADORES', 'PERSONAS') AND a.accion = 'INSERT' AND ISNULL(a.origen_sistema, '') <> 'PortalDeportivo'
    AND a.id_entidad IN (SELECT CONVERT(nvarchar(20), j.PK_id_jugador) FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni IN ($(if ($dep) { $dep } else { "''" })) AND a.entidad = 'JUGADORES'
                         UNION ALL SELECT CONVERT(nvarchar(20), PK_id_persona) FROM PERSONAS WHERE dni IN ($(if ($dep) { $dep } else { "''" })) AND a.entidad = 'PERSONAS'
                           AND NOT EXISTS (SELECT 1 FROM USUARIOS u WHERE u.FK_id_persona = PERSONAS.PK_id_persona))), '|',
 (SELECT COUNT(*) FROM AUDITORIA_CAMBIOS a WHERE a.fecha_utc >= '$inicioPruebasUtc' AND a.entidad IN ('JUGADORES', 'PERSONAS') AND a.accion = 'INSERT' AND (ISNULL(a.origen_sistema, '') <> 'PortalAdministrativo' OR a.id_usuario <> $idSuperAdmin)
    AND a.id_entidad IN (SELECT CONVERT(nvarchar(20), j.PK_id_jugador) FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni IN ($(if ($por) { $por } else { "''" })) AND a.entidad = 'JUGADORES'
                         UNION ALL SELECT CONVERT(nvarchar(20), PK_id_persona) FROM PERSONAS WHERE dni IN ($(if ($por) { $por } else { "''" })) AND a.entidad = 'PERSONAS')), '|',
 (SELECT COUNT(*) FROM AUDITORIA_CAMBIOS a WHERE a.fecha_utc >= '$inicioPruebasUtc' AND a.entidad = 'PAGOS' AND a.accion = 'INSERT' AND JSON_VALUE(a.datos_despues, '$.vencimiento') = '$mesTxt'
    AND JSON_VALUE(a.datos_despues, '$.idJugador') IN ($(if ($soloGenerador) { $soloGenerador -join ',' } else { '-1' })) AND NOT (a.origen_sistema = 'PortalAdministrativo' AND a.id_usuario IS NULL)), '|',
 (SELECT COUNT(*) FROM AUDITORIA_CAMBIOS a WHERE a.fecha_utc >= '$inicioPruebasUtc' AND a.entidad = 'PAGOS' AND a.accion = 'INSERT' AND JSON_VALUE(a.datos_despues, '$.vencimiento') = '$mesTxt'
    AND JSON_VALUE(a.datos_despues, '$.idJugador') IN ($(if ($soloGenerador) { $soloGenerador -join ',' } else { '-1' }))), '|',
 (SELECT COUNT(*) FROM AUDITORIA_CAMBIOS a WHERE a.fecha_utc >= '$inicioPruebasUtc' AND a.entidad IN ('PERSONAS', 'USUARIOS') AND a.accion = 'INSERT' AND a.origen_sistema IS NOT NULL
    AND a.id_entidad IN (SELECT CONVERT(nvarchar(20), u.PK_id_usuario) FROM USUARIOS u JOIN PERSONAS p ON p.PK_id_persona = u.FK_id_persona WHERE p.dni IN ($(if ($stf) { $stf } else { "''" })) AND a.entidad = 'USUARIOS')))
"@
    $cont = $contaminacion -split "\|"
    Verificar $g "AUD-1" "Ningún evento con origen PortalDeportivo lleva otro operador que el del token del Deportivo" ($cont[0] -eq "0") "0" $cont[0] $resumen
    Verificar $g "AUD-2" "Los pagos (Administrador 30000005) quedan con origen PortalAdministrativo y ese operador, aunque corrieron mezclados con pedidos de integración" ($cont[1] -eq "0" -and $cont[2] -eq "0") "0|0" "$($cont[1])|$($cont[2])"
    Verificar $g "AUD-3" "Las altas del Deportivo (integración) quedan con origen PortalDeportivo; las del portal, con PortalAdministrativo y el SuperAdmin" ($cont[3] -eq "0" -and $cont[4] -eq "0") "0|0" "$($cont[3])|$($cont[4])"
    Verificar $g "AUD-4" "Las cuotas del mes que emitió SOLO el generador ($($soloGenerador.Count) jugadores): un evento cada una, origen PortalAdministrativo y sin operador (no heredan los de otra conexión)" `
        ($cont[5] -eq "0" -and [int]$cont[6] -eq $soloGenerador.Count) "0 incorrectos; $($soloGenerador.Count) eventos" "$($cont[5]) incorrectos; $($cont[6]) eventos"
    Verificar $g "AUD-5" "Las escrituras directas del Deportivo (staff) no heredan un origen de otra conexión: origen NULL (el backend del Deportivo no lo sella; ver BUG-059-1)" ($cont[7] -eq "0") "0" $cont[7]
    $fantasma = (Integridad).auditoriaFantasma
    $listaRev = ($revertidas | ForEach-Object { "'$_'" }) -join ","
    $quedaron = if ($listaRev) { Sql "SELECT COUNT(*) FROM PERSONAS WHERE dni IN ($listaRev)" } else { "sin datos" }
    Verificar $g "AUD-6" "Operaciones revertidas bajo concurrencia ($($revertidas.Count) staff que insertan PERSONAS y fallan en USUARIOS) no dejan la persona ni su evento: 0 eventos INSERT de entidades que no existen" `
        ($fantasma -eq $integridadInicial.auditoriaFantasma -and $quedaron -eq "0") "$($integridadInicial.auditoriaFantasma) fantasma; 0 personas" "$fantasma fantasma; $quedaron personas" "códigos del Deportivo: $($ids.rechazoStaffCodigos)"
    $ids.auditoriaResumen = $resumen
}
catch {
    Registrar "Entorno" "ENV-X" "La ejecución se interrumpió" "BLOCKED" "sin error técnico" "error" "$($_.Exception.Message) @ línea $($_.InvocationInfo.ScriptLineNumber)"
    Write-Host "  $($_.Exception.GetType().FullName): $($_.Exception.Message)`n$($_.ScriptStackTrace)" -ForegroundColor Yellow
}
finally {
    # Cada proceso se detiene aunque falle otro: uno que quede vivo ocupa su puerto y la corrida siguiente le hablaría a él.
    $logs = foreach ($api in $procesos.ToArray()) { try { Detener $api } catch { Write-Host "No se pudo detener $($api.Nombre): $($_.Exception.Message)" -ForegroundColor Red } }
    $textoLogs = $logs -join "`n"
}

# ---------------------------------------------------------------- errores SQL, deadlocks, triggers, bloqueos (toda la corrida)
$g = "Errores y bloqueos"
$patrones = [ordered]@{
    "deadlock (1205)" = "(?i)deadlock|interbloqueo|\b1205\b"
    "timeout" = "(?i)timeout expired|tiempo de espera de la ejecuci|Execution Timeout"
    "bloqueo de cobros vencido" = "Hay otra operaci.n de cobro en curso"
    "trigger + OUTPUT (334)" = "(?i)\b334\b|cl.usula OUTPUT sin una cl.usula INTO|OUTPUT clause without INTO|desencadenador habilitado"
    "error de trigger (THROW 51010/51011)" = "\b5101[01]\b"
    # Sin contar UX_USUARIOS_email: es la falla A PROPÓSITO de StaffQueRevierte (se cuenta aparte).
    "violación de unicidad sin manejar" = "(?i)(Cannot insert duplicate key|No se puede insertar una fila de clave duplicada)(?![^\r\n]*UX_USUARIOS_email)"
    "rechazo provocado (UX_USUARIOS_email)" = "(?i)(Cannot insert duplicate key|No se puede insertar una fila de clave duplicada)[^\r\n]*UX_USUARIOS_email"
    "violación de FK" = "(?i)FOREIGN KEY constraint|conflicto con la restricci.n FOREIGN KEY"
}
$hallados = [ordered]@{}
foreach ($k in $patrones.Keys) { $hallados[$k] = ([regex]::Matches($textoLogs, $patrones[$k])).Count }
$deadlocksXe = try { Sql "SELECT COUNT(*) FROM (SELECT CAST(t.target_data AS XML) x FROM sys.dm_xe_session_targets t JOIN sys.dm_xe_sessions s ON s.address = t.event_session_address WHERE s.name = 'system_health' AND t.target_name = 'ring_buffer') d CROSS APPLY d.x.nodes('RingBufferTarget/event[@name=''xml_deadlock_report'']') n(e) WHERE n.e.value('@timestamp', 'datetime2') >= '$inicioPruebasUtc'" } catch { "error: $($_.Exception.Message)" }
$esperasDespues = Sql "SELECT CONCAT(SUM(CASE WHEN wait_type LIKE 'LCK_M_%' THEN waiting_tasks_count ELSE 0 END), '|', SUM(CASE WHEN wait_type LIKE 'LCK_M_%' THEN wait_time_ms ELSE 0 END)) FROM sys.dm_os_wait_stats"
$lckAntes = $esperasAntes -split "\|"; $lckDespues = $esperasDespues -split "\|"
$http5xx = @($operaciones | Where-Object { ($_.codigo -ge 500 -or $_.codigo -eq 0) -and $_.etiqueta -notlike "rechazo-*" })
Verificar $g "DL-1" "0 deadlocks en la corrida (system_health: xml_deadlock_report desde el inicio de las pruebas; logs: 1205/interbloqueo)" ($deadlocksXe -eq "0" -and $hallados["deadlock (1205)"] -eq 0) "0|0" "$deadlocksXe|$($hallados['deadlock (1205)'])"
Verificar $g "ERR-1" "0 timeouts, 0 vencimientos del bloqueo de cobros, 0 respuestas 5xx o sin respuesta" ($hallados["timeout"] -eq 0 -and $hallados["bloqueo de cobros vencido"] -eq 0 -and $http5xx.Count -eq 0) "0|0|0" "$($hallados['timeout'])|$($hallados['bloqueo de cobros vencido'])|$($http5xx.Count)" (($http5xx | Select-Object -First 5 | ForEach-Object { "$($_.caso) $($_.etiqueta) $($_.codigo) $($_.error) $($_.cuerpo)" }) -join " || ")
Verificar $g "TRG-1" "0 errores de trigger: ni 334 (OUTPUT sin INTO con triggers) ni los THROW de los triggers de género" ($hallados["trigger + OUTPUT (334)"] -eq 0 -and $hallados["error de trigger (THROW 51010/51011)"] -eq 0) "0|0" "$($hallados['trigger + OUTPUT (334)'])|$($hallados['error de trigger (THROW 51010/51011)'])"
Verificar $g "ERR-2" "0 violaciones de unicidad o de FK sin manejar en los logs" ($hallados["violación de unicidad sin manejar"] -eq 0 -and $hallados["violación de FK"] -eq 0) "0|0" "$($hallados['violación de unicidad sin manejar'])|$($hallados['violación de FK'])"
# OUTPUT sin INTO en el código que escribe en tablas con trigger (las dos APIs): tiene que ser 0.
$fuentes = @(Get-ChildItem (Join-Path $repo "DaoLibrary") -Recurse -Filter *.cs) + @(Get-ChildItem (Join-Path (Split-Path (Split-Path (Split-Path (Split-Path (Split-Path $DeportivoDll))))) "CACC.DAO") -Recurse -Filter *.cs -ErrorAction SilentlyContinue)
$outputSinInto = @($fuentes | Select-String -Pattern "OUTPUT\s+(INSERTED|DELETED)\." | Where-Object { $_.Line -notmatch "\bINTO\b" -and $_.Path -notlike "*\bin\*" -and $_.Path -notlike "*\obj\*" } | ForEach-Object { "$(Split-Path $_.Path -Leaf):$($_.LineNumber)" })
Verificar $g "TRG-2" "Código de las dos APIs: ningún OUTPUT INSERTED/DELETED sin INTO (patrón que provoca el error 334 con los triggers de auditoría)" ($outputSinInto.Count -eq 0) "0" $outputSinInto.Count ($outputSinInto -join ", ")
$secretos = ([regex]::Matches($textoLogs, [regex]::Escape($credencial))).Count + ([regex]::Matches($textoLogs, [regex]::Escape($claveJwt))).Count + ([regex]::Matches($textoLogs, [regex]::Escape($claveStaff))).Count + ([regex]::Matches($textoLogs, 'eyJ[A-Za-z0-9_-]{20,}')).Count
Verificar "Logs" "LOG-1" "Los logs de las APIs y los generadores no contienen la credencial, la clave JWT, contraseñas ni tokens" ($secretos -eq 0) 0 $secretos

# ---------------------------------------------------------------- resumen
$duracion = [int]((Get-Date) - $inicioCorrida).TotalSeconds
$aislamientoVisto = @($rafagas | ForEach-Object { $_.aislamiento } | Sort-Object -Unique)
$bloqueos = [pscustomobject]@{
    esperasDeBloqueoSqlServer = "$([long]$lckDespues[0] - [long]$lckAntes[0]) esperas LCK_M_*, $([long]$lckDespues[1] - [long]$lckAntes[1]) ms en total (toda la instancia, durante la corrida)"
    maxEsperandoBloqueoCobros = ($rafagas | Measure-Object maxEsperandoBloqueoCobros -Maximum).Maximum
    maxSesionesBloqueadas = ($rafagas | Measure-Object maxBloqueadas -Maximum).Maximum
    maxEsperaObservadaMs = ($rafagas | Measure-Object maxEsperaMs -Maximum).Maximum
    tiposEspera = @($rafagas | ForEach-Object { $_.tiposEspera } | Sort-Object -Unique)
    bloqueosEntreSistemas = @($rafagas | ForEach-Object { $_.bloqueosCruzados } | Sort-Object -Unique)
    aislamientoSesiones = $aislamientoVisto
}
$tiemposPorCaso = $operaciones | Group-Object caso | ForEach-Object {
    $t = Tiempos ($_.Group | ForEach-Object { [pscustomobject]@{ Ms = $_.ms; InicioUtc = [datetime]::MinValue; FinUtc = [datetime]::MaxValue } })
    [pscustomobject]@{ caso = $_.Name; operaciones = $_.Count; p50 = $t.p50; p95 = $t.p95; max = $t.max }
}
Write-Host "`n== Tiempos por caso (ms)" -ForegroundColor Cyan
$tiemposPorCaso | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
Write-Host "== Bloqueos: $($bloqueos | ConvertTo-Json -Compress -Depth 3)"
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
Write-Host "`nTotal: $pass PASS | $fail FAIL | $blocked BLOCKED   ($duracion s, $($operaciones.Count) operaciones en $($rafagas.Count) ráfagas, base $Base)"

$informe = [pscustomobject]@{
    fecha = (Get-Date).ToString("s"); fechaNegocio = $hoy.ToString("yyyy-MM-dd"); duracionSegundos = $duracion; base = $Base; servidor = $Servidor
    iteraciones = $Iteraciones; cargas = $Cargas; administrativoDll = $AdministrativoDll; deportivoDll = $DeportivoDll; config = $ids
    erroresEnLogs = $hallados; deadlocksSystemHealth = $deadlocksXe; bloqueos = $bloqueos; tiemposPorCaso = $tiemposPorCaso
    totales = [pscustomobject]@{ pass = $pass; fail = $fail; blocked = $blocked }; grupos = $grupos; casos = $resultados
    rafagas = $rafagas; operaciones = $operaciones
}
$json = $informe | ConvertTo-Json -Depth 6
[IO.File]::WriteAllText((Join-Path $Salida "resultado.json"), $json, [Text.UTF8Encoding]::new($false))
if ($Evidencia) { [IO.File]::WriteAllText($Evidencia, $json, [Text.UTF8Encoding]::new($false)) }
Write-Host "Evidencia: $Salida"
if ($fail -gt 0 -or $blocked -gt 0) { exit 1 }
exit 0
