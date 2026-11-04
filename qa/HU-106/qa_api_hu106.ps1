<#
.SYNOPSIS
    QA de HU-106 contra la API real y SQL Server: simulación de impacto de un cambio de cuota mensual, confirmación,
    datos que cambian en el medio, permisos, auditoría, cuotas históricas y tiempos.

.DESCRIPTION
    Requiere la API Administrativa levantada (-Url) sobre una base de PRUEBA con las cuentas ficticias de HU-059
    (SuperAdmin 20111222, Administrador 30000005) y el volumen de qa/HU-106/carga_hu106.sql. Escribe en esa base:
    usarla solo con una base descartable (*_Prueba). La clave de las cuentas va en CACC_CLAVE (no se imprime).
    Código de salida: 0 = todo PASS, 1 = algún FAIL.
#>
#Requires -Version 7.0
param(
    [string]$Url = "http://localhost:5118",
    [string]$Servidor = "(localdb)\MSSQLLocalDB",
    [string]$Base = "ClubCamionerosUnificadaV2_HU106_Prueba",
    [string]$Evidencia = ""
)
$ErrorActionPreference = "Stop"
if ($Base -notmatch "_Prueba$") { throw "Solo contra una base de prueba (*_Prueba)." }
$clave = $env:CACC_CLAVE
if (-not $clave) { throw "Definí CACC_CLAVE con la clave de las cuentas ficticias." }

function Sql([string]$Consulta) {
    $t = & sqlcmd -S $Servidor -E -C -b -I -W -h -1 -f 65001 -d $Base -Q "SET NOCOUNT ON; $Consulta" 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "SQL: $t" }
    $t.Trim()
}
$http = [Net.Http.HttpClient]::new(); $http.Timeout = [TimeSpan]::FromSeconds(60)
function Pedir([string]$Metodo, [string]$Ruta, [string]$Token, $Cuerpo) {
    $m = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Metodo), "$Url$Ruta")
    if ($Token) { $m.Headers.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $Token) }
    if ($null -ne $Cuerpo) { $m.Content = [Net.Http.StringContent]::new(($Cuerpo | ConvertTo-Json -Depth 6 -Compress), [Text.Encoding]::UTF8, "application/json") }
    $reloj = [Diagnostics.Stopwatch]::StartNew()
    $r = $http.SendAsync($m).Result
    $texto = $r.Content.ReadAsStringAsync().Result
    $reloj.Stop()
    [pscustomobject]@{ Codigo = [int]$r.StatusCode; Json = $(if ($texto) { try { $texto | ConvertFrom-Json } catch { $null } }); Texto = $texto; Ms = [math]::Round($reloj.Elapsed.TotalMilliseconds, 1) }
}
$resultados = [Collections.Generic.List[object]]::new()
function Registrar([string]$Id, [string]$Caso, [bool]$Ok, $Obtenido) {
    $resultados.Add([pscustomobject]@{ Id = $Id; Caso = $Caso; Estado = $(if ($Ok) { "PASS" } else { "FAIL" }); Obtenido = $Obtenido })
    Write-Host ("{0,-5} {1,-8} {2} | {3}" -f $(if ($Ok) { "PASS" } else { "FAIL" }), $Id, $Caso, $(if ($Obtenido -is [string]) { $Obtenido } else { $Obtenido | ConvertTo-Json -Compress -Depth 5 }))
}
function Conteos { Sql "SELECT CONCAT((SELECT COUNT(*) FROM ARANCELES), '|', (SELECT COUNT(*) FROM PAGOS), '|', (SELECT COUNT(*) FROM AUDITORIA_CAMBIOS), '|', (SELECT CHECKSUM_AGG(CHECKSUM(*)) FROM PAGOS))" }
function Login([string]$Usuario) { (Pedir "POST" "/api/auth/login" $null @{ username = $Usuario; password = $clave }).Json.token }

