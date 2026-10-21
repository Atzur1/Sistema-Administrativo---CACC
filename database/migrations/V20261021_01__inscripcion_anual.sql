-- La inscripcion pasa a ser anual.
--
-- Hasta ahora un jugador tenia UNA inscripcion pendiente a la vez (indice unico por jugador). Con la inscripcion anual
-- cada jugador tiene una por anio, y si no pago la del anio anterior puede coexistir con la nueva: lo que identifica a
-- cada inscripcion es el dia en que empieza su anio (PAGOS.fecha_vencimiento). El indice unico pasa a ser por
-- (jugador, fecha_vencimiento): dos inscripciones de anios distintos conviven, pero no dos del mismo anio.
-- Es idempotente.
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.PAGOS') AND name = N'UX_PAGOS_InscripcionPendiente_Jugador')
    DROP INDEX UX_PAGOS_InscripcionPendiente_Jugador ON dbo.PAGOS;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.PAGOS') AND name = N'UX_PAGOS_InscripcionPendiente_Periodo')
    EXEC sys.sp_executesql N'
        CREATE UNIQUE INDEX UX_PAGOS_InscripcionPendiente_Periodo
        ON dbo.PAGOS (FK_id_jugador, fecha_vencimiento)
        WHERE estado = 0 AND concepto = ''Inscripcion'';';
