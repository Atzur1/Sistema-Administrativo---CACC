/* =====================================================================================
   PASO 3 de 4 — Permisos del usuario cacc_api (se puede volver a ejecutar sin riesgo)
   -------------------------------------------------------------------------------------
   Conectado a la base del club, con una cuenta administradora. Ejecutarlo después del paso 2
   y, por las dudas, después de cada despliegue que agregue migraciones.

   Qué puede hacer cacc_api:
     - Leer, agregar, modificar y borrar datos en el esquema dbo (todo lo que hace la API),
       incluidas las secuencias de ids (SEQ_PAGOS, SEQ_JUGADORES).
   Qué NO puede hacer:
     - Modificar ni borrar la auditoría (AUDITORIA_CAMBIOS) ni el registro de pagos anulados
       (PAGOS_ANULADOS). Sí puede agregar filas: lo hacen los triggers y la anulación de pagos.
     - Cambiar la estructura: crear, modificar o borrar tablas, triggers o índices, desactivar
       triggers de auditoría o vaciar tablas (TRUNCATE). Eso lo hace la cuenta administradora
       al aplicar las migraciones.
     - Nada fuera de esta base (otras bases, logins, configuración del servidor).

   Un único lote, sin GO: también lo ejecuta una prueba de integración.
   ===================================================================================== */

IF USER_ID(N'cacc_api') IS NULL
    THROW 50003, 'No existe el usuario cacc_api en esta base: ejecutá primero 02_crear_usuario_en_base.sql.', 1;

-- Por si en algún momento se lo agregó a un rol amplio: nunca dueño ni administrador de la base.
IF IS_ROLEMEMBER(N'db_owner', N'cacc_api') = 1 ALTER ROLE db_owner DROP MEMBER [cacc_api];
IF IS_ROLEMEMBER(N'db_ddladmin', N'cacc_api') = 1 ALTER ROLE db_ddladmin DROP MEMBER [cacc_api];
IF IS_ROLEMEMBER(N'db_securityadmin', N'cacc_api') = 1 ALTER ROLE db_securityadmin DROP MEMBER [cacc_api];

-- Datos: sobre el esquema completo, así cubre también las tablas y secuencias que agreguen migraciones futuras.
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO [cacc_api];

-- Auditoría y anulaciones: leer y agregar, nunca modificar ni borrar. DENY gana sobre el GRANT del esquema.
-- (No se niega CONTROL: incluye SELECT, y la pantalla de Auditoría necesita leer.)
DENY UPDATE, DELETE, ALTER, TAKE OWNERSHIP ON OBJECT::dbo.AUDITORIA_CAMBIOS TO [cacc_api];
IF OBJECT_ID(N'dbo.PAGOS_ANULADOS', N'U') IS NOT NULL
    DENY UPDATE, DELETE, ALTER, TAKE OWNERSHIP ON OBJECT::dbo.PAGOS_ANULADOS TO [cacc_api];

-- El historial de migraciones solo lo escribe la cuenta administradora.
IF OBJECT_ID(N'dbo.__CaccMigraciones', N'U') IS NOT NULL
    DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.__CaccMigraciones TO [cacc_api];

PRINT N'Permisos de cacc_api aplicados.';