# ---- preparación
for ($i = 0; $i -lt 60; $i++) { try { if ((Pedir "GET" "/api/aranceles/resumen" $null $null).Codigo -eq 401) { break } } catch { }; Start-Sleep -Milliseconds 500 }
$super = Login "20111222"; $admin = Login "30000005"
if (-not $super -or -not $admin) { throw "No se pudo iniciar sesión con las cuentas ficticias." }
$hoy = [datetime]::ParseExact((Sql "SELECT CONVERT(CHAR(10), CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Argentina Standard Time' AS DATE), 23)"), "yyyy-MM-dd", $null)
$mesFuturo = (New-Object DateTime $hoy.Year, $hoy.Month, 1).AddMonths(2)       # sin aranceles programados en la carga
$mesFuturoTxt = $mesFuturo.ToString("yyyy-MM-dd"); $finFuturoTxt = $mesFuturo.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd")
$hoyTxt = $hoy.ToString("yyyy-MM-dd"); $mesActualTxt = (New-Object DateTime $hoy.Year, $hoy.Month, 1).ToString("yyyy-MM-dd")
$propias = (Sql "SELECT STRING_AGG(FK_id_categoria, ',') FROM ARANCELES WHERE FK_id_categoria IS NOT NULL")
$catMesEnCurso = [int](Sql "SELECT PK_id_categoria FROM CATEGORIAS ORDER BY PK_id_categoria OFFSET 4 ROWS FETCH NEXT 1 ROWS ONLY")
$catVacia = [int](Sql "SELECT PK_id_categoria FROM CATEGORIAS WHERE nombre_categoria = 'HU106 sin jugadores'")
$idSuper = [int](Sql "SELECT u.PK_id_usuario FROM USUARIOS u JOIN PERSONAS p ON p.PK_id_persona = u.FK_id_persona WHERE p.dni = '20111222'")
Write-Host "Hoy $hoyTxt | mes futuro $mesFuturoTxt | categorías con arancel propio: $propias | categoría mes en curso: $catMesEnCurso"

# ---- permisos: solo SuperAdmin (backend, no solo el botón)
$cambioS1 = @{ genero = "Masculino"; tipo = "porcentaje"; valor = 10; vigenteDesde = $mesFuturoTxt }
$a1 = Pedir "POST" "/api/aranceles/simular" $admin $cambioS1
$a2 = Pedir "POST" "/api/aranceles/programar" $admin ($cambioS1 + @{ huella = "x"; jugadoresConfirmados = 1 })
$a3 = Pedir "POST" "/api/aranceles/simular" $null $cambioS1
Registrar "AUT-1" "Administrador: simular 403, programar 403; sin sesión 401" ($a1.Codigo -eq 403 -and $a2.Codigo -eq 403 -and $a3.Codigo -eq 401) "$($a1.Codigo)/$($a2.Codigo)/$($a3.Codigo)"

# ---- S1: Masculino +10 % (varias categorías), contra una consulta independiente
$antes = Conteos
$tiempos = @(); $s1 = $null
for ($i = 1; $i -le 10; $i++) { $s1 = Pedir "POST" "/api/aranceles/simular" $super $cambioS1; $tiempos += $s1.Ms }
$despues = Conteos
Registrar "SIM-0" "10 simulaciones no escriben: ARANCELES, PAGOS (y su contenido) y AUDITORIA_CAMBIOS iguales" ($antes -eq $despues) "$antes -> $despues"

# Lo esperado, escrito distinto que la consulta del backend: varones en el club ese mes, de categorías sin arancel propio.
$esperado = Sql @"
SELECT STRING_AGG(CONCAT(cat, ':', n), ',') WITHIN GROUP (ORDER BY cat) FROM (
  SELECT j.FK_id_categoria AS cat, COUNT(*) AS n FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona
  WHERE LTRIM(RTRIM(p.genero)) = 'Masculino' AND (j.fecha_alta IS NULL OR j.fecha_alta <= '$finFuturoTxt')
    AND (j.fecha_baja IS NULL OR j.fecha_baja >= '$mesFuturoTxt')
    AND j.FK_id_categoria NOT IN (SELECT FK_id_categoria FROM ARANCELES WHERE FK_id_categoria IS NOT NULL AND vigente_desde <= '$finFuturoTxt')
  GROUP BY j.FK_id_categoria) x
