<#
.SYNOPSIS
    HU-053: prueba de V20260929_07 (original vs protegido) sobre COPIAS de bases reales. No toca las bases de origen.

.DESCRIPTION
    Restaura copias (COPY_ONLY) de la base del Portal Administrativo (A) y de una base que ya mezcla usuarios de los dos
    sistemas con los nombres de A (CCACUnificada). En cada copia deja al SuperAdmin como estaba antes de V07
    (acceso_portal = 0, rol_portal = NULL) y ejecuta:
      - el V20260929_07 ORIGINAL (tomado de git, commit HEAD) sobre la copia mixta: muestra a quien habilitaria;
      - el V20260929_07 PROTEGIDO (el del arbol de trabajo) sobre la copia de A, sobre la copia mixta, una segunda vez
        sobre A (repeticion) y sobre la base unificada de prueba (que no debe cambiar).
    Antes de cada ejecucion cuenta, con el mismo criterio del UPDATE original, cuantas filas afectaria.
    Al final borra las copias.
#>
[CmdletBinding()]
param(
    [string]$Servidor = "(localdb)\MSSQLLocalDB",
    [string]$BaseA = "ClubCamionerosPRUEBA",
    [string]$BaseMixta = "CCACUnificada",
    [string]$BaseUnificada = "ClubCamionerosUnificadaV2_HU053",
    [string]$Salida = (Join-Path $env:TEMP "cacc-hu053")
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$script = "database/V20260929_07__acceso_administrador_rol_portal.sql"
New-Item -ItemType Directory -Force -Path $Salida | Out-Null

function Sql([string]$Base, [string]$Consulta) {
    $texto = & sqlcmd -S $Servidor -E -C -b -I -W -h -1 -s "|" -d $Base -Q "SET NOCOUNT ON; $Consulta" 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "Fallo la consulta en ${Base}: $texto" }
    $texto.Trim()
}

function Ejecutar-V07([string]$Base, [string]$Version, [string]$Contenido) {
    $archivo = Join-Path $Salida "V07_${Version}_$Base.sql"
    [IO.File]::WriteAllText($archivo, $Contenido.Replace("USE ClubCamionerosPruebaCuatro;", "USE [$Base];"), [Text.UTF8Encoding]::new($false))
    $texto = & sqlcmd -S $Servidor -E -C -b -I -W -d $Base -i $archivo 2>&1 | Out-String
    [pscustomobject]@{ Codigo = $LASTEXITCODE; Texto = $texto.Trim() }
}

function Restaurar-Copia([string]$Origen, [string]$Copia) {
    $bak = Join-Path $Salida "$Origen`_v07.bak"
    Sql master "BACKUP DATABASE [$Origen] TO DISK = N'$bak' WITH COPY_ONLY, INIT, FORMAT, CHECKSUM;" | Out-Null
    $carpetaDatos = Sql master "SELECT CONVERT(nvarchar(400), SERVERPROPERTY('InstanceDefaultDataPath'));"
    $archivos = (& sqlcmd -S $Servidor -E -C -b -W -h -1 -s "|" -Q "SET NOCOUNT ON; RESTORE FILELISTONLY FROM DISK = N'$bak';" 2>&1) |
        Where-Object { $_ -match '\|' } | ForEach-Object { $c = $_.Split('|'); [pscustomobject]@{ Logico = $c[0]; Tipo = $c[2] } }
    $mover = ($archivos | ForEach-Object {
        $extension = if ($_.Tipo -eq 'L') { 'ldf' } else { 'mdf' }
        "MOVE N'$($_.Logico)' TO N'$carpetaDatos$Copia`_$($_.Logico).$extension'" }) -join ", "
    Sql master "IF DB_ID(N'$Copia') IS NOT NULL BEGIN ALTER DATABASE [$Copia] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Copia]; END;
                RESTORE DATABASE [$Copia] FROM DISK = N'$bak' WITH $mover, RECOVERY;" | Out-Null
}

$criterioOriginal = "SELECT COUNT(*) FROM dbo.USUARIO WHERE FK_id_rol = 1 AND activo = 1 AND acceso_portal = 0"
$habilitados = "SELECT STRING_AGG(CONVERT(varchar(10), PK_id_usuario), ',') WITHIN GROUP (ORDER BY PK_id_usuario) FROM dbo.USUARIO WHERE acceso_portal = 1"
$antesDeV07 = "UPDATE dbo.USUARIO SET acceso_portal = 0, rol_portal = NULL WHERE PK_id_usuario = 1;"

$original = (& git -C $repo show "HEAD:$script") -join "`n"
$protegido = [IO.File]::ReadAllText((Join-Path $repo $script), [Text.Encoding]::UTF8)
$resultados = @()

