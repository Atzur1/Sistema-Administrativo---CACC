-- HU-019 — Deuda Global Total (panel de tesorería "Cuotas y Pagos").
--
-- PagosDao.ObtenerResumen, PagosDao.ObtenerPendientesAgrupados y EstadisticasDao.ObtenerTotales
-- filtran todos por PAGOS.estado = 0 para barrer las cuotas pendientes, pero PAGOS solo tiene el
-- índice de la PK (PK_id_pago) — cada una de esas consultas hoy hace table scan completo. Con
-- 1261 filas no se nota, pero apenas arranque BE-4 (emisión mensual de cuotas, todavía pendiente)
-- va a crecer rápido y la HU-019 pide explícitamente evitar el barrido lento / bloqueante.
--
-- INCLUDE trae ya resueltas las columnas que esas consultas necesitan (FK_id_jugador para
-- agrupar/contar jugadores distintos, monto_base/monto_final/fecha_vencimiento para la fórmula
-- de DescuentosSql), así el índice cubre la consulta sin un lookup extra al índice clustered.
USE ClubCamionerosPruebaCuatro;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_PAGOS_Estado_Pendientes' AND object_id = OBJECT_ID('PAGOS')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_PAGOS_Estado_Pendientes
        ON PAGOS (estado)
        INCLUDE (FK_id_jugador, monto_base, monto_final, fecha_vencimiento);
END
GO
