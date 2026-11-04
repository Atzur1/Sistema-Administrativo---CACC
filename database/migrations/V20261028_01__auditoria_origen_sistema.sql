-- HU-055: sistema de origen en la auditoria.
--
-- Una escritura que llega por la API de integracion la hace el Portal Deportivo (credencial de sistema) en nombre de una
-- persona (el token del usuario). AUDITORIA_CAMBIOS ya guarda a la persona (id_usuario y su identidad congelada); esta
-- migracion agrega la otra dimension: origen_sistema = el sistema que hizo el pedido ('PortalDeportivo' por la integracion,
-- 'PortalAdministrativo' por el resto de esta API). NULL = no paso por esta API: la base es compartida con el Portal
-- Deportivo (HU-053), asi que su backend y los scripts de mantenimiento escriben sin sellar el origen. Los eventos
-- anteriores a esta migracion tambien quedan en NULL: no se sabe con certeza de donde vinieron.
--
-- La API lo pasa en SESSION_CONTEXT('origen') al abrir cada conexion (SqlConnectionFactory), igual que 'idUsuario', y lo
-- congela el trigger que ya completa la identidad del operador. El evento se escribe en la misma transaccion que el cambio:
-- si la operacion se revierte, el evento tambien.
--
-- Es idempotente. El trigger va con SQL dinamico porque CREATE TRIGGER debe ser la primera sentencia de su lote.
IF COL_LENGTH(N'dbo.AUDITORIA_CAMBIOS', N'origen_sistema') IS NULL
    ALTER TABLE dbo.AUDITORIA_CAMBIOS ADD origen_sistema nvarchar(50) NULL;

EXEC sys.sp_executesql N'
CREATE OR ALTER TRIGGER dbo.TR_AUDITORIA_IDENTIDAD_ACTOR
ON dbo.AUDITORIA_CAMBIOS
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    -- Congela la identidad del operador en cada evento (nombre y DNI salen de PERSONAS, fuente unica de identidad) y el
    -- sistema que hizo el pedido (SESSION_CONTEXT origen; NULL = no paso por la API del Portal Administrativo).
    UPDATE audit
    SET nombre_usuario = persona.nombre,
        apellido_usuario = persona.apellido,
        dni_usuario = persona.dni,
        origen_sistema = TRY_CONVERT(nvarchar(50), SESSION_CONTEXT(N''origen''))
    FROM dbo.AUDITORIA_CAMBIOS audit
    INNER JOIN inserted event ON event.PK_id_evento = audit.PK_id_evento
    LEFT JOIN dbo.USUARIOS usuario ON usuario.PK_id_usuario = event.id_usuario
    LEFT JOIN dbo.PERSONAS persona ON persona.PK_id_persona = usuario.FK_id_persona;
END;';
