<#
.SYNOPSIS
    HU-055 (code review): con ASPNETCORE_ENVIRONMENT=Production la API real rechaza HTTP antes de validar credenciales, y
    X-Forwarded-Proto solo vale si viene de un proxy confiable.

.DESCRIPTION
    Levanta la API compilada en Production, por HTTP (sin certificado), contra una base de PRUEBA, con JWT y credencial al
    azar. No escribe en la base: solo pide endpoints de lectura. Cada escenario arranca su propia instancia.
    Códigos de salida: 0 todo PASS; 1 algún FAIL; 2 entorno no disponible.
#>
#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$Servidor = "(localdb)\MSSQLLocalDB",
    [string]$Base = "ClubCamionerosUnificadaV2_HU059_Prueba",
    [string]$AdministrativoDll = "",
    [int]$Puerto = 5481
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (-not $AdministrativoDll) { $AdministrativoDll = Join-Path $repo "ApiGestion\bin\Debug\net9.0\ApiGestion.dll" }
if (-not (Test-Path $AdministrativoDll)) { Write-Host "ENTORNO NO DISPONIBLE: falta $AdministrativoDll"; exit 2 }
Add-Type -AssemblyName System.Net.Http

function Azar([int]$n) { $b = New-Object byte[] $n; [Security.Cryptography.RandomNumberGenerator]::Fill($b); [Convert]::ToBase64String($b).TrimEnd('=').Replace('+', '-').Replace('/', '_') }
$claveJwt = Azar 48
$credencial = Azar 32
$tokenFalso = "eyJ" + (Azar 30)
$resultados = New-Object Collections.Generic.List[object]
function Verificar([string]$Id, [string]$Caso, [bool]$Ok, [string]$Obtenido) {
    $resultados.Add([pscustomobject]@{ Id = $Id; Caso = $Caso; Estado = $(if ($Ok) { "PASS" } else { "FAIL" }); Obtenido = $Obtenido })
    Write-Host ("{0,-5} {1,-4} {2}  [{3}]" -f $(if ($Ok) { "PASS" } else { "FAIL" }), $Id, $Caso, $Obtenido) -ForegroundColor $(if ($Ok) { "Green" } else { "Red" })
}

