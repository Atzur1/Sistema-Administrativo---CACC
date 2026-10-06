-- El usuario para ingresar al Portal Administrativo pasa a ser el DNI (el correo se acepta durante la
-- transición). Para que el DNI identifique a una sola cuenta:
--   1. Las cuentas con acceso al portal guardan el DNI solo con dígitos (sin puntos, espacios ni guiones).
--   2. Dos cuentas con acceso no pueden compartir DNI.
--
-- Se limita a las cuentas con acceso_portal = 1 a propósito: USUARIO es una tabla compartida con el otro equipo,
-- y una restricción sobre toda la tabla podría rechazar sus altas. Quien todavía no tiene acceso se valida y se
-- normaliza recién al habilitarlo (UsuariosPortalDao.Habilitar).

SET QUOTED_IDENTIFIER ON;

UPDATE dbo.USUARIO
SET dni = REPLACE(REPLACE(REPLACE(dni, '.', ''), ' ', ''), '-', '')
WHERE acceso_portal = 1
  AND dni IS NOT NULL
  AND dni <> REPLACE(REPLACE(REPLACE(dni, '.', ''), ' ', ''), '-', '');

-- Si ya hubiera dos cuentas con acceso y el mismo DNI, esta sentencia falla con un error claro y la migración no
-- se aplica: hay que resolver ese duplicado a mano antes de seguir.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_USUARIO_dni_acceso_portal' AND object_id = OBJECT_ID('dbo.USUARIO'))
    CREATE UNIQUE INDEX UX_USUARIO_dni_acceso_portal
        ON dbo.USUARIO (dni)
        WHERE acceso_portal = 1 AND dni IS NOT NULL AND dni <> '';
