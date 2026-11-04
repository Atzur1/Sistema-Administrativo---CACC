USE ClubCamionerosPruebaCuatro;
GO

-- HU-053: este script es de la base del Portal Administrativo ANTERIOR a la unificacion (tablas USUARIO/PERSONA). En la
-- base unificada (USUARIOS/PERSONAS) acceso_portal y rol_portal ya existen y FK_id_rol es del Portal Deportivo: no se
-- aplica nada, ni las columnas, ni la migracion de datos, ni el trigger.
IF OBJECT_ID(N'dbo.USUARIOS', N'U') IS NOT NULL OR OBJECT_ID(N'dbo.PERSONAS', N'U') IS NOT NULL OR OBJECT_ID(N'dbo.USUARIO', N'U') IS NULL
BEGIN
    PRINT N'V20260929_07: la base no tiene dbo.USUARIO (o es la base unificada). No se hizo ningun cambio.';
    SET NOEXEC ON;
END;
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

-- Migración: el SuperAdmin actual (FK_id_rol = 1) queda habilitado como SuperAdmin
-- en nuestra propia columna. Sin esto, queda afuera del sistema en cuanto el login
-- empiece a exigir acceso_portal = 1.
--
-- HU-053: "FK_id_rol = 1" deja de identificar a nuestro SuperAdmin en cuanto la
-- base trae usuarios del Portal Deportivo: ahí el rol 1 es SU "Administrador
-- (Tesorero)", y el UPDATE original lo convertía en SuperAdmin de este portal
-- (probado en CCACUnificada: habilitaba al usuario 3, que viene del Deportivo).
-- Por eso solo se habilita a quien cumple TODO esto:
--   * FK_id_rol = 1, cuenta activa, sin acceso ni rol de portal todavía;
--   * con credencial de ESTE portal (password_hash, que solo escribe el Portal
--     Administrativo desde V20260929_02; las cuentas del Deportivo traen su clave
--     en contrasenia);
--   * a lo sumo UNA fila: hay un único SuperAdmin previo. Con más, se detiene.
-- Antes del UPDATE se listan y cuentan las filas (SELECT equivalente); si el UPDATE
-- afecta otra cantidad, se revierte. Quien necesite acceso y no lo reciba acá lo
-- habilita el SuperAdmin desde Usuarios (candidatos de UsuariosPortalDao).
IF (SELECT COUNT(*) FROM dbo.ROLES) <> 2
   OR NOT EXISTS (SELECT 1 FROM dbo.ROLES WHERE PK_id_rol = 1 AND nombre_rol = N'Administrador General')
    PRINT N'V20260929_07: ROLES no es el catálogo original del Portal Administrativo; FK_id_rol = 1 no alcanza para identificar al SuperAdmin y se exige su credencial (password_hash).';

DECLARE @candidatos TABLE (PK_id_usuario int PRIMARY KEY);
INSERT @candidatos (PK_id_usuario)
SELECT PK_id_usuario FROM dbo.USUARIO
WHERE FK_id_rol = 1 AND activo = 1 AND acceso_portal = 0 AND rol_portal IS NULL AND password_hash IS NOT NULL;

DECLARE @previstas int = (SELECT COUNT(*) FROM @candidatos);
SELECT u.PK_id_usuario, u.email, u.FK_id_rol FROM dbo.USUARIO u JOIN @candidatos c ON c.PK_id_usuario = u.PK_id_usuario;
PRINT CONCAT(N'V20260929_07: cuentas a habilitar como SuperAdmin = ', @previstas, N' (se esperan 0 o 1).');

IF @previstas > 1
    THROW 51207, N'V20260929_07: hay más de un candidato a SuperAdmin. No se habilitó a ninguno: revisar a mano.', 1;

BEGIN TRANSACTION;
UPDATE u SET acceso_portal = 1, rol_portal = 1
FROM dbo.USUARIO u JOIN @candidatos c ON c.PK_id_usuario = u.PK_id_usuario
WHERE u.acceso_portal = 0;
IF @@ROWCOUNT <> @previstas
BEGIN
    ROLLBACK TRANSACTION;
    THROW 51208, N'V20260929_07: el UPDATE afectó una cantidad distinta de la prevista. Se revirtió.', 1;
END;
COMMIT TRANSACTION;
GO

-- Redefine el snapshot de auditoría para incluir las columnas nuevas. El resto
-- del cuerpo es idéntico al de V20260929_05 (identidad del operador).
-- Va con SQL dinámico: CREATE TRIGGER sobre una tabla que no existe no compila ni
-- con NOEXEC, y en la base unificada dbo.USUARIO no existe.
IF OBJECT_ID(N'dbo.USUARIO', N'U') IS NOT NULL
EXEC sys.sp_executesql N'
CREATE OR ALTER TRIGGER dbo.TR_AUDIT_USUARIO ON dbo.USUARIO AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N''idUsuario''));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIO WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N''USUARIO'', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_usuario, d.PK_id_usuario)),
        CASE WHEN d.PK_id_usuario IS NULL THEN ''INSERT'' WHEN i.PK_id_usuario IS NULL THEN ''DELETE'' ELSE ''UPDATE'' END,
        CASE WHEN d.PK_id_usuario IS NULL THEN NULL ELSE
            (SELECT d.email, d.nombre, d.apellido, d.dni, d.FK_id_rol AS idRol, d.activo,
                d.activacion_pendiente AS activacionPendiente, d.acceso_portal AS accesoPortal, d.rol_portal AS rolPortal,
                CONVERT(bit, CASE WHEN d.PK_id_usuario IS NOT NULL AND i.PK_id_usuario IS NOT NULL AND ISNULL(d.password_hash, N'''') <> ISNULL(i.password_hash, N'''') THEN 1 ELSE 0 END) AS credencialActualizada
             FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_usuario IS NULL THEN NULL ELSE
            (SELECT i.email, i.nombre, i.apellido, i.dni, i.FK_id_rol AS idRol, i.activo,
                i.activacion_pendiente AS activacionPendiente, i.acceso_portal AS accesoPortal, i.rol_portal AS rolPortal,
                CONVERT(bit, CASE WHEN d.PK_id_usuario IS NOT NULL AND i.PK_id_usuario IS NOT NULL AND ISNULL(d.password_hash, N'''') <> ISNULL(i.password_hash, N'''') THEN 1 ELSE 0 END) AS credencialActualizada
             FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_usuario = i.PK_id_usuario;
END;';
GO

DENY UPDATE, DELETE ON OBJECT::dbo.AUDITORIA_CAMBIOS TO public;
GO

SET NOEXEC OFF;
GO
