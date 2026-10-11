/* =====================================================================================
   PASO 4 de 4 — Verificar que cacc_api quedó como corresponde (solo lectura, no cambia nada)
   -------------------------------------------------------------------------------------
   Conectado a la base del club, con una cuenta administradora. Todas las filas tienen que
   decir OK. Si alguna dice REVISAR, volvé a ejecutar 03_permisos_usuario_api.sql.
   ===================================================================================== */

EXECUTE AS USER = N'cacc_api';

SELECT 'No es sysadmin del servidor'          AS control, CASE WHEN IS_SRVROLEMEMBER('sysadmin') = 1 THEN 'REVISAR' ELSE 'OK' END AS resultado
UNION ALL
SELECT 'No es dueño de la base (db_owner)',            CASE WHEN IS_ROLEMEMBER('db_owner') = 1 THEN 'REVISAR' ELSE 'OK' END
UNION ALL
SELECT 'Puede leer PAGOS',                             CASE WHEN HAS_PERMS_BY_NAME('dbo.PAGOS', 'OBJECT', 'SELECT') = 1 THEN 'OK' ELSE 'REVISAR' END
UNION ALL
SELECT 'Puede registrar pagos (INSERT en PAGOS)',      CASE WHEN HAS_PERMS_BY_NAME('dbo.PAGOS', 'OBJECT', 'INSERT') = 1 THEN 'OK' ELSE 'REVISAR' END
UNION ALL
SELECT 'Puede usar la secuencia de pagos',             CASE WHEN HAS_PERMS_BY_NAME('dbo.SEQ_PAGOS', 'OBJECT', 'UPDATE') = 1 THEN 'OK' ELSE 'REVISAR' END
UNION ALL
SELECT 'NO puede borrar la auditoría',                 CASE WHEN HAS_PERMS_BY_NAME('dbo.AUDITORIA_CAMBIOS', 'OBJECT', 'DELETE') = 1 THEN 'REVISAR' ELSE 'OK' END
UNION ALL
SELECT 'NO puede modificar la auditoría',              CASE WHEN HAS_PERMS_BY_NAME('dbo.AUDITORIA_CAMBIOS', 'OBJECT', 'UPDATE') = 1 THEN 'REVISAR' ELSE 'OK' END
UNION ALL
SELECT 'NO puede borrar pagos anulados',               CASE WHEN HAS_PERMS_BY_NAME('dbo.PAGOS_ANULADOS', 'OBJECT', 'DELETE') = 1 THEN 'REVISAR' ELSE 'OK' END
UNION ALL
SELECT 'NO puede cambiar la estructura de PAGOS',      CASE WHEN HAS_PERMS_BY_NAME('dbo.PAGOS', 'OBJECT', 'ALTER') = 1 THEN 'REVISAR' ELSE 'OK' END
UNION ALL
SELECT 'NO puede crear tablas',                        CASE WHEN HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE TABLE') = 1 THEN 'REVISAR' ELSE 'OK' END;

REVERT;
