<#
.SYNOPSIS
    HU-059: deja la base de prueba de la integración en un estado inicial conocido, siempre el mismo.

.DESCRIPTION
    0. Antes de cualquier SQL valida servidor, nombre y entorno, y que la base (si existe) la haya creado este script
       (seguridad_base.ps1; pruebas en pruebas_seguridad_reset.ps1).
    1. Borra la base y la crea con el esquema unificado de HU-053
       (database/esquema/ClubCamioneros_unificada_crear_base.sql): tablas, constraints, SEQUENCE, índices, triggers y vistas.
    2. Aplica las migraciones posteriores (database/migrations) con el migrador de la propia API
       (--DatabaseMigrations:SoloMigrar=true): el mismo código y el mismo hash que en producción.
    3. Carga las cuentas ficticias (datos/01_cuentas_prueba.sql) y los datos de negocio (datos/02_fixtures_e2e.sql).
    4. Verifica que no quedó ninguna migración sin aplicar y que los datos de prueba están.
    No hace DELETE de nada: el reset es recrear la base.

.EXAMPLE
    .\qa\HU-059\reset_base.ps1 -Base ClubCamionerosUnificadaV2_HU059_Prueba
#>
[CmdletBinding()]
param(
    [string]$Servidor = $(if ($env:CACC_E2E_SQL_SERVIDOR) { $env:CACC_E2E_SQL_SERVIDOR } else { "(localdb)\MSSQLLocalDB" }),
    [string]$Base = $(if ($env:CACC_E2E_BASE) { $env:CACC_E2E_BASE } else { "ClubCamionerosUnificadaV2_HU059_Prueba" }),
    [string]$AdministrativoDll = "",
    [string]$Salida = (Join-Path $env:TEMP "cacc-hu059"),
    # Una base de prueba creada antes de que existiera la marca: se acepta una vez, a pedido explícito, y queda marcada.
    [switch]$AdoptarBaseSinMarca
)

$ErrorActionPreference = "Stop"
# Solo una base de prueba creada por este script se puede borrar (ver seguridad_base.ps1). Se valida ANTES de cualquier SQL.
. (Join-Path $PSScriptRoot "seguridad_base.ps1")
Assert-DestinoDePrueba $Servidor $Base
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (-not $AdministrativoDll) { $AdministrativoDll = Join-Path $repo "ApiGestion\bin\Debug\net9.0\ApiGestion.dll" }
if (-not (Test-Path $AdministrativoDll)) { throw "No encuentro $AdministrativoDll. Compilar antes: dotnet build ApiGestion" }
New-Item -ItemType Directory -Force -Path $Salida | Out-Null
$log = Join-Path $Salida "reset_base.log"
"HU-059 reset - $(Get-Date -Format s) - $Servidor / $Base" | Set-Content $log

function Sql([string]$BaseSql, [string]$Consulta, [string]$Archivo) {
    $argumentos = @("-S", $Servidor, "-E", "-C", "-b", "-I", "-W", "-h", "-1", "-f", "65001", "-d", $BaseSql)
    if ($Archivo) { $argumentos += @("-i", $Archivo) } else { $argumentos += @("-Q", "SET NOCOUNT ON; $Consulta") }
    $texto = & sqlcmd @argumentos 2>&1 | Out-String
    "`n== $(if ($Archivo) { Split-Path $Archivo -Leaf } else { 'consulta' }) -> $LASTEXITCODE`n$texto" | Add-Content $log
    if ($LASTEXITCODE -ne 0) { throw "SQL falló ($(if ($Archivo) { Split-Path $Archivo -Leaf } else { $Consulta.Substring(0, [Math]::Min(60, $Consulta.Length)) })). Ver $log" }
    $texto.Trim()
}
function Azar([int]$bytes = 48) {
    $b = New-Object byte[] $bytes; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b)
}

$inicio = Get-Date
# ---- 0. la base, si existe, tiene que ser una que creó este script (solo lectura)
$origen = Assert-MarcaDePrueba $Base { param($b, $c) Sql $b $c } -AdoptarBaseSinMarca:$AdoptarBaseSinMarca
"base: $origen" | Add-Content $log

# ---- 1. esquema unificado (HU-053). El nombre ya es un identificador validado; igual va con QUOTENAME del lado del servidor.
Sql "master" "DECLARE @b sysname = N'$Base'; IF DB_ID(@b) IS NOT NULL BEGIN DECLARE @q nvarchar(300) = QUOTENAME(@b);
    EXEC (N'ALTER DATABASE ' + @q + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + @q + N';'); END" | Out-Null
