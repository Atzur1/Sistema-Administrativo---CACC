/* =====================================================================================
   PASO 1 de 4 — Crear el LOGIN con el que la API se conecta a SQL Server
   -------------------------------------------------------------------------------------
   Se ejecuta UNA SOLA VEZ por servidor, en la base master, con una cuenta administradora
   (sa o un login con rol sysadmin). No es una migración: no lo corre la API.

   Antes de ejecutar:
     1. Reemplazá la contraseña de abajo por una larga y aleatoria (mínimo 20 caracteres).
        Guardala en el gestor de secretos del despliegue: es la que va en la cadena de
        conexión de la API (ConnectionStrings__ConexionSQL). No la guardes en el repositorio.
     2. Reemplazá el nombre de la base por el de producción.

   Este login es técnico: lo usa solo la API. Las personas siguen entrando al portal con su
   DNI y contraseña (por ejemplo, el SuperAdmin); eso no cambia.
   ===================================================================================== */

DECLARE @Contrasena NVARCHAR(128) = N'REEMPLAZAR-por-una-contrasena-larga-y-aleatoria';
DECLARE @Base SYSNAME = N'ClubCamionerosCACC';

IF @Contrasena LIKE N'REEMPLAZAR%'
    THROW 50001, 'Cambiá la contraseña antes de ejecutar el script.', 1;

IF SUSER_ID(N'cacc_api') IS NULL
BEGIN
    DECLARE @sql NVARCHAR(MAX) =
        N'CREATE LOGIN [cacc_api] WITH PASSWORD = ' + QUOTENAME(@Contrasena, '''') +
        N', DEFAULT_DATABASE = ' + QUOTENAME(@Base) +
        N', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;';
    EXEC sys.sp_executesql @sql;
    PRINT N'Login cacc_api creado.';
END
ELSE
    PRINT N'El login cacc_api ya existía: no se modificó.';
GO