"@
$excluidosEsperados = Sql @"
SELECT COUNT(*) FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona
WHERE LTRIM(RTRIM(p.genero)) = 'Masculino' AND (j.fecha_baja IS NULL OR j.fecha_baja >= '$mesFuturoTxt') AND j.FK_id_categoria IN ($propias)
"@
$imp = $s1.Json
$obtenido = (($imp.lineas | Sort-Object idCategoria | ForEach-Object { "$($_.idCategoria):$($_.jugadores)" }) -join ",")
$jugEsperados = (($esperado -split ",") | ForEach-Object { [int](($_ -split ":")[1]) } | Measure-Object -Sum).Sum
Registrar "SIM-1" "Masculino +10 %: jugadores por categoría = consulta independiente (bajas y otro género afuera)" ($obtenido -eq $esperado) "backend $obtenido | esperado $esperado"
Registrar "SIM-2" "Monto: base 85.000 -> 93.500 (HU-069 en el backend); todas las líneas 85.000 -> 93.500" ($imp.montoBase -eq 85000 -and $imp.montoNuevo -eq 93500 -and @($imp.lineas | Where-Object { $_.montoActual -ne 85000 -or $_.montoNuevo -ne 93500 }).Count -eq 0) "$($imp.montoBase) -> $($imp.montoNuevo)"
Registrar "SIM-3" "Total: jugadores y diferencia mensual = suma del desglose (8.500 por jugador)" ($imp.jugadores -eq $jugEsperados -and $imp.diferenciaMensual -eq 8500 * $jugEsperados) "$($imp.jugadores) jugadores, $($imp.diferenciaMensual)"
Registrar "SIM-4" "HU-048: los varones de categorías con arancel propio quedan en excluidos" ((($imp.excluidos | Measure-Object jugadores -Sum).Sum) -eq [int]$excluidosEsperados) "$((($imp.excluidos | ForEach-Object { "$($_.nombreCategoria):$($_.jugadores)" }) -join ', ')) | esperado $excluidosEsperados"
Registrar "SIM-5" "Más de una categoría: confirmación reforzada" ($imp.categorias -gt 1 -and $imp.requiereConfirmacionReforzada) "$($imp.categorias) categorías"
$orden = $tiempos | Sort-Object
Registrar "PERF-1" "Simulación < 2 s con $(Sql "SELECT COUNT(*) FROM JUGADORES") jugadores y $(Sql "SELECT COUNT(*) FROM PAGOS") pagos (10 corridas, ida y vuelta HTTP)" ($orden[-1] -lt 2000) "mín $($orden[0]) ms, mediana $($orden[4]) ms, máx $($orden[-1]) ms"

# ---- S3: confirmación reforzada en el backend (cantidad mal escrita -> 400, nada grabado)
$antes = Conteos
$mal = Pedir "POST" "/api/aranceles/programar" $super ($cambioS1 + @{ huella = $imp.huella; jugadoresConfirmados = $imp.jugadores - 1 })
$sin = Pedir "POST" "/api/aranceles/programar" $super ($cambioS1 + @{ huella = $imp.huella })
Registrar "CONF-1" "Varias categorías con la cantidad mal escrita o sin ella: 400 y nada grabado" ($mal.Codigo -eq 400 -and $sin.Codigo -eq 400 -and (Conteos) -eq $antes) "$($mal.Codigo)/$($sin.Codigo): $($mal.Json.mensaje)"

# ---- S4: datos que cambian entre simulación y confirmación -> 409, nada grabado
$cambioS4 = @{ genero = "Femenino"; tipo = "porcentaje"; valor = 5; vigenteDesde = $mesFuturoTxt }
$s4 = (Pedir "POST" "/api/aranceles/simular" $super $cambioS4).Json
$catSinPropio = [int](Sql "SELECT TOP (1) PK_id_categoria FROM CATEGORIAS WHERE PK_id_categoria NOT IN ($propias) AND nombre_categoria <> 'HU106 sin jugadores' ORDER BY PK_id_categoria DESC")
Sql "INSERT PERSONAS (nombre, apellido, dni, genero, fecha_de_nacimiento) VALUES (N'Otro', N'Proceso', '96999999', 'Femenino', '2012-01-01'); INSERT JUGADORES (FK_id_persona, FK_id_categoria, fecha_alta) SELECT PK_id_persona, $catSinPropio, '$hoyTxt' FROM PERSONAS WHERE dni = '96999999';" | Out-Null
$antes = Conteos
$c4 = Pedir "POST" "/api/aranceles/programar" $super ($cambioS4 + @{ huella = $s4.huella; jugadoresConfirmados = $s4.jugadores })
Registrar "STALE-1" "Entró una jugadora entre simular y confirmar: 409 con el impacto nuevo (+1) y nada grabado" ($c4.Codigo -eq 409 -and $c4.Json.impacto.jugadores -eq $s4.jugadores + 1 -and (Conteos) -eq $antes) "$($c4.Codigo): $($s4.jugadores) -> $($c4.Json.impacto.jugadores); $($c4.Json.mensaje)"
$r4 = Pedir "POST" "/api/aranceles/programar" $super ($cambioS4 + @{ huella = $c4.Json.impacto.huella; jugadoresConfirmados = $c4.Json.impacto.jugadores })
Registrar "STALE-2" "Revisado el impacto nuevo, la confirmación se acepta" ($r4.Codigo -eq 200) "$($r4.Codigo) en $($r4.Ms) ms"

