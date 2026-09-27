-- Corrige un bug real: cancelar una beca/descuento le borraba el beneficio a los meses ya
-- pasados que estuvieron correctamente cubiertos mientras estuvo activo, porque el cálculo de
-- saldo de una cuota (DescuentosSql.ApplyDescuentoActivo, PagosDao.ObtenerDescuentoAplicableEnPeriodo)
-- solo miraba "¿hay un beneficio con estado_activo = 1 hoy que cubra esta fecha?" — al cancelar,
-- esa pregunta deja de encontrar CUALQUIER cuota que ese beneficio cubría, pasadas o futuras.
--
-- fecha_cancelacion guarda el último día que el beneficio sigue contando para una cuota: los
-- meses posteriores a esa fecha dejan de tener el descuento, pero los anteriores lo conservan
-- aunque el beneficio ya no esté vigente hoy. NULL = nunca se canceló, sigue el rango
-- fecha_inicio/fecha_fin de siempre.
USE ClubCamionerosPruebaCuatro;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('JUGADORES_DESCUENTOS') AND name = 'fecha_cancelacion'
)
BEGIN
    ALTER TABLE JUGADORES_DESCUENTOS ADD fecha_cancelacion DATE NULL;
END
GO

-- Beneficios que ya estaban cancelados ANTES de esta migración no tienen registrada la fecha
-- real en que se cancelaron (esa información nunca se guardó). Sin backfill, quedarían con
-- fecha_cancelacion NULL, que la nueva lógica interpreta como "nunca se canceló" — el bug que
-- se corrige acá, pero al revés (reaplicaría el descuento sin límite). Como mejor aproximación
-- disponible, se los marca cancelados a partir de hoy: es exacto para los que se cancelaron hoy
-- mismo, y para cualquier otro es más seguro que dejarlos sin fecha de corte.
UPDATE JUGADORES_DESCUENTOS
    SET fecha_cancelacion = CAST(GETDATE() AS DATE)
    WHERE estado_activo = 0 AND fecha_cancelacion IS NULL;
GO