$esquema = [IO.File]::ReadAllText((Join-Path $repo "database\esquema\ClubCamioneros_unificada_crear_base.sql"), [Text.Encoding]::UTF8)
$esquemaBase = Join-Path $Salida "esquema_$Base.sql"
[IO.File]::WriteAllText($esquemaBase, $esquema.Replace("ClubCamionerosUnificadaV2", $Base), [Text.UTF8Encoding]::new($false))
Sql "master" -Archivo $esquemaBase | Out-Null
# La marca: la próxima vez, este script sabe que la base es suya y la puede recrear.
Sql $Base "EXEC sys.sp_addextendedproperty @name = N'$MarcaDePrueba', @value = N'reset_base';" | Out-Null
Write-Host "  Esquema unificado creado en $Base."

# ---- 2. migraciones con el migrador de la API (sale sin levantar la API). JWT y credencial al azar: solo para que arranque.
$cadena = "Server=$Servidor;Database=$Base;Trusted_Connection=True;TrustServerCertificate=True;"
$info = New-Object Diagnostics.ProcessStartInfo "dotnet", "`"$AdministrativoDll`" --DatabaseMigrations:SoloMigrar=true"
$info.WorkingDirectory = Split-Path $AdministrativoDll
$info.UseShellExecute = $false; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
$info.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Development"
$info.EnvironmentVariables["ConnectionStrings__ConexionMigraciones"] = $cadena
$info.EnvironmentVariables["ConnectionStrings__ConexionSQL"] = $cadena
$info.EnvironmentVariables["Jwt__Key"] = (Azar); $info.EnvironmentVariables["Jwt__Issuer"] = "CACC-HU059"; $info.EnvironmentVariables["Jwt__Audience"] = "CACC-HU059"
$info.EnvironmentVariables["Integracion__ClienteDeportivo__Clave"] = (Azar 32)
$p = [Diagnostics.Process]::Start($info)
$salidaMigrador = $p.StandardOutput.ReadToEndAsync(); $errorMigrador = $p.StandardError.ReadToEndAsync()
if (-not $p.WaitForExit(180000)) { $p.Kill(); throw "El migrador no terminó en 180 s. Ver $log" }
"`n== migrador -> $($p.ExitCode)`n$($salidaMigrador.Result)`n$($errorMigrador.Result)" | Add-Content $log
if ($p.ExitCode -ne 0) { throw "El migrador terminó con código $($p.ExitCode). Ver $log" }
$archivos = @(Get-ChildItem (Join-Path $repo "database\migrations") -Filter "V*.sql").Name
$aplicadas = (Sql $Base "SELECT Nombre FROM dbo.__CaccMigraciones") -split "`r?`n" | ForEach-Object { $_.Trim() }
$faltan = @($archivos | Where-Object { $_ -notin $aplicadas })
if ($faltan.Count -gt 0) { throw "Quedaron migraciones sin aplicar: $($faltan -join ', '). Ver $log" }
Write-Host "  Migraciones al día ($($archivos.Count) de database/migrations aplicadas)."

# ---- 3. datos de prueba
Sql $Base -Archivo (Join-Path $PSScriptRoot "datos\01_cuentas_prueba.sql") | Out-Null
$fixtures = Sql $Base -Archivo (Join-Path $PSScriptRoot "datos\02_fixtures_e2e.sql")
Write-Host "  Cuentas ficticias y $($fixtures -replace '\s+', ' ')"

# ---- 4. verificación del estado inicial
$estado = Sql $Base @"
SELECT CONCAT((SELECT COUNT(*) FROM USUARIOS), '|', (SELECT COUNT(*) FROM JUGADORES), '|', (SELECT COUNT(*) FROM PAGOS), '|',
    (SELECT COUNT(*) FROM JUGADORES WHERE fecha_baja IS NOT NULL), '|', CASE WHEN COL_LENGTH('dbo.AUDITORIA_CAMBIOS', 'origen_sistema') IS NULL THEN 0 ELSE 1 END, '|',
    CASE WHEN OBJECT_ID('dbo.SEQ_JUGADORES') IS NOT NULL AND OBJECT_ID('dbo.TR_JUGADORES_PERIODOS') IS NOT NULL
               AND EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.JUGADORES') AND name = 'UX_JUGADORES_persona')
         THEN 1 ELSE 0 END)
"@
if ($estado -ne "5|4|5|0|1|1") { throw "El estado inicial no es el esperado (usuarios|jugadores|pagos|bajas|origen|objetos = $estado; se esperaba 5|4|5|0|1|1). Ver $log" }
Write-Host "  Estado inicial verificado ($estado) en $([int]((Get-Date) - $inicio).TotalSeconds) s." -ForegroundColor Green
