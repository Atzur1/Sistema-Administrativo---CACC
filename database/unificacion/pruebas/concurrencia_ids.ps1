<#
.SYNOPSIS
    HU-053: prueba de concurrencia de identificadores en la base unificada (sesiones reales de SQL Server en paralelo).

.DESCRIPTION
    Crea una base de prueba vacia con esquema/ClubCamioneros_unificada_crear_base.sql y lanza N procesos sqlcmd que
    arrancan a la vez (esperan una "largada" comun) y repiten K altas cada uno:
      Fase 1 - mecanismo acordado: INSERT sin id en PERSONAS, JUGADORES (SEQ_JUGADORES), PAGOS (SEQ_PAGOS) y en las 6
               tablas donde el Portal Deportivo calcula MAX+1 (USUARIOS, STAFF, CATEGORIAS, STAFF_CATEGORIAS,
               RESPONSABLES, JUGADORES_RESPONSABLES), leyendo el id generado con OUTPUT ... INTO. Se espera 0 errores.
      Fase 2 - patron actual del Portal Deportivo (con parches/ellos-definitivo.patch): SELECT MAX(id) + 1 y despues
               INSERT con SET IDENTITY_INSERT ON, en dos comandos (como JugadorDao). La pausa entre los dos comandos
               representa la ida y vuelta de red entre ExecuteScalar y ExecuteNonQuery. Se espera que choque.
    Al final borra la base de prueba (salvo -ConservarBase).
#>
[CmdletBinding()]
param(
    [string]$Servidor = "(localdb)\MSSQLLocalDB",
    [string]$Base = "ClubCamionerosUnificadaV2_HU053_Concurrencia",
    [int]$Sesiones = 8,
    [int]$Iteraciones = 50,
    [string]$Salida = (Join-Path $env:TEMP "cacc-hu053"),
    [switch]$ConservarBase
)

$ErrorActionPreference = "Stop"
if ($Base -notmatch '(?i)_hu053') { throw "La base '$Base' se borra y se crea: tiene que tener '_HU053' en el nombre." }
$database = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
New-Item -ItemType Directory -Force -Path $Salida | Out-Null

function Sql([string]$Db, [string]$Consulta) {
    $texto = & sqlcmd -S $Servidor -E -C -b -I -W -h -1 -s "|" -d $Db -Q "SET NOCOUNT ON; $Consulta" 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "Fallo la consulta en ${Db}: $texto" }
    $texto.Trim()
}

Sql master "IF DB_ID(N'$Base') IS NOT NULL BEGIN ALTER DATABASE [$Base] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Base]; END" | Out-Null
$esquema = [IO.File]::ReadAllText((Join-Path $database "esquema\ClubCamioneros_unificada_crear_base.sql"), [Text.Encoding]::UTF8)
$archivoEsquema = Join-Path $Salida "esquema_$Base.sql"
[IO.File]::WriteAllText($archivoEsquema, $esquema.Replace("ClubCamionerosUnificadaV2", $Base), [Text.UTF8Encoding]::new($false))
& sqlcmd -S $Servidor -E -C -b -I -i $archivoEsquema | Out-Null
if ($LASTEXITCODE -ne 0) { throw "No se pudo crear la base $Base" }

Sql $Base "CREATE TABLE dbo.PRUEBA_LARGADA (fase int NOT NULL PRIMARY KEY);
           CREATE TABLE dbo.PRUEBA_RESULTADO (fase int NOT NULL, sesion int NOT NULL, intentos int NOT NULL, errores int NOT NULL,
                                              ultimo_error nvarchar(400) NULL, PRIMARY KEY (fase, sesion));" | Out-Null

