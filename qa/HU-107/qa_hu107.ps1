<#
.SYNOPSIS
    QA de HU-107 contra la API real y SQL Server: fecha de cada pago, paginación y orden en el servidor, totales del
    período completo, exportaciones PDF/CSV, permisos y tiempos. Opcionalmente, la pantalla real en el navegador.

.DESCRIPTION
    1. Genera una clave aleatoria para las cuentas del QA (no se imprime ni se guarda) y recrea la base descartable con
       preparar_base_hu107.ps1.
    2. Levanta la API compilada (bin\Debug) en :5118 contra esa base.
    3. Compara cada respuesta con una consulta SQL independiente (no con el DAO que se está probando).
    4. Con -Navegador, levanta ng serve en :4200 y corre qa_navegador_hu107.mjs (Chrome headless por CDP).
    Escribe la evidencia en -Evidencia. Código de salida: 0 = todo PASS, 1 = algún FAIL.
#>
#Requires -Version 7.0
param(
    [string]$Servidor = "(localdb)\MSSQLLocalDB",
    [string]$Base = "ClubCamionerosUnificadaV2_HU107_Prueba",
    [string]$Url = "http://localhost:5118",
    [string]$Evidencia = (Join-Path $PSScriptRoot "evidencia"),
    [switch]$Navegador,
    [switch]$SinPreparar
)
$ErrorActionPreference = "Stop"
if ($Base -notmatch "_Prueba$") { throw "Solo contra una base de prueba (*_Prueba)." }
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$bin = Join-Path $repo "ApiGestion\bin\Debug\net9.0"
New-Item -ItemType Directory -Force $Evidencia | Out-Null
$inv = [Globalization.CultureInfo]::InvariantCulture

# ---- clave de la corrida (solo en memoria y en el entorno de los procesos hijos)
$clave = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(18)).Replace('+', 'x').Replace('/', 'y') + "Aa1!"
Add-Type -Path (Join-Path $bin "BCrypt.Net-Next.dll")
$hash = [BCrypt.Net.BCrypt]::HashPassword($clave, 11)
if (-not $SinPreparar) { & (Join-Path $PSScriptRoot "preparar_base_hu107.ps1") -HashClave $hash -Servidor $Servidor -Base $Base }
else {
    # Corrida sobre la base ya cargada: clave nueva para las cuentas del QA y sin intentos fallidos de corridas anteriores.
    $r = & sqlcmd -S $Servidor -E -C -b -I -W -h -1 -d $Base -v "HASH=$hash" -Q "SET NOCOUNT ON; UPDATE USUARIOS SET contrasenia = '`$(HASH)' WHERE FK_id_persona IN (SELECT PK_id_persona FROM PERSONAS WHERE dni IN ('95900001','95900002')); SELECT @@ROWCOUNT; DELETE FROM dbo.INTENTOS_LOGIN;" 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0 -or $r.Trim() -notmatch '^2') { throw "No se pudo renovar la clave de las cuentas del QA: $r" }
}

function Sql([string]$Consulta) {
    $t = & sqlcmd -S $Servidor -E -C -b -I -W -h -1 -f 65001 -d $Base -Q "SET NOCOUNT ON; $Consulta" 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "SQL: $t" }
    $t.Trim()
}

# ---- API
$procesos = [Collections.Generic.List[Diagnostics.Process]]::new()
function Iniciar([string]$Archivo, [string]$Argumentos, [string]$Carpeta, [hashtable]$Entorno, [string]$Log) {
    $info = [Diagnostics.ProcessStartInfo]::new($Archivo, $Argumentos)
    $info.WorkingDirectory = $Carpeta; $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    foreach ($k in $Entorno.Keys) { $info.EnvironmentVariables[$k] = $Entorno[$k] }
    $p = [Diagnostics.Process]::new(); $p.StartInfo = $info
    $escritor = [IO.StreamWriter]::new($Log, $false, [Text.UTF8Encoding]::new($false)); $escritor.AutoFlush = $true
    $accion = { if ($null -ne $EventArgs.Data) { $Event.MessageData.WriteLine($EventArgs.Data) } }
    Register-ObjectEvent $p OutputDataReceived -Action $accion -MessageData $escritor | Out-Null
    Register-ObjectEvent $p ErrorDataReceived -Action $accion -MessageData $escritor | Out-Null
    $p.Start() | Out-Null; $p.BeginOutputReadLine(); $p.BeginErrorReadLine()
    $procesos.Add($p); $p
}
function Detener {
    foreach ($p in $procesos.ToArray()) {
        if (-not $p.HasExited) { & taskkill /PID $p.Id /T /F 2>&1 | Out-Null }
    }
}

