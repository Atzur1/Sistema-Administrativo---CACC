USE ClubCamionerosPruebaCuatro;
GO

IF COL_LENGTH('dbo.AUDITORIA_CAMBIOS', 'nombre_usuario') IS NULL
    ALTER TABLE dbo.AUDITORIA_CAMBIOS ADD nombre_usuario NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.AUDITORIA_CAMBIOS', 'apellido_usuario') IS NULL
    ALTER TABLE dbo.AUDITORIA_CAMBIOS ADD apellido_usuario NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.AUDITORIA_CAMBIOS', 'dni_usuario') IS NULL
    ALTER TABLE dbo.AUDITORIA_CAMBIOS ADD dni_usuario NVARCHAR(20) NULL;
GO

-- Freeze the operator identity on each audit event. Historical rows remain NULL
-- because the identity at the time cannot be reconstructed reliably.
CREATE OR ALTER TRIGGER dbo.TR_AUDITORIA_IDENTIDAD_ACTOR
ON dbo.AUDITORIA_CAMBIOS
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE audit
    SET nombre_usuario = usuario.nombre,
        apellido_usuario = usuario.apellido,
        dni_usuario = usuario.dni
    FROM dbo.AUDITORIA_CAMBIOS audit
    INNER JOIN inserted event ON event.PK_id_evento = audit.PK_id_evento
    LEFT JOIN dbo.USUARIO usuario ON usuario.PK_id_usuario = event.id_usuario;
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.AUDITORIA_CAMBIOS')
      AND name = 'IX_AUDITORIA_CAMBIOS_entidad_evento'
)
    CREATE INDEX IX_AUDITORIA_CAMBIOS_entidad_evento
        ON dbo.AUDITORIA_CAMBIOS(entidad, id_entidad, accion, fecha_utc, PK_id_evento)
        INCLUDE (id_usuario);
GO