$seguro = @'
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @s int = $(Sesion), @k int = $(Iteraciones), @i int = 0, @errores int = 0, @ultimo nvarchar(400);
DECLARE @id TABLE (tabla sysname, id int);
DECLARE @persona int, @jugador int, @usuario int, @staff int, @categoria int, @responsable int;
WHILE NOT EXISTS (SELECT 1 FROM dbo.PRUEBA_LARGADA WHERE fase = 1) WAITFOR DELAY '00:00:00.010';
WHILE @i < @k
BEGIN
    BEGIN TRY
        DELETE @id;
        BEGIN TRANSACTION;
        INSERT dbo.PERSONAS (genero, dni, nombre, apellido) OUTPUT N'PERSONAS', INSERTED.PK_id_persona INTO @id
        VALUES ('Masculino', CONVERT(varchar(9), 700000000 + @s * 100000 + @i), 'PRUEBA', CONCAT('CONCURRENCIA ', @s));
        SET @persona = (SELECT id FROM @id WHERE tabla = N'PERSONAS');
        INSERT dbo.JUGADORES (FK_id_persona, FK_id_categoria) OUTPUT N'JUGADORES', INSERTED.PK_id_jugador INTO @id VALUES (@persona, 1);
        SET @jugador = (SELECT id FROM @id WHERE tabla = N'JUGADORES');
        INSERT dbo.PAGOS (FK_id_jugador, monto_base, monto_final, fecha_pago, estado, concepto) OUTPUT N'PAGOS', INSERTED.PK_id_pago INTO @id
        VALUES (@jugador, 1, 1, '20261001', 1, 'Cuota');
        INSERT dbo.USUARIOS (FK_id_persona, FK_id_rol, activo) OUTPUT N'USUARIOS', INSERTED.PK_id_usuario INTO @id VALUES (@persona, 4, 1);
        SET @usuario = (SELECT id FROM @id WHERE tabla = N'USUARIOS');
        INSERT dbo.STAFF (FK_id_usuario) OUTPUT N'STAFF', INSERTED.PK_id_staff INTO @id VALUES (@usuario);
        SET @staff = (SELECT id FROM @id WHERE tabla = N'STAFF');
        INSERT dbo.CATEGORIAS (nombre_categoria, asociacion) OUTPUT N'CATEGORIAS', INSERTED.PK_id_categoria INTO @id VALUES (CONCAT('PRUEBA ', @s, '-', @i), 'AFA');
        SET @categoria = (SELECT id FROM @id WHERE tabla = N'CATEGORIAS');
        INSERT dbo.STAFF_CATEGORIAS (FK_id_staff, FK_id_categoria) OUTPUT N'STAFF_CATEGORIAS', INSERTED.PK_id_staff_categoria INTO @id VALUES (@staff, @categoria);
        INSERT dbo.RESPONSABLES (FK_id_persona, telefono) OUTPUT N'RESPONSABLES', INSERTED.PK_id_responsable INTO @id VALUES (@persona, '0');
        SET @responsable = (SELECT id FROM @id WHERE tabla = N'RESPONSABLES');
        INSERT dbo.JUGADORES_RESPONSABLES (FK_id_responsable, FK_id_jugador, parentesco) OUTPUT N'JUGADORES_RESPONSABLES', INSERTED.PK_id_jugador_responsable INTO @id
        VALUES (@responsable, @jugador, 'PRUEBA');
        IF EXISTS (SELECT 1 FROM @id WHERE id IS NULL) THROW 50001, N'Un INSERT no devolvio su id.', 1;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @errores += 1; SET @ultimo = LEFT(ERROR_MESSAGE(), 400);
    END CATCH;
    SET @i += 1;
END;
INSERT dbo.PRUEBA_RESULTADO (fase, sesion, intentos, errores, ultimo_error) VALUES (1, @s, @k, @errores, @ultimo);
'@

$maxMasUno = @'
SET NOCOUNT ON;
DECLARE @s int = $(Sesion), @k int = $(Iteraciones), @i int = 0, @errores int = 0, @ultimo nvarchar(400), @nuevo int;
WHILE NOT EXISTS (SELECT 1 FROM dbo.PRUEBA_LARGADA WHERE fase = 2) WAITFOR DELAY '00:00:00.010';
WHILE @i < @k
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;
        SET @nuevo = (SELECT ISNULL(MAX(PK_id_responsable), 0) + 1 FROM dbo.RESPONSABLES);
        WAITFOR DELAY '00:00:00.002';
        SET IDENTITY_INSERT dbo.RESPONSABLES ON;
        INSERT dbo.RESPONSABLES (PK_id_responsable, FK_id_persona, telefono) VALUES (@nuevo, NULL, 'MAX+1');
        SET IDENTITY_INSERT dbo.RESPONSABLES OFF;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET IDENTITY_INSERT dbo.RESPONSABLES OFF;
        SET @errores += 1; SET @ultimo = LEFT(ERROR_MESSAGE(), 400);
    END CATCH;
    SET @i += 1;
END;
INSERT dbo.PRUEBA_RESULTADO (fase, sesion, intentos, errores, ultimo_error) VALUES (2, @s, @k, @errores, @ultimo);
'@

function Correr-Fase([int]$Fase, [string]$Plantilla) {
    $archivo = Join-Path $Salida "concurrencia_fase$Fase.sql"
    [IO.File]::WriteAllText($archivo, $Plantilla, [Text.UTF8Encoding]::new($false))
    $procesos = 1..$Sesiones | ForEach-Object {
        Start-Process -FilePath "sqlcmd" -NoNewWindow -PassThru `
            -RedirectStandardOutput (Join-Path $Salida "concurrencia_f${Fase}_s$_.txt") `
            -ArgumentList "-S `"$Servidor`" -E -C -b -I -d $Base -v Sesion=$_ Iteraciones=$Iteraciones -i `"$archivo`""
    }
    Start-Sleep -Milliseconds 1500   # todas las sesiones conectadas y esperando la largada
    Sql $Base "INSERT dbo.PRUEBA_LARGADA (fase) VALUES ($Fase);" | Out-Null
    $procesos | Wait-Process -Timeout 600
}

