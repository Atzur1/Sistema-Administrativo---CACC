-- La auditoria de ARANCELES ahora tambien registra la categoria (idCategoria), ya que un
-- arancel puede ser por genero o por categoria. Mismo trigger de V20260929_03 con ese campo extra.
CREATE OR ALTER TRIGGER dbo.TR_AUDIT_ARANCELES ON dbo.ARANCELES AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIO WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'ARANCELES', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_arancel, d.PK_id_arancel)),
        CASE WHEN d.PK_id_arancel IS NULL THEN 'INSERT' WHEN i.PK_id_arancel IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_arancel IS NULL THEN NULL ELSE (SELECT d.genero, d.FK_id_categoria AS idCategoria, d.monto, d.vigente_desde FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_arancel IS NULL THEN NULL ELSE (SELECT i.genero, i.FK_id_categoria AS idCategoria, i.monto, i.vigente_desde FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_arancel = i.PK_id_arancel;
END;
