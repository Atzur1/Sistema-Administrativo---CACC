# HU-059/HU-060: infraestructura compartida de las suites de punta a punta (resultados, SQL de verificación, arranque y
# healthchecks de las APIs, pedidos HTTP). Se incluye con dot-sourcing DESPUÉS de definir los parámetros de la suite
# ($Servidor, $Base, $PuertoAdministrativo, $PuertoDeportivo, $ClaveCuentasPrueba, $TimeoutSegundos, $Salida).

# ---------------------------------------------------------------- resultados
$resultados = New-Object Collections.Generic.List[object]
function Registrar([string]$Grupo, [string]$Id, [string]$Caso, [string]$Estado, $Esperado, $Obtenido, [string]$Detalle = "") {
    $resultados.Add([pscustomobject]@{ Grupo = $Grupo; Id = $Id; Caso = $Caso; Estado = $Estado; Esperado = "$Esperado"; Obtenido = "$Obtenido"
        Detalle = $Detalle.Substring(0, [Math]::Min(300, $Detalle.Length)) })
    $color = @{ PASS = "Green"; FAIL = "Red"; BLOCKED = "Yellow" }[$Estado]
    Write-Host ("{0,-7} {1,-8} {2}  [esperado: {3} | obtenido: {4}]" -f $Estado, $Id, $Caso, $Esperado, $Obtenido) -ForegroundColor $color
}
function Verificar([string]$Grupo, [string]$Id, [string]$Caso, [bool]$Condicion, $Esperado, $Obtenido, [string]$Detalle = "") {
    Registrar $Grupo $Id $Caso $(if ($Condicion) { "PASS" } else { "FAIL" }) $Esperado $Obtenido $Detalle
}

# ---------------------------------------------------------------- SQL (solo para VALIDAR lo persistido)
function Sql([string]$Consulta) {
    $texto = & sqlcmd -S $Servidor -E -C -b -I -W -h -1 -f 65001 -d $Base -Q "SET NOCOUNT ON; $Consulta" 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "SQL: $texto" }
    $texto.Trim()
}

# ---------------------------------------------------------------- APIs
function Azar([int]$bytes = 48) {
    $b = New-Object byte[] $bytes; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
    [Convert]::ToBase64String($b).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}
$claveJwt = Azar 48
$credencial = Azar 32
$cadena = "Server=$Servidor;Database=$Base;Trusted_Connection=True;TrustServerCertificate=True;"
$urlAdministrativo = "http://localhost:$PuertoAdministrativo"
$urlDeportivo = "http://localhost:$PuertoDeportivo"
$procesos = New-Object Collections.Generic.List[object]

function Iniciar([string]$Nombre, [string]$Dll, [int]$Puerto, [hashtable]$Entorno) {
    $info = New-Object Diagnostics.ProcessStartInfo "dotnet", "`"$Dll`""
    $info.WorkingDirectory = Split-Path $Dll
    $info.UseShellExecute = $false; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $info.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Development"
    $info.EnvironmentVariables["ASPNETCORE_URLS"] = "http://localhost:$Puerto"
    foreach ($k in $Entorno.Keys) { $info.EnvironmentVariables[$k] = $Entorno[$k] }
    $p = [Diagnostics.Process]::Start($info)
    $api = [pscustomobject]@{ Nombre = $Nombre; Proceso = $p; Puerto = $Puerto; Salida = $p.StandardOutput.ReadToEndAsync(); Error = $p.StandardError.ReadToEndAsync()
        Log = (Join-Path $Salida "$Nombre.log") }
    $procesos.Add($api)
    $api
}
function Detener($api) {
    if (-not $api.Proceso.HasExited) { $api.Proceso.Kill(); $api.Proceso.WaitForExit(10000) | Out-Null }
    $texto = $api.Salida.Result + "`n" + $api.Error.Result
    [IO.File]::WriteAllText($api.Log, $texto)
    $texto
}
function EntornoAdministrativo([bool]$Generador) {
    @{ "ConnectionStrings__ConexionSQL" = $cadena; "Jwt__Key" = $claveJwt; "Jwt__Issuer" = "CACC-HU059"; "Jwt__Audience" = "CACC-HU059"
       "Integracion__ClienteDeportivo__Clave" = $credencial; "DatabaseMigrations__Enabled" = "false"
       "GeneracionCuotas__Habilitada" = $(if ($Generador) { "true" } else { "false" })
       # Ningún correo sale de una prueba: el SMTP apunta a un puerto local cerrado, así que habilitar una cuenta responde
       # "no se pudo enviar el correo" sin contactar ningún servidor real.
       "Email__SmtpHost" = "127.0.0.1"; "Email__SmtpPort" = "9"; "Email__EnableSsl" = "false"; "Email__LogResetLink" = "false" }
}
function EntornoDeportivo([string]$Clave) {
    @{ "ConnectionStrings__DefaultConnection" = $cadena; "Jwt__Key" = $claveJwt; "Jwt__Issuer" = "CACC-HU059"; "Jwt__Audience" = "CACC-HU059"
       "AdministrativoApi__BaseUrl" = $urlAdministrativo; "AdministrativoApi__ClaveIntegracion" = $Clave; "AdministrativoApi__TimeoutSegundos" = "5" }
}

