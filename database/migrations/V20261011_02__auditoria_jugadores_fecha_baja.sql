-- La auditoria de JUGADORES tambien registra la fecha de baja (V20261011_01): quien dio de baja o
-- reactivo a un jugador queda en AUDITORIA_CAMBIOS como cualquier otro cambio.
CREATE OR ALTER TRIGGER dbo.TR_AUDIT_JUGADORES ON dbo.JUGADORES AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIO WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'JUGADORES', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_jugador, d.PK_id_jugador)),
        CASE WHEN d.PK_id_jugador IS NULL THEN 'INSERT' WHEN i.PK_id_jugador IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_jugador IS NULL THEN NULL ELSE (SELECT d.FK_id_persona AS idPersona, d.FK_id_categoria AS idCategoria, d.fecha_alta, d.fecha_baja FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_jugador IS NULL THEN NULL ELSE (SELECT i.FK_id_persona AS idPersona, i.FK_id_categoria AS idCategoria, i.fecha_alta, i.fecha_baja FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_jugador = i.PK_id_jugador;
END;