function Levantar([hashtable]$Extra) {
    $info = New-Object Diagnostics.ProcessStartInfo "dotnet", "`"$AdministrativoDll`""
    $info.WorkingDirectory = Split-Path $AdministrativoDll
    $info.UseShellExecute = $false; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $e = $info.EnvironmentVariables
    $e["ASPNETCORE_ENVIRONMENT"] = "Production"; $e["ASPNETCORE_URLS"] = "http://localhost:$Puerto"
    $e["ConnectionStrings__ConexionSQL"] = "Server=$Servidor;Database=$Base;Trusted_Connection=True;TrustServerCertificate=True;"
    $e["Jwt__Key"] = $claveJwt; $e["Jwt__Issuer"] = "CACC-HTTPS"; $e["Jwt__Audience"] = "CACC-HTTPS"
    $e["Integracion__ClienteDeportivo__Clave"] = $credencial; $e["GeneracionCuotas__Habilitada"] = "false"
    foreach ($k in $Extra.Keys) { $e[$k] = $Extra[$k] }
    $p = [Diagnostics.Process]::Start($info)
    $api = [pscustomobject]@{ Proceso = $p; Salida = $p.StandardOutput.ReadToEndAsync(); Error = $p.StandardError.ReadToEndAsync() }
    $limite = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $limite -and -not $p.HasExited) {
        try { (New-Object Net.Sockets.TcpClient("localhost", $Puerto)).Dispose(); return $api } catch { Start-Sleep -Milliseconds 300 }
    }
    throw "La API no quedó escuchando en $Puerto. $($api.Error.Result)"
}
function Bajar($api) { if (-not $api.Proceso.HasExited) { $api.Proceso.Kill(); $api.Proceso.WaitForExit(10000) | Out-Null }; $api.Salida.Result + "`n" + $api.Error.Result }

$http = New-Object Net.Http.HttpClient (New-Object Net.Http.HttpClientHandler -Property @{ AllowAutoRedirect = $false })
function Pedir([string]$Metodo, [string]$Ruta, [hashtable]$Headers, [string]$Cuerpo) {
    $m = New-Object Net.Http.HttpRequestMessage ([Net.Http.HttpMethod]::new($Metodo)), "http://localhost:$Puerto$Ruta"
    foreach ($k in $Headers.Keys) { $m.Headers.TryAddWithoutValidation($k, [string]$Headers[$k]) | Out-Null }
    if ($Cuerpo) { $m.Content = New-Object Net.Http.StringContent $Cuerpo, ([Text.Encoding]::UTF8), "application/json" }
    $r = $http.SendAsync($m).Result
    [pscustomobject]@{ Codigo = [int]$r.StatusCode; Cuerpo = $r.Content.ReadAsStringAsync().Result; Location = [string]$r.Headers.Location }
}
$integracion = "/api/integracion/v1/estado-deuda/dni/98590003"
$conCredenciales = @{ "X-Integration-Key" = $credencial; "Authorization" = "Bearer $tokenFalso" }
$logs = @()

try {
    # ---- 1. Production por HTTP, sin proxy: todo se rechaza antes de validar nada
    $api = Levantar @{}
    $r1 = Pedir "GET" $integracion $conCredenciales
    $r2 = Pedir "POST" "/api/auth/login" @{} '{"username":"20111222","password":"x"}'
    $r3 = Pedir "GET" "/api/estado-deuda/dni/98590003" $conCredenciales
    $log = Bajar $api; $logs += $log
    Verificar "H1" "Integración por HTTP con credencial y JWT: 403 FORBIDDEN, sin redirección" ($r1.Codigo -eq 403 -and $r1.Cuerpo -match '"errorCode":"FORBIDDEN"' -and -not $r1.Location) "$($r1.Codigo) $($r1.Cuerpo) Location='$($r1.Location)'"
    Verificar "H2" "Login por HTTP: 403 con el formato del portal (no llega a validar la contraseña)" ($r2.Codigo -eq 403 -and $r2.Cuerpo -match '"exito":false') "$($r2.Codigo) $($r2.Cuerpo)"
    Verificar "H3" "Alias /api/estado-deuda por HTTP: 403" ($r3.Codigo -eq 403) "$($r3.Codigo)"
    Verificar "H4" "El log registra el rechazo sin la ruta, el DNI, la credencial ni el token" `
        ($log -match 'no usar HTTPS' -and $log -notmatch '98590003' -and -not $log.Contains($credencial) -and -not $log.Contains($tokenFalso)) "rechazo en log: $($log -match 'no usar HTTPS')"

    # ---- 2. Detrás de un proxy de una red NO confiable: X-Forwarded-Proto: https no alcanza
    $api = Levantar @{ "ForwardedHeaders__Enabled" = "true"; "ForwardedHeaders__KnownNetworks" = "192.0.2.0/24" }
    $r4 = Pedir "GET" $integracion ($conCredenciales + @{ "X-Forwarded-Proto" = "https"; "X-Forwarded-For" = "198.51.100.1" })
    $logs += Bajar $api
    Verificar "H5" "Un salto que no es proxy confiable no puede declararse HTTPS: 403" ($r4.Codigo -eq 403) "$($r4.Codigo) $($r4.Cuerpo)"

    # ---- 3. Detrás de un proxy confiable (loopback, por defecto) que informa https: pasa al chequeo de credencial y token
    $api = Levantar @{ "ForwardedHeaders__Enabled" = "true" }
    $r5 = Pedir "GET" $integracion @{ "X-Forwarded-Proto" = "https"; "X-Forwarded-For" = "198.51.100.1" }
    $r6 = Pedir "GET" $integracion ($conCredenciales + @{ "X-Forwarded-Proto" = "https" })
    $r7 = Pedir "GET" $integracion @{ "X-Forwarded-Proto" = "http"; "X-Integration-Key" = $credencial }
    $log = Bajar $api; $logs += $log
    Verificar "H6" "Proxy confiable con https y sin credencial: 401 UNAUTHORIZED de HU-055 (pasó el chequeo HTTPS)" ($r5.Codigo -eq 401 -and $r5.Cuerpo -match 'UNAUTHORIZED') "$($r5.Codigo) $($r5.Cuerpo)"
    Verificar "H7" "Proxy confiable con https, credencial y un JWT inválido: 401 del token (llega a la autenticación)" ($r6.Codigo -eq 401) "$($r6.Codigo)"
    Verificar "H8" "Proxy confiable que informa http (PUBLIC_SCHEME=http): 403" ($r7.Codigo -eq 403) "$($r7.Codigo)"
    Verificar "H9" "Sin lista explícita, el log avisa que se usan las redes por defecto" ($log -match 'se confía solo en loopback y redes privadas') "aviso: $($log -match 'se confía solo en loopback')"

    # ---- 4. Seguridad:PermitirHttpSinTls=true (solo para la propia máquina): acepta HTTP y lo avisa
    $api = Levantar @{ "Seguridad__PermitirHttpSinTls" = "true" }
    $r8 = Pedir "GET" $integracion @{}
    $log = Bajar $api; $logs += $log
    Verificar "H10" "Con PermitirHttpSinTls: el pedido HTTP sigue (401 por falta de credencial) y el log lo advierte" ($r8.Codigo -eq 401 -and $log -match 'PermitirHttpSinTls=true') "$($r8.Codigo); aviso: $($log -match 'PermitirHttpSinTls=true')"
}
catch {
    Verificar "H-X" "La ejecución se interrumpió" $false $_.Exception.Message
    if ($api) { $logs += Bajar $api }
}

$todo = $logs -join "`n"
Verificar "H11" "Ningún log contiene la credencial, la clave JWT ni el token" (-not $todo.Contains($credencial) -and -not $todo.Contains($claveJwt) -and -not $todo.Contains($tokenFalso)) "ok"
$pass = @($resultados | Where-Object Estado -eq "PASS").Count
$fail = @($resultados | Where-Object Estado -eq "FAIL").Count
Write-Host "`nTotal: $pass PASS | $fail FAIL"
if ($fail -gt 0) { exit 1 }
exit 0