$personasAntes = [int](Sql $Base "SELECT COUNT(*) FROM dbo.PERSONAS")
Correr-Fase 1 $seguro
$esperadas = $Sesiones * $Iteraciones
$fase1 = Sql $Base "SELECT CONCAT(COUNT(*), ' sesiones, ', SUM(intentos), ' altas, ', SUM(errores), ' errores', ISNULL(' | ' + MAX(ultimo_error), '')) FROM dbo.PRUEBA_RESULTADO WHERE fase = 1"
$conteos = Sql $Base @"
SELECT CONCAT_WS(' ',
 'PERSONAS', (SELECT COUNT(*) FROM dbo.PERSONAS WHERE nombre = 'PRUEBA'),
 'JUGADORES', (SELECT COUNT(*) FROM dbo.JUGADORES), 'PAGOS', (SELECT COUNT(*) FROM dbo.PAGOS),
 'USUARIOS', (SELECT COUNT(*) FROM dbo.USUARIOS), 'STAFF', (SELECT COUNT(*) FROM dbo.STAFF),
 'CATEGORIAS', (SELECT COUNT(*) FROM dbo.CATEGORIAS WHERE nombre_categoria LIKE 'PRUEBA %'),
 'STAFF_CATEGORIAS', (SELECT COUNT(*) FROM dbo.STAFF_CATEGORIAS), 'RESPONSABLES', (SELECT COUNT(*) FROM dbo.RESPONSABLES),
 'JUGADORES_RESPONSABLES', (SELECT COUNT(*) FROM dbo.JUGADORES_RESPONSABLES))
"@
$distintos = Sql $Base @"
SELECT CONCAT_WS(' ',
 (SELECT COUNT(DISTINCT PK_id_jugador) FROM dbo.JUGADORES), (SELECT COUNT(DISTINCT PK_id_pago) FROM dbo.PAGOS),
 (SELECT COUNT(DISTINCT PK_id_usuario) FROM dbo.USUARIOS), (SELECT COUNT(DISTINCT PK_id_staff) FROM dbo.STAFF),
 (SELECT COUNT(DISTINCT PK_id_responsable) FROM dbo.RESPONSABLES), (SELECT COUNT(DISTINCT PK_id_jugador_responsable) FROM dbo.JUGADORES_RESPONSABLES),
 (SELECT COUNT(DISTINCT PK_id_staff_categoria) FROM dbo.STAFF_CATEGORIAS), (SELECT COUNT(DISTINCT PK_id_categoria) FROM dbo.CATEGORIAS WHERE nombre_categoria LIKE 'PRUEBA %'))
"@
$todasIguales = ($distintos.Split(' ') | Where-Object { [int]$_ -ne $esperadas }).Count -eq 0
$erroresFase1 = [int](Sql $Base "SELECT SUM(errores) FROM dbo.PRUEBA_RESULTADO WHERE fase = 1")

$responsablesAntes = [int](Sql $Base "SELECT COUNT(*) FROM dbo.RESPONSABLES")
Correr-Fase 2 $maxMasUno
$fase2 = Sql $Base "SELECT CONCAT(COUNT(*), ' sesiones, ', SUM(intentos), ' intentos, ', SUM(errores), ' errores', ISNULL(' | ' + MAX(ultimo_error), '')) FROM dbo.PRUEBA_RESULTADO WHERE fase = 2"
$erroresFase2 = [int](Sql $Base "SELECT SUM(errores) FROM dbo.PRUEBA_RESULTADO WHERE fase = 2")
$altasFase2 = [int](Sql $Base "SELECT COUNT(*) FROM dbo.RESPONSABLES") - $responsablesAntes

if (-not $ConservarBase) { Sql master "ALTER DATABASE [$Base] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Base];" | Out-Null }

$resultadoFase1 = if ($erroresFase1 -eq 0 -and $todasIguales) { "PASS" } else { "FAIL" }
[pscustomobject]@{
    Sesiones = $Sesiones; IteracionesPorSesion = $Iteraciones; AltasEsperadasPorTabla = $esperadas
    Fase1_IdentitySequence = $fase1
    Fase1_Conteos = $conteos
    Fase1_IdsDistintosPorTabla = $distintos
    Fase1_Resultado = $resultadoFase1
    Fase2_MaxMasUno = $fase2
    Fase2_AltasLogradas = "$altasFase2 de $esperadas"
    Fase2_Conclusion = if ($erroresFase2 -gt 0) { "MAX+1 choca bajo concurrencia ($erroresFase2 altas rechazadas por PK duplicada)" } else { "Sin choques en esta corrida (no prueba que MAX+1 sea seguro)" }
} | Format-List
if ($resultadoFase1 -ne "PASS") { exit 1 }
