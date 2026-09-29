USE ClubCamionerosPruebaCuatro;
GO

/*
  Columna propia del portal para decidir acceso y rol, desacoplada de FK_id_rol
  (que pertenece al otro equipo y puede significar otra cosa para ellos, p.ej.
  un Director Técnico). acceso_portal es el gate real de login: sin él no hay
  ingreso posible, sin importar FK_id_rol o la contraseña. rol_portal es el rol
  que usamos nosotros para autorizar (1 = SuperAdmin, 2 = Administrador).
*/
IF COL_LENGTH('dbo.USUARIO', 'acceso_portal') IS NULL
    ALTER TABLE dbo.USUARIO ADD acceso_portal BIT NOT NULL CONSTRAINT DF_USUARIO_acceso_portal DEFAULT 0;
GO

IF COL_LENGTH('dbo.USUARIO', 'rol_portal') IS NULL
    ALTER TABLE dbo.USUARIO ADD rol_portal INT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_USUARIO_rol_portal_valores')
    ALTER TABLE dbo.USUARIO ADD CONSTRAINT CK_USUARIO_rol_portal_valores CHECK (rol_portal IS NULL OR rol_portal IN (1, 2));
GO

-- Nadie puede quedar con acceso habilitado sin un rol asignado.
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_USUARIO_acceso_portal_requiere_rol')
    ALTER TABLE dbo.USUARIO ADD CONSTRAINT CK_USUARIO_acceso_portal_requiere_rol CHECK (acceso_portal = 0 OR rol_portal IS NOT NULL);
GO

-- Migración: quien hoy entra con FK_id_rol = 1 queda habilitado como SuperAdmin
-- en nuestra propia columna. Sin esto, el superadmin actual queda afuera del
-- sistema en cuanto el login empiece a exigir acceso_portal = 1.
UPDATE dbo.USUARIO
SET acceso_portal = 1, rol_portal = 1
WHERE FK_id_rol = 1 AND activo = 1 AND acceso_portal = 0;
GO

-- Redefine el snapshot de auditoría para incluir las columnas nuevas. El resto
-- del cuerpo es idéntico al de V20260929_05 (identidad del operador).
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
                d.activacion_pendiente AS activacionPendiente, d.acceso_portal AS accesoPortal, d.rol_portal AS rolPortal,
                CONVERT(bit, CASE WHEN d.PK_id_usuario IS NOT NULL AND i.PK_id_usuario IS NOT NULL AND ISNULL(d.password_hash, N'') <> ISNULL(i.password_hash, N'') THEN 1 ELSE 0 END) AS credencialActualizada
             FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_usuario IS NULL THEN NULL ELSE
            (SELECT i.email, i.nombre, i.apellido, i.dni, i.FK_id_rol AS idRol, i.activo,
                i.activacion_pendiente AS activacionPendiente, i.acceso_portal AS accesoPortal, i.rol_portal AS rolPortal,
                CONVERT(bit, CASE WHEN d.PK_id_usuario IS NOT NULL AND i.PK_id_usuario IS NOT NULL AND ISNULL(d.password_hash, N'') <> ISNULL(i.password_hash, N'') THEN 1 ELSE 0 END) AS credencialActualizada
             FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_usuario = i.PK_id_usuario;
END;
GO

DENY UPDATE, DELETE ON OBJECT::dbo.AUDITORIA_CAMBIOS TO public;
GO
