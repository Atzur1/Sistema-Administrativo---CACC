USE ClubCamionerosPruebaCuatro;
GO

IF COL_LENGTH('dbo.USUARIO', 'nombre') IS NULL
    ALTER TABLE dbo.USUARIO ADD nombre NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.USUARIO', 'apellido') IS NULL
    ALTER TABLE dbo.USUARIO ADD apellido NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.USUARIO', 'dni') IS NULL
    ALTER TABLE dbo.USUARIO ADD dni NVARCHAR(20) NULL;
GO

-- Capture the account's declared identity in the change snapshot without
-- writing either plaintext passwords or password hashes to the audit record.
CREATE OR ALTER TRIGGER dbo.TR_AUDIT_USUARIO ON dbo.USUARIO AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIO WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'USUARIO', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_usuario, d.PK_id_usuario)),
        CASE WHEN d.PK_id_usuario IS NULL THEN 'INSERT' WHEN i.PK_id_usuario IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_usuario IS NULL THEN NULL ELSE
            (SELECT d.email, d.nombre, d.apellido, d.dni, d.FK_id_rol AS idRol, d.activo,
                d.activacion_pendiente AS activacionPendiente,
                CONVERT(bit, CASE WHEN d.PK_id_usuario IS NOT NULL AND i.PK_id_usuario IS NOT NULL AND ISNULL(d.password_hash, N'') <> ISNULL(i.password_hash, N'') THEN 1 ELSE 0 END) AS credencialActualizada
             FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_usuario IS NULL THEN NULL ELSE
            (SELECT i.email, i.nombre, i.apellido, i.dni, i.FK_id_rol AS idRol, i.activo,
                i.activacion_pendiente AS activacionPendiente,
                CONVERT(bit, CASE WHEN d.PK_id_usuario IS NOT NULL AND i.PK_id_usuario IS NOT NULL AND ISNULL(d.password_hash, N'') <> ISNULL(i.password_hash, N'') THEN 1 ELSE 0 END) AS credencialActualizada
             FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_usuario = i.PK_id_usuario;
END;
GO

DENY UPDATE, DELETE ON OBJECT::dbo.AUDITORIA_CAMBIOS TO public;
GO
