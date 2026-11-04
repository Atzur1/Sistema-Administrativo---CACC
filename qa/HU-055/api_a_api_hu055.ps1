<#
.SYNOPSIS
    HU-055: prueba API -> API. El backend del Portal Deportivo (con parches/ellos-credencial-integracion.patch) consulta la
    API de integración del Administrativo con su credencial de sistema y el token del usuario.

.DESCRIPTION
    Levanta las dos APIs sobre la misma base de PRUEBA (la de e2e_hu055.ps1, con sus cuentas y su jugador), con una clave
    JWT y una credencial de integración generadas al azar para la corrida (no se imprimen). Casos:
      A. el Deportivo con la credencial correcta: su GET /api/jugadores/{id} trae el estado de cuota que informa el Administrativo;
      B. el Deportivo con una credencial equivocada: el Administrativo responde 401 y el Deportivo no puede verificar el estado;
      C. el Deportivo sin credencial: no arranca.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$DeportivoDll,
    [string]$Servidor = "(localdb)\MSSQLLocalDB",
    [string]$Base = "ClubCamionerosUnificadaV2_HU055_Prueba",
    [string]$Clave = "Camioneros.Test.2026!",
    [int]$PuertoAdministrativo = 5199,
    [int]$PuertoDeportivo = 5299,
    [string]$Salida = (Join-Path $env:TEMP "cacc-hu055")
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$administrativoDll = Join-Path $repo "ApiGestion\bin\Debug\net9.0\ApiGestion.dll"
New-Item -ItemType Directory -Force -Path $Salida | Out-Null
Add-Type -AssemblyName System.Net.Http
$cadena = "Server=$Servidor;Database=$Base;Trusted_Connection=True;TrustServerCertificate=True;"

function Azar([int]$bytes = 32) {
    $b = New-Object byte[] $bytes; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
    [Convert]::ToBase64String($b).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}
function Iniciar([string]$Dll, [int]$Puerto, [hashtable]$Entorno) {
    $info = New-Object Diagnostics.ProcessStartInfo "dotnet", "`"$Dll`""
    $info.WorkingDirectory = Split-Path $Dll
    $info.UseShellExecute = $false; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $info.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Development"
    $info.EnvironmentVariables["ASPNETCORE_URLS"] = "http://localhost:$Puerto"
    foreach ($k in $Entorno.Keys) { $info.EnvironmentVariables[$k] = $Entorno[$k] }
    $p = [Diagnostics.Process]::Start($info)
    [pscustomobject]@{ Proceso = $p; Salida = $p.StandardOutput.ReadToEndAsync(); Error = $p.StandardError.ReadToEndAsync(); Puerto = $Puerto }
}
function Detener($api) {
    if (-not $api.Proceso.HasExited) { $api.Proceso.Kill(); $api.Proceso.WaitForExit(10000) | Out-Null }
    $api.Salida.Result + "`n" + $api.Error.Result
}
function Esperar($api) {
    $c = New-Object Net.Http.HttpClient
    for ($i = 0; $i -lt 60; $i++) {
        if ($api.Proceso.HasExited) { return $false }
        try { $c.GetAsync("http://localhost:$($api.Puerto)/").Result | Out-Null; return $true } catch { Start-Sleep -Milliseconds 500 }
    }
    $false
}
function Pedir([string]$Metodo, [string]$Url, [string]$Token, [string]$Cuerpo) {
    $c = New-Object Net.Http.HttpClient
    $m = New-Object Net.Http.HttpRequestMessage ([Net.Http.HttpMethod]::new($Metodo)), $Url
    if ($Token) { $m.Headers.Authorization = New-Object Net.Http.Headers.AuthenticationHeaderValue "Bearer", $Token }
    if ($Cuerpo) { $m.Content = New-Object Net.Http.StringContent $Cuerpo, ([Text.Encoding]::UTF8), "application/json" }
    $r = $c.SendAsync($m).Result
    [pscustomobject]@{ Codigo = [int]$r.StatusCode; Cuerpo = $r.Content.ReadAsStringAsync().Result }
}

