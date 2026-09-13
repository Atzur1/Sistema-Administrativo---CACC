-- HU-016 - Procesamiento transaccional del cobro y cambio de estado de la cuota a "Abonada".
-- Base: ClubCamionerosPRUEBA (SQL Server)
--
-- Migración de esquema. Motivos:
--   1. PAGOS no registra qué mes cubre cada cobro: fecha_pago indica cuándo se
--      cobró, no qué cuota se pagó. Sin ese dato no se puede calcular la
--      morosidad ni impedir que una misma cuota se cobre dos veces.
--   2. Se agrega la columna periodo (DATE), que guarda el primer día del mes
--      que se paga (por ejemplo 2026-07-01 para julio de 2026).
--   3. Índice único filtrado por jugador y período: bloqueo de re-cobro de
--      HU-016 garantizado a nivel de datos, no solo en la interfaz.
--   4. El período se incorpora al trigger de inmutabilidad creado en
--      V20260913_01: un cobro registrado no puede cambiar de mes.
--
-- Los pagos históricos quedan con periodo en NULL porque ese dato no se
-- registraba. El índice filtrado los excluye, así que no generan conflictos.
-- Este script no inserta ni modifica datos.
--
-- Ejecutar según el procedimiento de la V.008 del Plan de Gestión de la
-- Configuración: backup previo verificado, sqlcmd con -b e -I, y verificación
-- posterior. El índice filtrado requiere QUOTED_IDENTIFIER y ANSI_NULLS en ON.

USE ClubCamionerosPRUEBA;
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

-- 1. Columna del período cubierto por el cobro
ALTER TABLE PAGOS ADD periodo DATE NULL;

-- Las sentencias que usan la columna nueva se ejecutan con EXEC: dentro del
-- mismo lote la columna todavía no existe cuando SQL Server lo compila.

-- 2. El período siempre es el primer día del mes
EXEC('
ALTER TABLE PAGOS ADD CONSTRAINT CK_PAGOS_periodo
    CHECK (periodo IS NULL OR DAY(periodo) = 1);
');

-- 3. Una cuota por jugador y período
EXEC('
CREATE UNIQUE INDEX UX_PAGOS_jugador_periodo
    ON PAGOS (FK_id_jugador, periodo)
    WHERE periodo IS NOT NULL;
');

-- 4. El período pasa a formar parte de los datos inmutables del cobro
EXEC('
ALTER TRIGGER TR_PAGOS_bloquear_modificacion_cobro
ON PAGOS
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT i.PK_id_pago, i.metodo_pago COLLATE Latin1_General_CS_AS,
               i.referencia_pago, i.FK_id_usuario_registro, i.fecha_registro,
               i.periodo
        FROM inserted i
        EXCEPT
        SELECT d.PK_id_pago, d.metodo_pago COLLATE Latin1_General_CS_AS,
               d.referencia_pago, d.FK_id_usuario_registro, d.fecha_registro,
               d.periodo
        FROM deleted d
    )
        THROW 51000, ''El método de pago, el período y los datos de registro de un cobro no se pueden modificar.'', 1;
END
');

COMMIT TRANSACTION;
GO

-- Verificación posterior (solo lectura)
SELECT COUNT(*) AS filas, SUM(CASE WHEN periodo IS NULL THEN 1 ELSE 0 END) AS sin_periodo
FROM PAGOS;
