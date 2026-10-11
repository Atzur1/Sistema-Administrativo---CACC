/* =====================================================================================
   PASO 2 de 4 — Crear el USUARIO cacc_api dentro de la base del club
   -------------------------------------------------------------------------------------
   Se ejecuta UNA SOLA VEZ, conectado a la base del club (no a master), con una cuenta
   administradora. Requiere que exista el login del paso 1.
   ===================================================================================== */

IF SUSER_ID(N'cacc_api') IS NULL
    THROW 50002, 'No existe el login cacc_api: ejecutá primero 01_crear_login_api.sql en master.', 1;

IF USER_ID(N'cacc_api') IS NULL
BEGIN
    CREATE USER [cacc_api] FOR LOGIN [cacc_api] WITH DEFAULT_SCHEMA = dbo;
    PRINT N'Usuario cacc_api creado en la base.';
END
ELSE
    PRINT N'El usuario cacc_api ya existía en la base: no se modificó.';
GO