Restaurar-Copia $BaseA "HU053_V07_A_Prueba"
Restaurar-Copia $BaseMixta "HU053_V07_Mixta_Prueba"
Restaurar-Copia $BaseMixta "HU053_V07_MixtaOriginal_Prueba"
foreach ($copia in @("HU053_V07_A_Prueba", "HU053_V07_Mixta_Prueba", "HU053_V07_MixtaOriginal_Prueba")) { Sql $copia $antesDeV07 | Out-Null }

$casos = @(
    @{ Caso = "ORIGINAL sobre base mixta A+B"; Base = "HU053_V07_MixtaOriginal_Prueba"; Version = "original"; Sql = $original; Esperadas = "1 (solo el SuperAdmin de A)" },
    @{ Caso = "PROTEGIDO sobre base de A"; Base = "HU053_V07_A_Prueba"; Version = "protegido"; Sql = $protegido; Esperadas = "1 (SuperAdmin de A)" },
    @{ Caso = "PROTEGIDO repetido sobre base de A"; Base = "HU053_V07_A_Prueba"; Version = "protegido"; Sql = $protegido; Esperadas = "0" },
    @{ Caso = "PROTEGIDO sobre base mixta A+B"; Base = "HU053_V07_Mixta_Prueba"; Version = "protegido"; Sql = $protegido; Esperadas = "1 (solo el SuperAdmin de A)" }
)
foreach ($caso in $casos) {
    $previstas = Sql $caso.Base $criterioOriginal
    $antes = Sql $caso.Base $habilitados
    $ejecucion = Ejecutar-V07 $caso.Base $caso.Version $caso.Sql
    $despues = Sql $caso.Base $habilitados
    $resultados += [pscustomobject]@{
        Caso = $caso.Caso; Esperadas = $caso.Esperadas; CriterioOriginalContaria = $previstas
        HabilitadosAntes = $antes; HabilitadosDespues = $despues; Salida = $ejecucion.Codigo
        Mensaje = (($ejecucion.Texto -split "`r?`n") | Where-Object { $_ -match 'V20260929_07|Msg' }) -join ' / ' }
}

# El trigger que crea la version protegida (SQL dinamico) es el mismo que crea la original.
# Hash del cuerpo desde el nombre del trigger (SQL Server guarda CREATE OR ALTER como CREATE), sin espacios ni saltos de linea (sqlcmd trunca columnas largas: se compara en el servidor).
$definicion = "DECLARE @d nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(N'dbo.TR_AUDIT_USUARIO'));
SET @d = SUBSTRING(@d, NULLIF(CHARINDEX(N'TRIGGER dbo.TR_AUDIT_USUARIO', @d), 0), LEN(@d));
SELECT CONVERT(varchar(64), HASHBYTES('SHA2_256', REPLACE(REPLACE(REPLACE(REPLACE(@d, NCHAR(13), N''), NCHAR(10), N''), NCHAR(9), N''), N' ', N'')), 2);"
$triggerOriginal = Sql "HU053_V07_MixtaOriginal_Prueba" $definicion
$triggerProtegido = Sql "HU053_V07_A_Prueba" $definicion
if ($triggerOriginal -eq "NULL" -or -not $triggerOriginal) { throw "No se pudo leer TR_AUDIT_USUARIO de la copia con el script original." }
$resultados += [pscustomobject]@{
    Caso = "TR_AUDIT_USUARIO creado por la version protegida = el de la original"; Esperadas = "True"; CriterioOriginalContaria = ""
    HabilitadosAntes = ""; HabilitadosDespues = ""; Salida = ($triggerOriginal -eq $triggerProtegido); Mensaje = "" }

# Base unificada: el script no debe cambiar nada (ni datos ni objetos).
$huella = "SELECT CONCAT((SELECT COUNT(*) FROM dbo.USUARIOS WHERE acceso_portal = 1), '/', (SELECT CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM dbo.USUARIOS), '/', (SELECT COUNT(*) FROM sys.objects WHERE type = 'TR'), '/', COL_LENGTH('dbo.USUARIO', 'acceso_portal'))"
$antes = Sql $BaseUnificada $huella
$ejecucion = Ejecutar-V07 $BaseUnificada "protegido" $protegido
$despues = Sql $BaseUnificada $huella
$resultados += [pscustomobject]@{
    Caso = "PROTEGIDO sobre base unificada (USUARIOS)"; Esperadas = "sin cambios"; CriterioOriginalContaria = "n/a (no hay dbo.USUARIO)"
    HabilitadosAntes = $antes; HabilitadosDespues = $despues; Salida = $ejecucion.Codigo
    Mensaje = (($ejecucion.Texto -split "`r?`n") | Where-Object { $_ -match 'V20260929_07|Msg' }) -join ' / ' }

foreach ($copia in @("HU053_V07_A_Prueba", "HU053_V07_Mixta_Prueba", "HU053_V07_MixtaOriginal_Prueba")) {
    Sql master "ALTER DATABASE [$copia] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$copia];" | Out-Null
}

$resultados | Format-List