$http = [Net.Http.HttpClient]::new(); $http.Timeout = [TimeSpan]::FromSeconds(120)
function Pedir([string]$Metodo, [string]$Ruta, [string]$Token, $Cuerpo) {
    $m = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Metodo), "$Url$Ruta")
    if ($Token) { $m.Headers.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $Token) }
    if ($null -ne $Cuerpo) { $m.Content = [Net.Http.StringContent]::new(($Cuerpo | ConvertTo-Json -Compress), [Text.Encoding]::UTF8, "application/json") }
    $reloj = [Diagnostics.Stopwatch]::StartNew()
    $r = $http.SendAsync($m).Result
    $bytes = $r.Content.ReadAsByteArrayAsync().Result
    $reloj.Stop()
    $texto = [Text.Encoding]::UTF8.GetString($bytes)
    [pscustomobject]@{
        Codigo = [int]$r.StatusCode; Bytes = $bytes; Texto = $texto; Ms = [math]::Round($reloj.Elapsed.TotalMilliseconds, 1)
        Json = $(if ($r.Content.Headers.ContentType.MediaType -eq "application/json") { $texto | ConvertFrom-Json -DateKind String })
    }
}
$resultados = [Collections.Generic.List[object]]::new()
function Registrar([string]$Id, [string]$Caso, [bool]$Ok, $Obtenido) {
    $resultados.Add([pscustomobject]@{ Id = $Id; Caso = $Caso; Estado = $(if ($Ok) { "PASS" } else { "FAIL" }); Obtenido = $Obtenido })
    Write-Host ("{0,-5} {1,-8} {2} | {3}" -f $(if ($Ok) { "PASS" } else { "FAIL" }), $Id, $Caso, $(if ($Obtenido -is [string]) { $Obtenido } else { $Obtenido | ConvertTo-Json -Compress -Depth 5 }))
}

