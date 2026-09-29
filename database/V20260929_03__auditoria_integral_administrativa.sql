USE ClubCamionerosPruebaCuatro;
GO

IF OBJECT_ID('dbo.AUDITORIA_CAMBIOS', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AUDITORIA_CAMBIOS
    (
        PK_id_evento BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AUDITORIA_CAMBIOS PRIMARY KEY,
        id_usuario INT NULL,
        email_usuario NVARCHAR(254) NULL,
        entidad SYSNAME NOT NULL,
        id_entidad NVARCHAR(128) NOT NULL,
        accion VARCHAR(10) NOT NULL,
        fecha_utc DATETIME2(7) NOT NULL CONSTRAINT DF_AUDITORIA_CAMBIOS_fecha_utc DEFAULT SYSUTCDATETIME(),
        datos_antes NVARCHAR(MAX) NULL,
        datos_despues NVARCHAR(MAX) NULL,
        CONSTRAINT CK_AUDITORIA_CAMBIOS_accion CHECK (accion IN ('INSERT', 'UPDATE', 'DELETE')),
        CONSTRAINT CK_AUDITORIA_CAMBIOS_antes_json CHECK (datos_antes IS NULL OR ISJSON(datos_antes) = 1),
        CONSTRAINT CK_AUDITORIA_CAMBIOS_despues_json CHECK (datos_despues IS NULL OR ISJSON(datos_despues) = 1)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.AUDITORIA_CAMBIOS') AND name = 'IX_AUDITORIA_CAMBIOS_fecha')
    CREATE INDEX IX_AUDITORIA_CAMBIOS_fecha ON dbo.AUDITORIA_CAMBIOS(fecha_utc DESC, PK_id_evento DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.AUDITORIA_CAMBIOS') AND name = 'IX_AUDITORIA_CAMBIOS_usuario_fecha')
    CREATE INDEX IX_AUDITORIA_CAMBIOS_usuario_fecha ON dbo.AUDITORIA_CAMBIOS(id_usuario, fecha_utc DESC, PK_id_evento DESC);
GO

-- Triggers write the audit row in the same transaction as each domain change.
-- Actor context is set by SqlConnectionFactory from the validated JWT claim.
CREATE OR ALTER TRIGGER dbo.TR_AUDIT_PAGOS ON dbo.PAGOS AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIO WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'PAGOS', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_pago, d.PK_id_pago)),
        CASE WHEN d.PK_id_pago IS NULL THEN 'INSERT' WHEN i.PK_id_pago IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_pago IS NULL THEN NULL ELSE (SELECT d.FK_id_jugador AS idJugador, d.monto_base AS montoBase, d.FK_id_jugador_descuento AS idDescuento, d.monto_final AS montoFinal, d.fecha_pago AS fechaPago, d.metodo_pago AS metodoPago, d.fecha_vencimiento AS vencimiento, d.estado, d.concepto FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_pago IS NULL THEN NULL ELSE (SELECT i.FK_id_jugador AS idJugador, i.monto_base AS montoBase, i.FK_id_jugador_descuento AS idDescuento, i.monto_final AS montoFinal, i.fecha_pago AS fechaPago, i.metodo_pago AS metodoPago, i.fecha_vencimiento AS vencimiento, i.estado, i.concepto FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_pago = i.PK_id_pago;
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_AUDIT_ARANCELES ON dbo.ARANCELES AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIO WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'ARANCELES', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_arancel, d.PK_id_arancel)),
        CASE WHEN d.PK_id_arancel IS NULL THEN 'INSERT' WHEN i.PK_id_arancel IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_arancel IS NULL THEN NULL ELSE (SELECT d.genero, d.monto, d.vigente_desde FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_arancel IS NULL THEN NULL ELSE (SELECT i.genero, i.monto, i.vigente_desde FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_arancel = i.PK_id_arancel;
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_AUDIT_ARANCELES_INSCRIPCION ON dbo.ARANCELES_INSCRIPCION AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIO WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'ARANCELES_INSCRIPCION', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_arancel_inscripcion, d.PK_id_arancel_inscripcion)),
        CASE WHEN d.PK_id_arancel_inscripcion IS NULL THEN 'INSERT' WHEN i.PK_id_arancel_inscripcion IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_arancel_inscripcion IS NULL THEN NULL ELSE (SELECT d.monto, d.vigente_desde FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_arancel_inscripcion IS NULL THEN NULL ELSE (SELECT i.monto, i.vigente_desde FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_arancel_inscripcion = i.PK_id_arancel_inscripcion;
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_AUDIT_JUGADORES_DESCUENTOS ON dbo.JUGADORES_DESCUENTOS AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIO WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'JUGADORES_DESCUENTOS', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_jugador_descuento, d.PK_id_jugador_descuento)),
        CASE WHEN d.PK_id_jugador_descuento IS NULL THEN 'INSERT' WHEN i.PK_id_jugador_descuento IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_jugador_descuento IS NULL THEN NULL ELSE (SELECT d.FK_id_jugador AS idJugador, d.FK_id_descuento AS idTipo, d.estado_activo AS activo, d.tipo_valor AS tipoValor, d.porcentaje, d.monto_fijo AS montoFijo, d.fecha_inicio AS desde, d.fecha_fin AS hasta, d.fecha_cancelacion AS fechaCancelacion FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_jugador_descuento IS NULL THEN NULL ELSE (SELECT i.FK_id_jugador AS idJugador, i.FK_id_descuento AS idTipo, i.estado_activo AS activo, i.tipo_valor AS tipoValor, i.porcentaje, i.monto_fijo AS montoFijo, i.fecha_inicio AS desde, i.fecha_fin AS hasta, i.fecha_cancelacion AS fechaCancelacion FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_jugador_descuento = i.PK_id_jugador_descuento;
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_AUDIT_JUGADORES ON dbo.JUGADORES AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIO WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'JUGADORES', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_jugador, d.PK_id_jugador)),
        CASE WHEN d.PK_id_jugador IS NULL THEN 'INSERT' WHEN i.PK_id_jugador IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_jugador IS NULL THEN NULL ELSE (SELECT d.FK_id_persona AS idPersona, d.FK_id_categoria AS idCategoria, d.fecha_alta FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_jugador IS NULL THEN NULL ELSE (SELECT i.FK_id_persona AS idPersona, i.FK_id_categoria AS idCategoria, i.fecha_alta FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_jugador = i.PK_id_jugador;
END;
GO

-- PERSONA contains sensitive identity data. Record the affected row and action but
-- deliberately do not duplicate DNI, name, date of birth, or other personal values.
CREATE OR ALTER TRIGGER dbo.TR_AUDIT_PERSONA ON dbo.PERSONA AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIO WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion)
    SELECT @actor, @email, N'PERSONA', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_persona, d.PK_id_persona)),
        CASE WHEN d.PK_id_persona IS NULL THEN 'INSERT' WHEN i.PK_id_persona IS NULL THEN 'DELETE' ELSE 'UPDATE' END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_persona = i.PK_id_persona;