$credencial = Azar
$jwt = @{ "Jwt__Key" = (Azar 48); "Jwt__Issuer" = "CACC-HU055"; "Jwt__Audience" = "CACC-HU055" }
$idJugador = (& sqlcmd -S $Servidor -E -C -b -W -h -1 -d $Base -Q "SET NOCOUNT ON; SELECT j.PK_id_jugador FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni = '41000055'" | Out-String).Trim()
if (-not $idJugador) { throw "Correr antes e2e_hu055.ps1 (crea el jugador de prueba)." }

$entornoAdministrativo = $jwt.Clone(); $entornoAdministrativo["ConnectionStrings__ConexionSQL"] = $cadena; $entornoAdministrativo["Integracion__ClienteDeportivo__Clave"] = $credencial
$administrativo = Iniciar $administrativoDll $PuertoAdministrativo $entornoAdministrativo
if (-not (Esperar $administrativo)) { Detener $administrativo | Out-Null; throw "No arrancó el Administrativo." }
$login = Pedir "POST" "http://localhost:$PuertoAdministrativo/api/auth/login" $null (@{ username = "20111222"; password = $Clave } | ConvertTo-Json)
$token = ($login.Cuerpo | ConvertFrom-Json).token

$resultados = @()
foreach ($caso in @(
    @{ N = "A. Deportivo con la credencial correcta"; Clave = $credencial; Arranca = $true },
    @{ N = "B. Deportivo con una credencial equivocada"; Clave = (Azar); Arranca = $true },
    @{ N = "C. Deportivo sin credencial"; Clave = ""; Arranca = $false })) {
    $entorno = $jwt.Clone()
    $entorno["ConnectionStrings__DefaultConnection"] = $cadena
    $entorno["AdministrativoApi__BaseUrl"] = "http://localhost:$PuertoAdministrativo"
    $entorno["AdministrativoApi__ClaveIntegracion"] = $caso.Clave
    $deportivo = Iniciar $DeportivoDll $PuertoDeportivo $entorno
    if (-not $caso.Arranca) {
        $termino = $deportivo.Proceso.WaitForExit(60000)
        $log = Detener $deportivo
        $resultados += [pscustomobject]@{ Caso = $caso.N; Obtenido = "terminó=$termino, nombra la configuración=$($log -match 'AdministrativoApi:ClaveIntegracion')"
            Resultado = if ($termino -and $deportivo.Proceso.ExitCode -ne 0 -and $log -match 'AdministrativoApi:ClaveIntegracion') { "PASS" } else { "FAIL" } }
        continue
    }
    if (-not (Esperar $deportivo)) { $log = Detener $deportivo; $resultados += [pscustomobject]@{ Caso = $caso.N; Obtenido = "no arrancó"; Resultado = "FAIL" }; continue }
    $r = Pedir "GET" "http://localhost:$PuertoDeportivo/api/jugadores/$idJugador" $token $null
    $estado = try { ($r.Cuerpo | ConvertFrom-Json).estadoCuota } catch { "<sin cuerpo>" }
    $logDeportivo = Detener $deportivo
    $resultados += [pscustomobject]@{ Caso = $caso.N; Obtenido = "HTTP $($r.Codigo), estadoCuota = '$estado'"; Resultado = "" ; Estado = $estado; LogDeportivo = $logDeportivo }
}
$logAdministrativo = Detener $administrativo

$a = $resultados[0]; $b = $resultados[1]
$rechazosAdministrativo = ([regex]::Matches($logAdministrativo, 'Credencial de sistema inv.lida')).Count
$a.Resultado = if ($a.Obtenido -match '^HTTP 200' -and $a.Estado -and $a.Estado -ne $b.Estado) { "PASS" } else { "FAIL" }
$b.Resultado = if ($b.Obtenido -match '^HTTP 200' -and $rechazosAdministrativo -ge 1) { "PASS" } else { "FAIL" }
$b.Obtenido += " | el Administrativo registró $rechazosAdministrativo rechazo(s) por credencial inválida"
$secretoEnLogs = ($logAdministrativo + $a.LogDeportivo + $b.LogDeportivo) -like "*$credencial*"
$resultados += [pscustomobject]@{ Caso = "D. La credencial no aparece en los logs de ninguna de las dos APIs"; Obtenido = "aparece=$secretoEnLogs"; Resultado = if (-not $secretoEnLogs) { "PASS" } else { "FAIL" } }

$resultados | ForEach-Object { "{0} => {1}" -f $_.Caso, $_.Resultado }
if ($resultados | Where-Object Resultado -ne "PASS") { exit 1 }