try {
    $entornoApi = @{
        ASPNETCORE_ENVIRONMENT = "Development"
        ConnectionStrings__ConexionSQL = "Server=$Servidor;Database=$Base;Trusted_Connection=True;TrustServerCertificate=True;"
        Jwt__Key = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
        Integracion__ClienteDeportivo__Clave = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
        DatabaseMigrations__Enabled = "false"
    }
    $logApi = Join-Path $Evidencia "api.log"
    Iniciar "dotnet" "`"$(Join-Path $bin 'ApiGestion.dll')`" --urls $Url" $bin $entornoApi $logApi | Out-Null
    for ($i = 0; $i -lt 120; $i++) { try { if ((Pedir "GET" "/api/recaudacion?anio=2026&mes=9" $null $null).Codigo -eq 401) { break } } catch { }; Start-Sleep -Milliseconds 500 }
    function Login([string]$Usuario) { (Pedir "POST" "/api/auth/login" $null @{ username = $Usuario; password = $clave }).Json.token }
    $super = Login "95900001"; $admin = Login "95900002"
    if (-not $super -or -not $admin) { throw "No se pudo iniciar sesión con las cuentas del QA." }

    function Pagina([int]$Mes, [int]$Page = 1, [int]$Size = 10, [string]$Sort = "fecha", [string]$Dir = "desc", [string]$Extra = "") {
        Pedir "GET" "/api/recaudacion/pagos?anio=2026&mes=$Mes&page=$Page&pageSize=$Size&sortBy=$Sort&sortDirection=$Dir$Extra" $super $null
    }
    function Todas([int]$Mes, [string]$Sort, [string]$Dir, [string]$Extra = "") {
        $primera = Pagina $Mes 1 10 $Sort $Dir $Extra
        $filas = [Collections.Generic.List[object]]::new()
        for ($n = 1; $n -le $primera.Json.totalPaginas; $n++) { foreach ($f in (Pagina $Mes $n 10 $Sort $Dir $Extra).Json.items) { $filas.Add($f) } }
        [pscustomobject]@{ Primera = $primera.Json; Filas = $filas.ToArray() }
    }
    # Lo esperado sale de SQL directo, con la misma regla de negocio (estado = 1, fecha_pago en el mes).
    function Esperado([int]$Mes, [string]$Filtro = "") {
        $desde = "2026-{0:00}-01" -f $Mes; $hasta = ([datetime]::ParseExact($desde, "yyyy-MM-dd", $inv)).AddMonths(1).ToString("yyyy-MM-dd")
        $f = (Sql "SELECT CONCAT(COUNT(*), '|', CONVERT(VARCHAR(30), ISNULL(SUM(p.monto_final), 0)), '|', CONVERT(VARCHAR(30), ISNULL(SUM(CASE WHEN p.metodo_pago = 'Efectivo' THEN p.monto_final END), 0)), '|', CONVERT(VARCHAR(30), ISNULL(SUM(CASE WHEN p.metodo_pago = 'Transferencia' THEN p.monto_final END), 0)), '|', SUM(CASE WHEN p.metodo_pago = 'Efectivo' THEN 1 ELSE 0 END), '|', SUM(CASE WHEN p.metodo_pago = 'Transferencia' THEN 1 ELSE 0 END)) FROM PAGOS p JOIN JUGADORES j ON j.PK_id_jugador = p.FK_id_jugador WHERE p.estado = 1 AND p.fecha_pago >= '$desde' AND p.fecha_pago < '$hasta' $Filtro") -split '\|'
        $ids = (Sql "SELECT p.PK_id_pago FROM PAGOS p JOIN JUGADORES j ON j.PK_id_jugador = p.FK_id_jugador WHERE p.estado = 1 AND p.fecha_pago >= '$desde' AND p.fecha_pago < '$hasta' $Filtro") -split "`r?`n"
        [pscustomobject]@{
            Pagos = [int]$f[0]; Total = [decimal]::Parse($f[1], $inv); Efectivo = [decimal]::Parse($f[2], $inv); Transferencia = [decimal]::Parse($f[3], $inv)
            PagosEfectivo = [int]$f[4]; PagosTransferencia = [int]$f[5]
            Ids = @($ids | Where-Object { $_ -match '^\d+$' } | ForEach-Object { [long]$_ })
        }
    }
    # Fecha real de registro; solo los históricos sin timestamp usan fecha_pago a medianoche.
    function Hora([string]$Texto, [string]$Formato) { [DateTimeOffset]::Parse($Texto, $inv).ToString($Formato, $inv) }
    function ClaveFecha($f) { "{0}|{1:D12}" -f $(if ($f.fechaHoraRegistro) { Hora $f.fechaHoraRegistro "yyyy-MM-ddTHH:mm:ss.fffffff" } else { "$($f.fechaPago)T00:00:00.0000000" }), [long]$f.idPago }
    function ClaveMonto($f) { "{0:000000000000.00}|{1:D12}" -f [decimal]$f.monto, [long]$f.idPago }
    function Ordenada([object[]]$Filas, [scriptblock]$Clave, [bool]$Desc) {
        $claves = @($Filas | ForEach-Object { & $Clave $_ })
        $orden = [string[]]$claves.Clone(); [Array]::Sort($orden, [StringComparer]::Ordinal)
        if ($Desc) { [Array]::Reverse($orden) }
        for ($i = 0; $i -lt $claves.Count; $i++) {
            if ($claves[$i] -ne $orden[$i]) { Write-Host "  fuera de orden en la posición ${i}: API '$($claves[$i])' / esperado '$($orden[$i])'"; return $false }
        }
        $true
    }

    # ===== Septiembre: más de 100 pagos =====
    $sep = Esperado 9
    Write-Host "Septiembre esperado (SQL): $($sep.Pagos) cobros, total $($sep.Total)"
    $p1 = Pagina 9
    Registrar "PAG-1" "Primera página: 10 filas, total = cobros del período (SQL), 13 páginas" ($p1.Codigo -eq 200 -and $p1.Json.items.Count -eq 10 -and $p1.Json.total -eq $sep.Pagos -and $p1.Json.totalPaginas -eq [math]::Ceiling($sep.Pagos / 10) -and $p1.Json.pagina -eq 1 -and $p1.Json.tamanioPagina -eq 10) "items=$($p1.Json.items.Count) total=$($p1.Json.total) paginas=$($p1.Json.totalPaginas) (SQL $($sep.Pagos))"
    $p7 = Pagina 9 7
    Registrar "PAG-2" "Página intermedia (7): 10 filas, mismo total" ($p7.Json.items.Count -eq 10 -and $p7.Json.total -eq $sep.Pagos -and $p7.Json.pagina -eq 7) "items=$($p7.Json.items.Count) total=$($p7.Json.total)"
    $ultima = Pagina 9 $p1.Json.totalPaginas
    Registrar "PAG-3" "Última página: el resto exacto" ($ultima.Json.items.Count -eq ($sep.Pagos - 10 * ($p1.Json.totalPaginas - 1))) "página $($p1.Json.totalPaginas): $($ultima.Json.items.Count) filas"
    $fuera = Pagina 9 ($p1.Json.totalPaginas + 1)
    Registrar "PAG-4" "Después de la última: sin filas, el total sigue siendo el del período" ($fuera.Codigo -eq 200 -and $fuera.Json.items.Count -eq 0 -and $fuera.Json.total -eq $sep.Pagos) "items=$($fuera.Json.items.Count) total=$($fuera.Json.total)"

    $recorridos = @{}
    foreach ($combo in @("fecha desc", "fecha asc", "monto desc", "monto asc")) {
        $sort, $dir = $combo -split ' '
        $r = Todas 9 $sort $dir
        $recorridos[$combo] = $r
        $ids = @($r.Filas | ForEach-Object { [long]$_.idPago })
        $iguales = ((@($ids | Sort-Object) -join ',') -eq (@($sep.Ids | Sort-Object) -join ','))
        Registrar "PAG-5" "Recorrer todas las páginas ($combo): $($sep.Pagos) filas, sin repetidas ni perdidas (= ids de SQL)" ($ids.Count -eq $sep.Pagos -and @($ids | Select-Object -Unique).Count -eq $ids.Count -and $iguales) "filas=$($ids.Count) únicas=$(@($ids | Select-Object -Unique).Count) igualSQL=$iguales"
        $funcionOrden = if ($sort -eq "fecha") { ${function:ClaveFecha} } else { ${function:ClaveMonto} }
        $columnaSql = if ($sort -eq "fecha") { "COALESCE(p.fecha_hora_registro, CAST(p.fecha_pago AS datetime2))" } else { "p.monto_final" }
        $idsSqlOrdenados = (Sql "SELECT p.PK_id_pago FROM PAGOS p JOIN JUGADORES j ON j.PK_id_jugador = p.FK_id_jugador WHERE p.estado = 1 AND p.fecha_pago >= '2026-09-01' AND p.fecha_pago < '2026-10-01' ORDER BY $columnaSql $dir, p.PK_id_pago $dir") -split "`r?`n"
        $ok = (Ordenada $r.Filas $funcionOrden ($dir -eq "desc")) -and (($ids -join ',') -eq (($idsSqlOrdenados | ForEach-Object { [long]$_ }) -join ','))
        Registrar "ORD-$($combo.Replace(' ', '-'))" "Orden $combo sobre TODO el período (cruza páginas), desempate por id" $ok "primeras: $((@($r.Filas | Select-Object -First 4 | ForEach-Object { if ($sort -eq 'fecha') { "$($_.idPago)@$($_.fechaHoraRegistro ?? $_.fechaPago)" } else { "$($_.idPago)=$($_.monto)" } })) -join ', ')"
    }
    $montosAsc = @($recorridos["monto asc"].Filas | ForEach-Object { [decimal]$_.monto } | Select-Object -Unique)
    Registrar "ORD-NUM" "Monto ordena como número (9000 < 20000 < 60000 < 85000 < 95000 < 100000), no como texto" (($montosAsc -join ',') -eq "9000,20000,60000,85000,95000,100000") ($montosAsc -join ',')

    # Mismo día, distintas horas, y dos con la misma hora exacta.
    $dia1 = @($recorridos["fecha asc"].Filas | Where-Object { $_.fechaPago -eq "2026-09-01" })
    $horasDia1 = @($dia1 | ForEach-Object { if ($_.fechaHoraRegistro) { Hora $_.fechaHoraRegistro "HH:mm:ss" } else { "sin hora" } })
    $idsMismaHora = @((Sql "SELECT PK_id_pago FROM PAGOS WHERE fecha_hora_registro = '2026-09-01T09:07:00' ORDER BY PK_id_pago") -split "`r?`n" | ForEach-Object { [long]$_ })
    $posMismaHora = @($dia1 | ForEach-Object { [long]$_.idPago } | Where-Object { $idsMismaHora -contains $_ })
    Registrar "ORD-DIA" "Mismo día (01/09): ordena por la hora real; misma hora exacta -> por id" ($dia1.Count -ge 4 -and (Ordenada $dia1 ${function:ClaveFecha} $false) -and ($posMismaHora -join ',') -eq ($idsMismaHora -join ',')) "horas=$($horasDia1 -join ', ') mismaHora=$($idsMismaHora -join ',')"

    # Fecha de cada pago: la de la base, en hora del club con su corrimiento.
    $muestra = $recorridos["fecha desc"].Filas | Where-Object { $_.fechaHoraRegistro } | Select-Object -First 1
    $enBase = Sql "SELECT CONCAT(CONVERT(CHAR(10), fecha_pago, 23), '|', CONVERT(VARCHAR(27), fecha_hora_registro, 126)) FROM PAGOS WHERE PK_id_pago = $($muestra.idPago)"
    $crudo = [regex]::Match($p1.Texto, '"fechaHoraRegistro":"([^"]+)"').Groups[1].Value
    $sinHora = @($recorridos["fecha desc"].Filas | Where-Object { -not $_.fechaHoraRegistro })
    $sinHoraBase = [int](Sql "SELECT COUNT(*) FROM PAGOS WHERE estado = 1 AND fecha_pago >= '2026-09-01' AND fecha_pago < '2026-10-01' AND fecha_hora_registro IS NULL")
    $registro = [DateTimeOffset]::Parse($muestra.fechaHoraRegistro, $inv)
    Registrar "FEC-1" "Cada fila trae fechaPago y fechaHoraRegistro = PAGOS.fecha_hora_registro, con corrimiento -03:00 de la zona" ($crudo -match '-03:00$' -and "$($muestra.fechaPago)|$($registro.DateTime.ToString('yyyy-MM-ddTHH:mm:ss.fffffff', $inv))".StartsWith(($enBase -replace '\.\d+$', ''))) "API $($muestra.fechaPago) $crudo | base $enBase"
    Registrar "FEC-2" "Pagos sin hora de registro: fechaHoraRegistro null, solo el día" ($sinHora.Count -eq $sinHoraBase -and @($sinHora | Where-Object { $_.fechaPago -notmatch '^\d{4}-\d\d-\d\d$' }).Count -eq 0) "API $($sinHora.Count) / base $sinHoraBase"
    $borde = @($recorridos["fecha desc"].Filas)[0]
    Registrar "FEC-3" "El 30/09 23:59:30 está en septiembre y es el primero por fecha desc" ($borde.fechaHoraRegistro -like "2026-09-30T23:59:30*") "$($borde.idPago) $($borde.fechaHoraRegistro)"

    # Totales del período completo, independientes de la página.
    $t1 = Pedir "GET" "/api/recaudacion?anio=2026&mes=9" $super $null
    Pagina 9 5 | Out-Null; Pagina 9 13 10 "monto" "asc" | Out-Null
    $t2 = Pedir "GET" "/api/recaudacion?anio=2026&mes=9" $super $null
    $tj = $t1.Json
    Registrar "TOT-1" "Tarjetas = SQL del período completo (total, efectivo, transferencia, cantidades)" ($tj.pagos -eq $sep.Pagos -and [decimal]$tj.total -eq $sep.Total -and [decimal]$tj.efectivo -eq $sep.Efectivo -and [decimal]$tj.transferencia -eq $sep.Transferencia -and $tj.pagosEfectivo -eq $sep.PagosEfectivo -and $tj.pagosTransferencia -eq $sep.PagosTransferencia) "API $($t1.Texto) | SQL $($sep.Pagos)/$($sep.Total)/$($sep.Efectivo)/$($sep.Transferencia)"
    Registrar "TOT-2" "Pedir otras páginas u otro orden no cambia los totales (respuesta idéntica)" ($t1.Texto -eq $t2.Texto) "iguales=$($t1.Texto -eq $t2.Texto)"
    Registrar "TOT-3" "Los totales ya no traen el detalle de filas" ($t1.Texto -notmatch '"detalle"') "$($t1.Bytes.Length) bytes"
    $sumaFilas = ($recorridos["fecha desc"].Filas | Measure-Object -Property monto -Sum).Sum
    $sumaPagina = ($p1.Json.items | Measure-Object -Property monto -Sum).Sum
    Registrar "TOT-4" "Suma de todas las páginas = total de la tarjeta; la página sola no lo es" ([decimal]$sumaFilas -eq [decimal]$tj.total -and [decimal]$sumaPagina -ne [decimal]$tj.total) "todas=$sumaFilas página1=$sumaPagina tarjeta=$($tj.total)"

    # Filtro de categoría: tabla y tarjetas cuentan el mismo conjunto.
    $catA = [int](Sql "SELECT PK_id_categoria FROM CATEGORIAS ORDER BY PK_id_categoria OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY")
    $sepA = Esperado 9 "AND j.FK_id_categoria = $catA"
    $pa = Todas 9 "fecha" "desc" "&idCategoria=$catA"
    $ta = (Pedir "GET" "/api/recaudacion?anio=2026&mes=9&idCategoria=$catA" $super $null).Json
    Registrar "CAT-1" "Con categoría: total de la tabla = tarjetas = SQL, y todas las filas son de esa categoría" ($pa.Primera.total -eq $sepA.Pagos -and $ta.pagos -eq $sepA.Pagos -and [decimal]$ta.total -eq $sepA.Total -and $pa.Filas.Count -eq $sepA.Pagos) "tabla=$($pa.Primera.total) tarjetas=$($ta.pagos) SQL=$($sepA.Pagos)"

    # ===== Octubre: pocos pagos; agosto: vacío =====
    $oct = Esperado 10
    $po = Pagina 10
    $to = (Pedir "GET" "/api/recaudacion?anio=2026&mes=10" $super $null).Json
    Registrar "CHI-1" "Mes con pocos pagos (octubre): una página, total correcto, tarjetas = SQL" ($po.Json.total -eq $oct.Pagos -and $po.Json.totalPaginas -eq 1 -and $po.Json.items.Count -eq $oct.Pagos -and $to.pagos -eq $oct.Pagos -and [decimal]$to.total -eq $oct.Total) "total=$($po.Json.total) páginas=$($po.Json.totalPaginas) filas=$($po.Json.items.Count) SQL=$($oct.Pagos)/$($oct.Total)"
    $ultimaOct = @($po.Json.items)[-1]
    Registrar "CHI-2" "01/10 00:01 queda en octubre (último por fecha desc)" ($ultimaOct.fechaHoraRegistro -like "2026-10-01T00:01:00*") "$($ultimaOct.fechaHoraRegistro)"
    $pg = Pagina 8
    $tg = (Pedir "GET" "/api/recaudacion?anio=2026&mes=8" $super $null).Json
    Registrar "VAC-1" "Mes vacío (agosto): 0 pagos, 1 página, sin filas, tarjetas en 0 (el 31/07 23:59 queda en julio)" ($pg.Codigo -eq 200 -and $pg.Json.total -eq 0 -and $pg.Json.totalPaginas -eq 1 -and $pg.Json.items.Count -eq 0 -and $tg.pagos -eq 0 -and [decimal]$tg.total -eq 0) "total=$($pg.Json.total) páginas=$($pg.Json.totalPaginas) tarjetas=$($tg.pagos)/$($tg.total)"

    # ===== Validación de parámetros =====
    $invalidos = @(
        "page=0", "page=-3", "pageSize=0", "pageSize=101", "sortBy=PK_id_pago", "sortBy=fecha_pago%3B%20DROP%20TABLE%20PAGOS--",
        "sortDirection=arriba", "sortDirection=desc%2C%20PK_id_pago", "mes=13", "anio=1999"
    )
    $codigos = foreach ($q in $invalidos) {
        $consulta = @{ anio = "2026"; mes = "9"; page = "1"; pageSize = "10"; sortBy = "fecha"; sortDirection = "desc" }
        $k, $v = $q -split '=', 2; $consulta[$k] = $v
        $r = Pedir "GET" ("/api/recaudacion/pagos?" + (($consulta.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join '&')) $super $null
        "$q->$($r.Codigo)"
    }
    $pagosIntactos = [int](Sql "SELECT COUNT(*) FROM PAGOS") -gt 0
    Registrar "VAL-1" "Página, tamaño, orden, dirección y período inválidos -> 400 (sin SQL inyectado; PAGOS intacta)" (@($codigos | Where-Object { $_ -notmatch '->400$' }).Count -eq 0 -and $pagosIntactos) ($codigos -join ' ')
    # Un parámetro vacío es un parámetro no enviado (así lo bindea ASP.NET): toma el valor por defecto, igual que page y pageSize.
    $vacio = Pedir "GET" "/api/recaudacion/pagos?anio=2026&mes=9&sortBy=&sortDirection=" $super $null
    Registrar "VAL-3" "sortBy y sortDirection vacíos = no enviados: 200 con el orden por defecto (fecha desc)" ($vacio.Codigo -eq 200 -and (($vacio.Json.items | ForEach-Object idPago) -join ',') -eq (($p1.Json.items | ForEach-Object idPago) -join ',')) "$($vacio.Codigo), mismas filas que fecha desc"
    $mayus = Pagina 9 1 10 "MONTO" "ASC"
    Registrar "VAL-2" "sortBy/sortDirection sin distinguir mayúsculas" ($mayus.Codigo -eq 200 -and [decimal]$mayus.Json.items[0].monto -eq 9000) "$($mayus.Codigo) primero=$($mayus.Json.items[0].monto)"

    # ===== Permisos (sin cambios: rol 1) =====
    $a1 = Pedir "GET" "/api/recaudacion/pagos?anio=2026&mes=9" $admin $null
    $a2 = Pedir "GET" "/api/recaudacion?anio=2026&mes=9" $admin $null
    $a3 = Pedir "GET" "/api/recaudacion/pagos?anio=2026&mes=9" $null $null
    $a4 = Pedir "GET" "/api/reportes/recaudacion/csv?anio=2026&mes=9" $admin $null
    Registrar "AUT-1" "Administrador: 403 en tabla, tarjetas y exportación; sin sesión 401" ($a1.Codigo -eq 403 -and $a2.Codigo -eq 403 -and $a4.Codigo -eq 403 -and $a3.Codigo -eq 401) "$($a1.Codigo)/$($a2.Codigo)/$($a4.Codigo)/$($a3.Codigo)"

    # ===== CSV =====
    $csv = Pedir "GET" "/api/reportes/recaudacion/csv?anio=2026&mes=9" $super $null
    [IO.File]::WriteAllBytes((Join-Path $Evidencia "reporte-recaudacion-septiembre.csv"), $csv.Bytes)
    $bom = $csv.Bytes.Length -ge 3 -and $csv.Bytes[0] -eq 0xEF -and $csv.Bytes[1] -eq 0xBB -and $csv.Bytes[2] -eq 0xBF
    $lineas = $csv.Texto.TrimStart([char]0xFEFF) -split "`r?`n"
    $iEnc = [Array]::FindIndex($lineas, [Predicate[string]] { param($l) $l.StartsWith("Fecha;Jugador") })
    $datos = @($lineas | Select-Object -Skip ($iEnc + 1) | Where-Object { $_.Trim() })
    $fechasCsv = @($datos | ForEach-Object { ($_ -split ';')[0] })
    $isoOk = @($fechasCsv | Where-Object { $_ -notmatch '^\d{4}-\d\d-\d\d( \d\d:\d\d)?$' }).Count -eq 0
    $sumaCsv = ($datos | ForEach-Object { [decimal]::Parse(($_ -split ';')[-1], $inv) } | Measure-Object -Sum).Sum
    $totalCsv = $lineas | Where-Object { $_ -like "TOTAL RECAUDADO;*" }
    Registrar "CSV-1" "CSV: BOM UTF-8, separador ';', columna Fecha primera, zona en el encabezado" ($bom -and $iEnc -ge 0 -and $lineas[$iEnc] -eq "Fecha;Jugador;DNI;Categoría;Medio de pago;Monto ($)" -and ($lineas -match '^Fechas;Hora del club \(').Count -eq 1) "$($lineas[$iEnc]) | $(@($lineas -match '^Fechas;')[0])"
    Registrar "CSV-2" "CSV: TODO el período ($($sep.Pagos) filas, no 10), fechas ISO, montos numéricos que suman el total" ($datos.Count -eq $sep.Pagos -and $isoOk -and [decimal]$sumaCsv -eq $sep.Total -and $totalCsv -eq "TOTAL RECAUDADO;$($sep.Pagos);$($sep.Total.ToString('0', $inv))") "filas=$($datos.Count) ISO=$isoOk suma=$sumaCsv | $totalCsv | ej. $($datos[0])"
    $ordenCsv = (@($datos | ForEach-Object { ($_ -split ';')[0] }) -join ',') -eq (@($recorridos["fecha desc"].Filas | ForEach-Object { if ($_.fechaHoraRegistro) { Hora $_.fechaHoraRegistro "yyyy-MM-dd HH:mm" } else { $_.fechaPago } }) -join ',')
    Registrar "CSV-3" "CSV en el mismo orden que la tabla por defecto (fecha desc) y con la misma fecha" $ordenCsv "igual=$ordenCsv"

    # ===== PDF =====
    $pdf = Pedir "GET" "/api/reportes/recaudacion/pdf?anio=2026&mes=9" $super $null
    $rutaPdf = Join-Path $Evidencia "reporte-recaudacion-septiembre.pdf"
    [IO.File]::WriteAllBytes($rutaPdf, $pdf.Bytes)
    $pdftotext = (Get-Command pdftotext -ErrorAction SilentlyContinue)?.Source ?? "C:\Program Files\Git\mingw64\bin\pdftotext.exe"
    $textoPdf = (& $pdftotext -layout $rutaPdf - 2>$null) -join "`n"
    [IO.File]::WriteAllText((Join-Path $Evidencia "reporte-recaudacion-septiembre.txt"), $textoPdf, [Text.UTF8Encoding]::new($false))
    $fechasPdf = [regex]::Matches($textoPdf, '(?m)^\s*(\d\d/\d\d/\d{4})( \d\d:\d\d)?\s').Count
    Registrar "PDF-1" "PDF: columna Fecha, una fila por cobro del período ($($sep.Pagos), no 10), en dd/MM/yyyy" ($pdf.Codigo -eq 200 -and [Text.Encoding]::ASCII.GetString($pdf.Bytes, 0, 4) -eq "%PDF" -and $textoPdf -match 'Fecha\s+Jugador' -and $fechasPdf -eq $sep.Pagos) "filas con fecha=$fechasPdf, $($pdf.Bytes.Length) bytes"
    Registrar "PDF-2" "PDF: aclara la zona horaria y el total del período" ($textoPdf -match 'Hora del club' -and $textoPdf -match "Total recaudado \($($sep.Pagos) cobros\)") "$([regex]::Match($textoPdf, 'Hora del club[^\n]*').Value.Trim()) | $([regex]::Match($textoPdf, 'Total recaudado \(\d+ cobros\)').Value)"

    # ===== Tiempos (julio: 5.000 cobros) =====
    $jul = Esperado 7
    $medir = {
        param($ruta)
        $ms = @(); for ($i = 0; $i -lt 10; $i++) { $ms += (Pedir "GET" $ruta $super $null).Ms }
        $o = $ms | Sort-Object; [pscustomobject]@{ Mediana = $o[4]; Max = $o[-1] }
    }
    $tiempos = [ordered]@{
        "pagos sep p1" = & $medir "/api/recaudacion/pagos?anio=2026&mes=9&page=1&pageSize=10&sortBy=fecha&sortDirection=desc"
        "pagos jul p1 fecha" = & $medir "/api/recaudacion/pagos?anio=2026&mes=7&page=1&pageSize=10&sortBy=fecha&sortDirection=desc"
        "pagos jul p250 monto" = & $medir "/api/recaudacion/pagos?anio=2026&mes=7&page=250&pageSize=10&sortBy=monto&sortDirection=asc"
        "totales jul" = & $medir "/api/recaudacion?anio=2026&mes=7"
        "csv jul" = & $medir "/api/reportes/recaudacion/csv?anio=2026&mes=7"
    }
    $pj = Pagina 7 501
    Registrar "VOL-1" "Julio ($($jul.Pagos) cobros): total y páginas correctos, última página con el resto" ($pj.Json.total -eq $jul.Pagos -and $pj.Json.totalPaginas -eq [math]::Ceiling($jul.Pagos / 10) -and $pj.Json.items.Count -eq ($jul.Pagos - 10 * ([math]::Ceiling($jul.Pagos / 10) - 1))) "total=$($pj.Json.total) páginas=$($pj.Json.totalPaginas) última=$($pj.Json.items.Count)"
    $maxPagina = ($tiempos.GetEnumerator() | Where-Object { $_.Key -like "pagos*" } | ForEach-Object { $_.Value.Max } | Measure-Object -Maximum).Maximum
    Registrar "PERF-1" "Página de la tabla < 1 s aun con 5.000 cobros (10 pedidos por caso)" ($maxPagina -lt 1000) (($tiempos.GetEnumerator() | ForEach-Object { "$($_.Key): mediana $($_.Value.Mediana) ms, máx $($_.Value.Max) ms" }) -join '; ')
    $tamPagina = (Pagina 7).Bytes.Length
    Registrar "PERF-2" "La página viaja con 10 filas, no con el mes entero" ($tamPagina -lt 5000) "$tamPagina bytes por página (julio)"

    if ($Navegador) {
        Write-Host "Levantando ng serve…"
        $front = Join-Path $repo "frontend-cacc"
        Iniciar "cmd.exe" "/c npx ng serve --port 4200" $front @{} (Join-Path $Evidencia "ng-serve.log") | Out-Null
        for ($i = 0; $i -lt 240; $i++) { try { if ($http.GetAsync("http://localhost:4200/").Result.IsSuccessStatusCode) { break } } catch { }; Start-Sleep -Milliseconds 500 }
        $logNav = Join-Path $Evidencia "salida_navegador.txt"
        # La salida se lee con ReadToEndAsync y no con eventos: las acciones de Register-ObjectEvent recién corren cuando
        # este hilo queda libre, y mientras espera a node están en cola (el log quedaba con la primera línea sola).
        $info = [Diagnostics.ProcessStartInfo]::new("node", "`"$(Join-Path $PSScriptRoot 'qa_navegador_hu107.mjs')`" `"$(Join-Path $Evidencia 'capturas')`"")
        $info.WorkingDirectory = $repo; $info.UseShellExecute = $false; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
        $info.EnvironmentVariables["CACC_CLAVE"] = $clave
        $info.EnvironmentVariables["CACC_USUARIO"] = "95900001"
        $nodo = [Diagnostics.Process]::Start($info); $procesos.Add($nodo)
        $salida = $nodo.StandardOutput.ReadToEndAsync(); $errores = $nodo.StandardError.ReadToEndAsync()
        if (-not $nodo.WaitForExit(300000)) { Registrar "NAV-0" "El recorrido en el navegador terminó a tiempo (5 min)" $false "timeout"; & taskkill /PID $nodo.Id /T /F 2>&1 | Out-Null }
        $salidaNav = $salida.Result + $errores.Result
        [IO.File]::WriteAllText($logNav, $salidaNav, [Text.UTF8Encoding]::new($false))
        Write-Host $salidaNav
        foreach ($linea in ($salidaNav -split "`r?`n" | Where-Object { $_ -match '^(PASS|FAIL)\s' })) {
            $m = [regex]::Match($linea, '^(PASS|FAIL)\s+(\S+)\s+(.*?) \| (.*)$')
            if ($m.Success) { Registrar $m.Groups[2].Value $m.Groups[3].Value ($m.Groups[1].Value -eq "PASS") $m.Groups[4].Value }
        }
        if ($nodo.HasExited -and $nodo.ExitCode -ne 0 -and -not ($salidaNav -match '(?m)^(PASS|FAIL)\s')) { Registrar "NAV-0" "El script de navegador corrió" $false "exit $($nodo.ExitCode)" }
    }
}
finally {
    Detener
    Get-EventSubscriber | Unregister-Event -ErrorAction SilentlyContinue
}

$resumen = [ordered]@{
    fecha = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    base = $Base
    septiembre = [ordered]@{ pagos = $sep.Pagos; total = $sep.Total; efectivo = $sep.Efectivo; transferencia = $sep.Transferencia }
    octubre = [ordered]@{ pagos = $oct.Pagos; total = $oct.Total }
    julio = [ordered]@{ pagos = $jul.Pagos }
    tiempos = $tiempos
    resultados = $resultados
}
$resumen | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $Evidencia "evidencia.json") -Encoding utf8
$fallas = @($resultados | Where-Object Estado -eq "FAIL").Count
Write-Host ("`nResultado: {0} PASS / {1} FAIL" -f (@($resultados | Where-Object Estado -eq "PASS").Count), $fallas)
exit ([int]($fallas -gt 0))
