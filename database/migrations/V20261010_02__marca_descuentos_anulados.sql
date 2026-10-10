-- Completa anulado para los beneficios anulados antes de V20261010_01.
--
-- Anular siempre dejó fecha_cancelacion = EOMONTH(fecha_inicio, -1), el último día del mes
-- anterior al inicio. Cancelar deja la fecha del día en que se canceló, que solo coincide con
-- ese valor si se canceló justo ese día: es la mejor aproximación disponible.
UPDATE dbo.JUGADORES_DESCUENTOS
SET anulado = 1
WHERE estado_activo = 0
  AND anulado = 0
  AND fecha_cancelacion = EOMONTH(fecha_inicio, -1);
