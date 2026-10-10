-- Marca explícita de beneficio anulado (asignado por error) en JUGADORES_DESCUENTOS.
--
-- Hasta ahora "Anulada" se deducía de estado_activo = 0 con fecha_cancelacion anterior a
-- fecha_inicio. Esa misma combinación la produce cancelar un beneficio Programado (la fecha de
-- hoy queda antes de su inicio), así que esos cancelados se mostraban como anulados. Con esta
-- columna el estado ya no depende de las fechas.
IF COL_LENGTH('dbo.JUGADORES_DESCUENTOS', 'anulado') IS NULL
    ALTER TABLE dbo.JUGADORES_DESCUENTOS
        ADD anulado BIT NOT NULL CONSTRAINT DF_JUGDESC_ANULADO DEFAULT (0);
