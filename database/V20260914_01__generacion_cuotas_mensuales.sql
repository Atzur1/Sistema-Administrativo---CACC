-- HU-009 - Generación masiva de cuotas mensuales para jugadores en el mes vigente.
-- Base: ClubCamionerosPRUEBA (SQL Server)
--
-- Migración de esquema + procedimiento almacenado. Motivos:
--   1. TARIFAS no existe en la base. Se crea con vigencia por categoría
--      (fecha_inicio / fecha_fin, igual que TIPO_DESCUENTO) porque CATEGORIAS
--      ya distingue rama: de las 13 categorías, "Femenino" es una más, no un
--      atributo aparte. fecha_fin NULL significa "vigente sin fin programado".
--   2. JUGADORES no tiene forma de marcar una baja. Se agrega la columna
--      activo (BIT, default 1) para poder excluir jugadores dados de baja de
--      la generación masiva más adelante; hoy las 577 filas existentes quedan
--      todas en 1 porque no hay mecanismo de baja implementado.
--   3. PAGOS.metodo_pago es NOT NULL: una cuota recién generada no tiene
--      método de pago todavía (se define al cobrarla, HU-015/HU-016), así que
--      la columna pasa a admitir NULL. El CHECK CK_PAGOS_metodo_pago ya acepta
--      NULL (una comparación IN con NULL da UNKNOWN, no FALSE); solo la
--      restricción NOT NULL bloqueaba el INSERT.
--   4. sp_GenerateMonthlyFees genera, para cada jugador activo, una cuota
--      'Impaga' (estado = 0) del período pedido (o el mes/año vigente por
--      defecto), congelando el monto de la tarifa vigente de su categoría y
--      el descuento activo que tuviera. La idempotencia la garantiza el
--      índice único UX_PAGOS_jugador_periodo (V20260913_02): el SP además
--      filtra duplicados explícitamente para poder informar cuántos se
--      omitieron sin depender de que el índice tire un error.
--
-- Ejecutar según el procedimiento de la V.008 del Plan de Gestión de la
-- Configuración: backup previo verificado, sqlcmd con -b e -I.

USE ClubCamionerosPRUEBA;
GO

-- sp_GenerateMonthlyFees inserta en PAGOS, que tiene el índice único filtrado
-- UX_PAGOS_jugador_periodo (V20260913_02): un procedimiento creado sin estas
-- dos opciones en ON falla en tiempo de ejecución con el error 1934 al hacer
-- ese INSERT, porque SQL Server graba QUOTED_IDENTIFIER/ANSI_NULLS del momento
-- de creación junto con el procedimiento.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

-- 1. Tabla de aranceles por categoría, con vigencia
CREATE TABLE TARIFAS
(
    PK_id_tarifa    INT IDENTITY(1,1) NOT NULL,
    FK_id_categoria INT NOT NULL,
    monto           DECIMAL(18,2) NOT NULL,
    fecha_inicio    DATE NOT NULL,
    fecha_fin       DATE NULL,
    CONSTRAINT PK_TARIFAS PRIMARY KEY (PK_id_tarifa),
    CONSTRAINT FK_TARIFAS_CATEGORIA FOREIGN KEY (FK_id_categoria)
        REFERENCES CATEGORIAS (PK_id_categoria),
    CONSTRAINT CK_TARIFAS_monto CHECK (monto > 0),
    CONSTRAINT CK_TARIFAS_fechas CHECK (fecha_fin IS NULL OR fecha_fin >= fecha_inicio)
);

-- Índice de apoyo: el SP resuelve la tarifa vigente de cada jugador por su
-- categoría, filtrando por fecha, para las 577 filas de JUGADORES.
CREATE INDEX IX_TARIFAS_categoria_vigencia
    ON TARIFAS (FK_id_categoria, fecha_inicio, fecha_fin);

-- 2. Marca de baja para JUGADORES (no existía ninguna hasta ahora)
ALTER TABLE JUGADORES ADD activo BIT NOT NULL
    CONSTRAINT DF_JUGADORES_activo DEFAULT 1 WITH VALUES;