$http = New-Object Net.Http.HttpClient
$http.Timeout = [TimeSpan]::FromSeconds(30)
function Pedir([string]$Metodo, [string]$Url, [string]$Token, [object]$Clave, [object]$Cuerpo) {
    $mensaje = New-Object Net.Http.HttpRequestMessage ([Net.Http.HttpMethod]::new($Metodo)), $Url
    if ($Token) { $mensaje.Headers.Authorization = New-Object Net.Http.Headers.AuthenticationHeaderValue "Bearer", $Token }
    if ($null -ne $Clave) { $mensaje.Headers.TryAddWithoutValidation("X-Integration-Key", [string[]]@($Clave)) | Out-Null }
    if ($null -ne $Cuerpo) {
        $json = if ($Cuerpo -is [string]) { $Cuerpo } else { $Cuerpo | ConvertTo-Json -Depth 5 -Compress }
        $mensaje.Content = New-Object Net.Http.StringContent $json, ([Text.Encoding]::UTF8), "application/json"
    }
    try {
        $respuesta = $http.SendAsync($mensaje).Result
        [pscustomobject]@{ Codigo = [int]$respuesta.StatusCode; Cuerpo = $respuesta.Content.ReadAsStringAsync().Result }
    } catch {
        [pscustomobject]@{ Codigo = 0; Cuerpo = "sin respuesta: $($_.Exception.InnerException.Message)" }
    }
}
function Json([string]$Texto) { try { $Texto | ConvertFrom-Json } catch { $null } }

# Healthcheck: polling con timeout explícito. "Lista" = responde algo que solo puede responder leyendo SQL Server.
function Esperar([string]$Que, [scriptblock]$Prueba, $Api) {
    $limite = (Get-Date).AddSeconds($TimeoutSegundos)
    $ultimo = ""
    while ((Get-Date) -lt $limite) {
        if ($Api -and $Api.Proceso.HasExited) { return [pscustomobject]@{ Ok = $false; Detalle = "el proceso terminó (código $($Api.Proceso.ExitCode))" } }
        try { $r = & $Prueba; if ($r.Ok) { return $r }; $ultimo = $r.Detalle } catch { $ultimo = $_.Exception.Message }
        Start-Sleep -Milliseconds 500
    }
    [pscustomobject]@{ Ok = $false; Detalle = "timeout de $TimeoutSegundos s; último estado: $ultimo" }
}
function Login {
    $r = Pedir "POST" "$urlAdministrativo/api/auth/login" $null $null @{ username = "20111222"; password = $ClaveCuentasPrueba }
    [pscustomobject]@{ Ok = ($r.Codigo -eq 200); Detalle = "login -> $($r.Codigo)"; Token = $(if ($r.Codigo -eq 200) { (Json $r.Cuerpo).token }) }
}
