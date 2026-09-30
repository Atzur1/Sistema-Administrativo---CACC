-- Indice del script historico V20260920_01, adaptado al ejecutor automatico.
-- Acelera consultas de cuotas pendientes sin cambiar sus registros.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_PAGOS_Estado_Pendientes'
      AND object_id = OBJECT_ID(N'dbo.PAGOS')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_PAGOS_Estado_Pendientes
        ON dbo.PAGOS (estado)
        INCLUDE (FK_id_jugador, monto_base, monto_final, fecha_vencimiento);
END;