-- 3. Una cuota recién generada no tiene método de pago hasta que se cobra
ALTER TABLE PAGOS ALTER COLUMN metodo_pago VARCHAR(50) NULL;

-- 4. Procedimiento de generación masiva. CREATE OR ALTER debe ser la primera
--    sentencia de su lote, por eso se ejecuta con EXEC dentro de la misma
--    transacción que las sentencias anteriores.
EXEC('
CREATE OR ALTER PROCEDURE sp_GenerateMonthlyFees
    @mes                INT = NULL,
    @anio               INT = NULL,
    @id_usuario_admin   INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @mes  = ISNULL(@mes, MONTH(GETDATE()));
    SET @anio = ISNULL(@anio, YEAR(GETDATE()));

    IF @mes NOT BETWEEN 1 AND 12
        THROW 51002, ''El mes debe estar entre 1 y 12.'', 1;

    DECLARE @periodo DATE = DATEFROMPARTS(@anio, @mes, 1);
    DECLARE @hoy DATE = CAST(GETDATE() AS DATE);
    DECLARE @generados INT;
    DECLARE @elegibles INT;

    BEGIN TRANSACTION;

    BEGIN TRY
        ;WITH JugadoresElegibles AS (
            SELECT
                j.PK_id_jugador AS id_jugador,
                t.monto AS monto_base,
                jd.id_jugador_descuento,
                CASE WHEN jd.porcentaje IS NOT NULL
                     THEN ROUND(t.monto - (t.monto * jd.porcentaje / 100.0), 2)
                     ELSE t.monto
                END AS monto_final
            FROM JUGADORES j
                OUTER APPLY (
                    SELECT TOP 1 tt.monto
                    FROM TARIFAS tt
                    WHERE tt.FK_id_categoria = j.FK_id_categoria
                      AND tt.fecha_inicio <= @hoy
                      AND (tt.fecha_fin IS NULL OR tt.fecha_fin >= @hoy)
                    ORDER BY tt.fecha_inicio DESC
                ) t
                OUTER APPLY (
                    SELECT TOP 1
                        jjd.PK_id_jugador_descuento AS id_jugador_descuento,
                        ttd.porcentaje AS porcentaje
                    FROM JUGADORES_DESCUENTOS jjd
                        INNER JOIN TIPO_DESCUENTO ttd ON ttd.PK_id_descuento = jjd.FK_id_descuento
                    WHERE jjd.FK_id_jugador = j.PK_id_jugador
                      AND jjd.estado_activo = 1
                      AND @hoy BETWEEN ttd.fecha_inicio AND ttd.fecha_fin
                    ORDER BY ttd.fecha_inicio DESC
                ) jd
            WHERE j.activo = 1
        )
        INSERT INTO PAGOS
            (FK_id_jugador, monto_base, FK_id_jugador_descuento, monto_final,
             fecha_pago, metodo_pago, fecha_vencimiento, estado,
             FK_id_usuario_registro, fecha_registro, periodo)
        SELECT
            e.id_jugador, e.monto_base, e.id_jugador_descuento, e.monto_final,
            NULL, NULL, NULL, 0,
            @id_usuario_admin, SYSDATETIME(), @periodo
        FROM JugadoresElegibles e
        WHERE e.monto_base IS NOT NULL
          AND NOT EXISTS (
              SELECT 1 FROM PAGOS p
              WHERE p.FK_id_jugador = e.id_jugador AND p.periodo = @periodo
          );

        SET @generados = @@ROWCOUNT;
        SET @elegibles = (SELECT COUNT(*) FROM JUGADORES WHERE activo = 1);

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH

    SELECT @generados AS TotalGenerated, (@elegibles - @generados) AS TotalSkipped;
END
');

COMMIT TRANSACTION;
GO

-- Verificación posterior (solo lectura)
SELECT COUNT(*) AS tarifas, (SELECT COUNT(*) FROM JUGADORES WHERE activo = 1) AS jugadores_activos
FROM TARIFAS;
