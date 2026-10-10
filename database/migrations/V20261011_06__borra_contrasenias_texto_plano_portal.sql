-- El Portal Administrativo ya no acepta contrasenas en texto plano (USUARIO.contrasenia): solo
-- password_hash (PBKDF2). Se borran las que quedaran cargadas en las cuentas con acceso al portal; esas
-- personas crean una contrasena nueva con "Olvidaste tu contrasena?" o con el enlace que les reenvia un
-- SuperAdmin desde Usuarios y Permisos.
--
-- Solo cuentas con acceso_portal = 1: las demas filas de USUARIO las administra el otro equipo. Eliminar
-- la columna, o impedir con una restriccion que se vuelva a cargar texto plano, requiere acordarlo con ellos.
UPDATE dbo.USUARIO
SET contrasenia = NULL
WHERE acceso_portal = 1 AND contrasenia IS NOT NULL;
