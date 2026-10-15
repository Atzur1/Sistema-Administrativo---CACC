-- Becados y Descuentos: se separan dos fechas que hasta ahora eran una sola.
--
--   fecha_asignacion = el dia en que se cargo la bonificacion (dato de registro, lo pone el sistema).
--   fecha_inicio     = desde que cuota rige. Para las bonificaciones nuevas es el primer dia de un mes (el periodo
--                      de la cuota), y fecha_fin el ultimo dia de un mes: una bonificacion cubre cuotas enteras.
--
-- Columna nueva y opcional: no cambia nada para el otro equipo. Las bonificaciones cargadas antes de esta
-- migracion quedan con la fecha vacia a proposito: completarlas haria que el disparador de auditoria registre
-- una modificacion por cada una, sin usuario, y no hay un dato exacto para rellenar.
IF COL_LENGTH('dbo.JUGADORES_DESCUENTOS', 'fecha_asignacion') IS NULL
    ALTER TABLE dbo.JUGADORES_DESCUENTOS ADD fecha_asignacion DATE NULL;
