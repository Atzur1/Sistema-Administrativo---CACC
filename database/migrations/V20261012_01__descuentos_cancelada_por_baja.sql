-- Una beca o descuento vigente o programado se cancela solo al dar de baja al jugador (JugadoresDao.DarDeBaja).
-- Esta marca distingue esa cancelacion de una hecha a mano, para mostrar "beca cancelada por baja" en las
-- cuotas que el jugador dejo pendientes. No cambia como se calcula el saldo: un beneficio cancelado sigue
-- aplicando a los meses que vencieron hasta la fecha de cancelacion, igual que cualquier otra cancelacion.
IF COL_LENGTH('dbo.JUGADORES_DESCUENTOS', 'cancelada_por_baja') IS NULL
    ALTER TABLE dbo.JUGADORES_DESCUENTOS
        ADD cancelada_por_baja BIT NOT NULL CONSTRAINT DF_JUGDESC_CANCELADA_POR_BAJA DEFAULT (0);
