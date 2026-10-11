-- La auditoria de JUGADORES_DESCUENTOS registra tambien si el beneficio se anulo (V20261010_01) y si se
-- cancelo por la baja del jugador (V20261012_01). Mismo trigger de V20260929_03 con esos dos campos extra.
CREATE OR ALTER TRIGGER dbo.TR_AUDIT_JUGADORES_DESCUENTOS ON dbo.JUGADORES_DESCUENTOS AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIO WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'JUGADORES_DESCUENTOS', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_jugador_descuento, d.PK_id_jugador_descuento)),
        CASE WHEN d.PK_id_jugador_descuento IS NULL THEN 'INSERT' WHEN i.PK_id_jugador_descuento IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_jugador_descuento IS NULL THEN NULL ELSE (SELECT d.FK_id_jugador AS idJugador, d.FK_id_descuento AS idTipo, d.estado_activo AS activo, d.tipo_valor AS tipoValor, d.porcentaje, d.monto_fijo AS montoFijo, d.fecha_inicio AS desde, d.fecha_fin AS hasta, d.fecha_cancelacion AS fechaCancelacion, d.anulado, d.cancelada_por_baja AS canceladaPorBaja FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_jugador_descuento IS NULL THEN NULL ELSE (SELECT i.FK_id_jugador AS idJugador, i.FK_id_descuento AS idTipo, i.estado_activo AS activo, i.tipo_valor AS tipoValor, i.porcentaje, i.monto_fijo AS montoFijo, i.fecha_inicio AS desde, i.fecha_fin AS hasta, i.fecha_cancelacion AS fechaCancelacion, i.anulado, i.cancelada_por_baja AS canceladaPorBaja FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_jugador_descuento = i.PK_id_jugador_descuento;
END;