# ---- S1 confirmado: auditoría completa
$historicoAntes = Sql "SELECT CHECKSUM_AGG(CHECKSUM(*)) FROM PAGOS WHERE fecha_vencimiento < '$mesFuturoTxt'"
$c1 = Pedir "POST" "/api/aranceles/programar" $super ($cambioS1 + @{ huella = $imp.huella; jugadoresConfirmados = $imp.jugadores })
$fila = Sql "SELECT CONCAT(monto, '|', tipo_cambio, '|', valor_cambio, '|', monto_anterior, '|', jugadores_alcanzados, '|', diferencia_mensual) FROM ARANCELES WHERE genero = 'Masculino' AND vigente_desde = '$mesFuturoTxt'"
# El origen (PortalAdministrativo / PortalDeportivo) es una columna de V20261028_01 (rama de integración); sin ella, el
# evento identifica a quien escribió por id y email, como siempre.
$conOrigen = (Sql "SELECT ISNULL(CONVERT(VARCHAR(10), COL_LENGTH('AUDITORIA_CAMBIOS', 'origen_sistema')), '')") -ne ""
$columnaOrigen = if ($conOrigen) { "origen_sistema" } else { "email_usuario" }
$origenEsperado = if ($conOrigen) { "PortalAdministrativo" } else { Sql "SELECT email FROM USUARIOS WHERE PK_id_usuario = $idSuper" }
$evento = Sql @"
SELECT TOP (1) CONCAT(accion, '|', id_usuario, '|', $columnaOrigen, '|', JSON_VALUE(datos_despues, '$.tipoCambio'), '|', JSON_VALUE(datos_despues, '$.valorCambio'), '|',
  JSON_VALUE(datos_despues, '$.montoAnterior'), '|', JSON_VALUE(datos_despues, '$.monto'), '|', JSON_VALUE(datos_despues, '$.jugadoresAlcanzados'), '|',
  JSON_VALUE(datos_despues, '$.diferenciaMensual'), '|', (SELECT COUNT(*) FROM OPENJSON(datos_despues, '$.impacto')))
FROM AUDITORIA_CAMBIOS WHERE entidad = N'ARANCELES' AND id_entidad = (SELECT CONVERT(NVARCHAR(128), PK_id_arancel) FROM ARANCELES WHERE genero = 'Masculino' AND vigente_desde = '$mesFuturoTxt')
ORDER BY PK_id_evento DESC
"@
Registrar "CONF-2" "Varias categorías con la cantidad exacta: 200 y el arancel guardado con lo aprobado" ($c1.Codigo -eq 200 -and $fila -eq "93500.00|Porcentaje|10.00|85000.00|$($imp.jugadores)|$(([decimal]$imp.diferenciaMensual).ToString('0.00', [Globalization.CultureInfo]::InvariantCulture))") "$($c1.Codigo) en $($c1.Ms) ms | $fila"
Registrar "AUD-1" "Auditoría: INSERT del SuperAdmin ($columnaOrigen), tipo, valor, anterior, nuevo, jugadores, diferencia y desglose" ($evento -eq "INSERT|$idSuper|$origenEsperado|Porcentaje|10.00|85000.00|93500.00|$($imp.jugadores)|$(([decimal]$imp.diferenciaMensual).ToString('0.00', [Globalization.CultureInfo]::InvariantCulture))|$(@($imp.lineas).Count)") $evento
Registrar "HIST-1" "Confirmar un mes futuro no toca ninguna cuota ya emitida" ((Sql "SELECT CHECKSUM_AGG(CHECKSUM(*)) FROM PAGOS WHERE fecha_vencimiento < '$mesFuturoTxt'") -eq $historicoAntes) "checksum $historicoAntes"
$repetido = Pedir "POST" "/api/aranceles/simular" $super $cambioS1
Registrar "VAL-1" "Un segundo arancel para el mismo destino y mes se rechaza ya en la simulación" ($repetido.Codigo -eq 400 -and $repetido.Json.mensaje -match "ya tiene un arancel") "$($repetido.Codigo): $($repetido.Json.mensaje)"

