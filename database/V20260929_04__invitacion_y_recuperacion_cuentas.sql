USE ClubCamionerosPruebaCuatro;
GO

IF COL_LENGTH('dbo.USUARIO', 'activacion_pendiente') IS NULL
    ALTER TABLE dbo.USUARIO ADD activacion_pendiente BIT NOT NULL CONSTRAINT DF_USUARIO_activacion_pendiente DEFAULT 0;
GO

IF COL_LENGTH('dbo.USUARIO', 'token_version') IS NULL
    ALTER TABLE dbo.USUARIO ADD token_version INT NOT NULL CONSTRAINT DF_USUARIO_token_version DEFAULT 0;
GO

IF OBJECT_ID('dbo.TOKEN_ACCESO_CUENTA', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TOKEN_ACCESO_CUENTA
    (
        PK_id_token BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TOKEN_ACCESO_CUENTA PRIMARY KEY,
        id_usuario INT NOT NULL,
        tipo VARCHAR(16) NOT NULL,
        hash_token CHAR(64) NOT NULL,
        creado_utc DATETIME2(7) NOT NULL CONSTRAINT DF_TOKEN_ACCESO_CUENTA_creado DEFAULT SYSUTCDATETIME(),
        vence_utc DATETIME2(7) NOT NULL,
        consumido_utc DATETIME2(7) NULL,
        CONSTRAINT FK_TOKEN_ACCESO_CUENTA_USUARIO FOREIGN KEY (id_usuario) REFERENCES dbo.USUARIO(PK_id_usuario),
        CONSTRAINT CK_TOKEN_ACCESO_CUENTA_tipo CHECK (tipo IN ('ACTIVACION', 'RECUPERACION')),
        CONSTRAINT UQ_TOKEN_ACCESO_CUENTA_hash UNIQUE (hash_token)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.TOKEN_ACCESO_CUENTA') AND name = 'IX_TOKEN_ACCESO_CUENTA_usuario_tipo')
    CREATE INDEX IX_TOKEN_ACCESO_CUENTA_usuario_tipo ON dbo.TOKEN_ACCESO_CUENTA(id_usuario, tipo, consumido_utc, vence_utc);
GO

CREATE OR ALTER TRIGGER dbo.TR_AUDIT_TOKEN_ACCESO_CUENTA ON dbo.TOKEN_ACCESO_CUENTA AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = COALESCE(TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario')), (SELECT TOP (1) id_usuario FROM inserted));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIO WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'TOKEN_ACCESO_CUENTA', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_token, d.PK_id_token)),
        CASE WHEN d.PK_id_token IS NULL THEN 'INSERT' WHEN i.PK_id_token IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_token IS NULL THEN NULL ELSE (SELECT d.id_usuario AS idUsuario, d.tipo, d.vence_utc AS venceUtc, d.consumido_utc AS consumidoUtc FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_token IS NULL THEN NULL ELSE (SELECT i.id_usuario AS idUsuario, i.tipo, i.vence_utc AS venceUtc, i.consumido_utc AS consumidoUtc FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_token = i.PK_id_token;
END;
GO

-- Keep account lifecycle and credential-change facts in the audit trail while
-- never copying password hashes or plaintext credentials into the log.
CREATE OR ALTER TRIGGER dbo.TR_AUDIT_USUARIO ON dbo.USUARIO AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIO WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'USUARIO', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_usuario, d.PK_id_usuario)),
        CASE WHEN d.PK_id_usuario IS NULL THEN 'INSERT' WHEN i.PK_id_usuario IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_usuario IS NULL THEN NULL ELSE
            (SELECT d.email, d.FK_id_rol AS idRol, d.activo, d.activacion_pendiente AS activacionPendiente,
                CONVERT(bit, CASE WHEN d.PK_id_usuario IS NOT NULL AND i.PK_id_usuario IS NOT NULL AND ISNULL(d.password_hash, N'') <> ISNULL(i.password_hash, N'') THEN 1 ELSE 0 END) AS credencialActualizada
             FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_usuario IS NULL THEN NULL ELSE
            (SELECT i.email, i.FK_id_rol AS idRol, i.activo, i.activacion_pendiente AS activacionPendiente,
                CONVERT(bit, CASE WHEN d.PK_id_usuario IS NOT NULL AND i.PK_id_usuario IS NOT NULL AND ISNULL(d.password_hash, N'') <> ISNULL(i.password_hash, N'') THEN 1 ELSE 0 END) AS credencialActualizada
             FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_usuario = i.PK_id_usuario;
END;
GO

DENY UPDATE, DELETE ON OBJECT::dbo.AUDITORIA_CAMBIOS TO public;
GO