END;
GO

-- Credential values are intentionally excluded from the snapshot.
CREATE OR ALTER TRIGGER dbo.TR_AUDIT_USUARIO ON dbo.USUARIO AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIO WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'USUARIO', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_usuario, d.PK_id_usuario)),
        CASE WHEN d.PK_id_usuario IS NULL THEN 'INSERT' WHEN i.PK_id_usuario IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_usuario IS NULL THEN NULL ELSE (SELECT d.email, d.FK_id_rol AS idRol, d.activo FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_usuario IS NULL THEN NULL ELSE (SELECT i.email, i.FK_id_rol AS idRol, i.activo FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_usuario = i.PK_id_usuario;
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_AUDIT_CATEGORIAS ON dbo.CATEGORIAS AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIO WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'CATEGORIAS', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_categoria, d.PK_id_categoria)),
        CASE WHEN d.PK_id_categoria IS NULL THEN 'INSERT' WHEN i.PK_id_categoria IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_categoria IS NULL THEN NULL ELSE (SELECT d.nombre_categoria FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_categoria IS NULL THEN NULL ELSE (SELECT i.nombre_categoria FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_categoria = i.PK_id_categoria;
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_AUDIT_TIPO_DESCUENTO ON dbo.TIPO_DESCUENTO AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIO WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'TIPO_DESCUENTO', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_descuento, d.PK_id_descuento)),
        CASE WHEN d.PK_id_descuento IS NULL THEN 'INSERT' WHEN i.PK_id_descuento IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_descuento IS NULL THEN NULL ELSE (SELECT d.tipo_descuento FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_descuento IS NULL THEN NULL ELSE (SELECT i.tipo_descuento FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_descuento = i.PK_id_descuento;
END;
GO

-- Ordinary database principals cannot rewrite the history. Database owners and
-- sysadmins retain administrative control, as required for SQL Server operation.
DENY UPDATE, DELETE ON OBJECT::dbo.AUDITORIA_CAMBIOS TO public;
GO
