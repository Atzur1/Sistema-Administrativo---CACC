-- HU-033 — Inscripción única para jugadores del plantel masculino.
--
-- La inscripción es un cargo aparte de la cuota mensual: tiene su propio valor versionado
-- por vigencia (como ARANCELES) y genera su propio registro en PAGOS, que acepta abonos
-- parciales igual que una cuota. Se guarda en una tabla propia y no como un género más de
-- ARANCELES porque ArancelesDao.ObtenerMontoVigente, el historial y el próximo cambio leen
-- esa tabla completa: una fila de inscripción ahí se tomaría como valor de cuota.
--
-- Idempotente: cada bloque verifica antes de crear, así el script se puede volver a correr.
USE ClubCamionerosPruebaCuatro;
GO

-- 1. Valor de inscripción versionado. Sin columna de género: solo aplica a la rama
--    Masculina y esa regla la valida el servicio de alta. vigente_hasta no se guarda, se
--    calcula a partir del próximo vigente_desde (mismo criterio que ARANCELES).
IF OBJECT_ID('ARANCELES_INSCRIPCION') IS NULL
BEGIN
    CREATE TABLE ARANCELES_INSCRIPCION (
        PK_id_arancel_inscripcion INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_ARANCELES_INSCRIPCION PRIMARY KEY,
        monto DECIMAL(18,2) NOT NULL
            CONSTRAINT CK_ARANCELES_INSCRIPCION_MONTO CHECK (monto > 0),
        vigente_desde DATE NOT NULL
            CONSTRAINT UQ_ARANCELES_INSCRIPCION_FECHA UNIQUE
    );
END
GO

-- 2. Concepto de cada registro de PAGOS. Las filas existentes son todas cuotas y quedan
--    como 'Cuota' por el DEFAULT ... WITH VALUES.
IF COL_LENGTH('PAGOS', 'concepto') IS NULL
BEGIN
    ALTER TABLE PAGOS
        ADD concepto VARCHAR(20) NOT NULL
            CONSTRAINT DF_PAGOS_CONCEPTO DEFAULT 'Cuota' WITH VALUES;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PAGOS_CONCEPTO')
BEGIN
    ALTER TABLE PAGOS
        ADD CONSTRAINT CK_PAGOS_CONCEPTO CHECK (concepto IN ('Cuota', 'Inscripcion'));
END
GO

-- 3. Las consultas de pendientes (deuda global, deudores, estadísticas) van a pasar a
--    filtrar por concepto: se suma al INCLUDE para que el índice siga cubriéndolas.
IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_PAGOS_Estado_Pendientes' AND object_id = OBJECT_ID('PAGOS')
)
AND NOT EXISTS (
    SELECT 1 FROM sys.index_columns ic
    JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
    WHERE i.name = 'IX_PAGOS_Estado_Pendientes' AND ic.object_id = OBJECT_ID('PAGOS')
      AND COL_NAME(ic.object_id, ic.column_id) = 'concepto'
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_PAGOS_Estado_Pendientes
        ON PAGOS (estado)
        INCLUDE (FK_id_jugador, monto_base, monto_final, fecha_vencimiento, concepto)
        WITH (DROP_EXISTING = ON);
END
GO

-- 4. Fecha de alta del jugador, para auditoría. NULL en los jugadores cargados antes de
--    esta HU (no se conoce su fecha real de ingreso).
IF COL_LENGTH('JUGADORES', 'fecha_alta') IS NULL
BEGIN
    ALTER TABLE JUGADORES ADD fecha_alta DATE NULL;
END
GO