# ---- S2: una categoría desde hoy (mes en curso): lo simulado es lo que se reemite
$cambioS2 = @{ idCategoria = $catMesEnCurso; tipo = "absoluto"; valor = 99000; vigenteDesde = $hoyTxt }
$s2 = (Pedir "POST" "/api/aranceles/simular" $super $cambioS2).Json
$pend = Sql "SELECT COUNT(*) FROM PAGOS g JOIN JUGADORES j ON j.PK_id_jugador = g.FK_id_jugador WHERE j.FK_id_categoria = $catMesEnCurso AND g.concepto = 'Cuota' AND g.fecha_vencimiento >= '$mesActualTxt' AND g.fecha_vencimiento < DATEADD(MONTH, 1, '$mesActualTxt') AND g.estado = 0 AND NOT EXISTS (SELECT 1 FROM PAGOS q WHERE q.FK_id_jugador = g.FK_id_jugador AND q.concepto = 'Cuota' AND q.estado = 1 AND q.fecha_vencimiento = g.fecha_vencimiento)"
$pagadasAntes = Sql "SELECT CONCAT(COUNT(*), '|', CHECKSUM_AGG(CHECKSUM(PK_id_pago, monto_base, monto_final, estado))) FROM PAGOS g JOIN JUGADORES j ON j.PK_id_jugador = g.FK_id_jugador WHERE j.FK_id_categoria = $catMesEnCurso AND g.concepto = 'Cuota' AND g.estado = 1"
$historicoS2 = Sql "SELECT CHECKSUM_AGG(CHECKSUM(*)) FROM PAGOS g JOIN JUGADORES j ON j.PK_id_jugador = g.FK_id_jugador WHERE j.FK_id_categoria = $catMesEnCurso AND g.fecha_vencimiento < '$mesActualTxt'"
Registrar "SIM-6" "Una categoría, mes en curso: los dos géneros con su monto actual, una sola categoría, cuotas del mes informadas" ($s2.categorias -eq 1 -and -not $s2.requiereConfirmacionReforzada -and $s2.mesEnCurso -and $s2.cuotasDelMesPendientesSinPagos -eq [int]$pend -and @($s2.lineas).Count -eq 2) "$((($s2.lineas | ForEach-Object { "$($_.genero) $($_.montoActual)->$($_.montoNuevo) x$($_.jugadores)" }) -join '; ')); pendientes sin pagos $($s2.cuotasDelMesPendientesSinPagos) (esperado $pend), con pagos $($s2.cuotasDelMesConPagos)"
$c2 = Pedir "POST" "/api/aranceles/programar" $super ($cambioS2 + @{ huella = $s2.huella })
$reemitidas = Sql "SELECT CONCAT(COUNT(*), '|', MIN(monto_base), '|', MAX(monto_base)) FROM PAGOS g JOIN JUGADORES j ON j.PK_id_jugador = g.FK_id_jugador WHERE j.FK_id_categoria = $catMesEnCurso AND g.concepto = 'Cuota' AND g.fecha_vencimiento >= '$mesActualTxt' AND g.fecha_vencimiento < DATEADD(MONTH, 1, '$mesActualTxt') AND g.estado = 0"
$conPagosDespues = Sql "SELECT CONCAT(COUNT(*), '|', CHECKSUM_AGG(CHECKSUM(PK_id_pago, monto_base, monto_final, estado))) FROM PAGOS g JOIN JUGADORES j ON j.PK_id_jugador = g.FK_id_jugador WHERE j.FK_id_categoria = $catMesEnCurso AND g.concepto = 'Cuota' AND g.estado = 1"
Registrar "REAL-1" "Confirmado: se reemiten exactamente las pendientes que mostró la simulación, todas con 99.000" ($c2.Codigo -eq 200 -and $reemitidas -eq "$pend|99000.00|99000.00" -and $c2.Json.mensaje -match "Se volvieron a emitir $pend cuota") "$($c2.Codigo): $reemitidas | $($c2.Json.mensaje)"
Registrar "HIST-2" "Las cuotas con pagos y las de meses anteriores conservan su monto" ($conPagosDespues -eq $pagadasAntes -and (Sql "SELECT CHECKSUM_AGG(CHECKSUM(*)) FROM PAGOS g JOIN JUGADORES j ON j.PK_id_jugador = g.FK_id_jugador WHERE j.FK_id_categoria = $catMesEnCurso AND g.fecha_vencimiento < '$mesActualTxt'") -eq $historicoS2) "pagadas $pagadasAntes -> $conPagosDespues"

# ---- S5: cero jugadores
$s5 = Pedir "POST" "/api/aranceles/simular" $super @{ idCategoria = $catVacia; tipo = "absoluto"; valor = 50000; vigenteDesde = $mesFuturoTxt }
Registrar "SIM-7" "Categoría sin jugadores: 0 afectados, $0, sin confirmación reforzada" ($s5.Codigo -eq 200 -and $s5.Json.jugadores -eq 0 -and $s5.Json.diferenciaMensual -eq 0 -and -not $s5.Json.requiereConfirmacionReforzada -and @($s5.Json.lineas).Count -eq 0) "$($s5.Json.jugadores) jugadores, $($s5.Json.diferenciaMensual)"

# ---- S6: validaciones de HU-069 en el backend
$v = @(
    @("porcentaje 1000", @{ genero = "Femenino"; tipo = "porcentaje"; valor = 1000; vigenteDesde = $hoy.AddMonths(3).ToString("yyyy-MM-01") }),
    @("porcentaje 12,345", @{ genero = "Femenino"; tipo = "porcentaje"; valor = 12.345; vigenteDesde = $hoy.AddMonths(3).ToString("yyyy-MM-01") }),
    @("porcentaje 0", @{ genero = "Femenino"; tipo = "porcentaje"; valor = 0; vigenteDesde = $hoy.AddMonths(3).ToString("yyyy-MM-01") }),
    @("monto fijo sin arancel vigente", @{ idCategoria = $catVacia; tipo = "montoFijo"; valor = 1000; vigenteDesde = $hoy.AddMonths(3).ToString("yyyy-MM-01") }),
    @("fecha pasada", @{ genero = "Femenino"; tipo = "absoluto"; valor = 1000; vigenteDesde = $hoy.AddDays(-1).ToString("yyyy-MM-dd") }),
    @("tipo inexistente", @{ genero = "Femenino"; tipo = "otro"; valor = 1000; vigenteDesde = $hoy.AddMonths(3).ToString("yyyy-MM-01") }))
foreach ($caso in $v) {
    $r = Pedir "POST" "/api/aranceles/simular" $super $caso[1]
    Registrar "VAL-2" "Rechazo controlado: $($caso[0])" ($r.Codigo -eq 400 -and $r.Json.mensaje) "$($r.Codigo): $($r.Json.mensaje)"
}

# ---- tiempos que registró el backend
$logs = Get-Content (Join-Path $env:TEMP "cacc-hu106\api.log") -Raw -ErrorAction SilentlyContinue
# La consola de la API escribe en la página de códigos de Windows: la "ó" puede llegar cambiada.
$ms = @([regex]::Matches($logs, "Simulaci.{1,2}n de arancel[^\r\n]* en (\d+) ms") | ForEach-Object { [int]$_.Groups[1].Value })
Registrar "PERF-2" "Duración medida en el backend (log de la API): todas < 2.000 ms" ($ms.Count -gt 0 -and ($ms | Measure-Object -Maximum).Maximum -lt 2000) "$($ms.Count) simulaciones: mín $(($ms | Measure-Object -Minimum).Minimum) ms, máx $(($ms | Measure-Object -Maximum).Maximum) ms"

$fallas = @($resultados | Where-Object Estado -eq "FAIL").Count
Write-Host "`nTotal: $($resultados.Count - $fallas) PASS | $fallas FAIL"
if ($Evidencia) {
    [IO.File]::WriteAllText($Evidencia, ([pscustomobject]@{ fecha = (Get-Date).ToString("s"); base = $Base; hoy = $hoyTxt; tiemposHttpMs = $tiempos; tiemposBackendMs = $ms; simulacionS1 = $imp; resultados = $resultados } | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
}
exit [int]($fallas -gt 0)
