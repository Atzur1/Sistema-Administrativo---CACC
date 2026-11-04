/* HU-061 - Datos de prueba (todos ficticios: DNI 9061xxxx, correos @cacc.test).
   Se ejecuta sobre una base recien creada con database/esquema/ClubCamioneros_unificada_crear_base.sql
   (ver 00_preparar_entorno.ps1). NO ejecutar sobre ClubCamionerosPRUEBA ni CCACUnificada.

   Contrasena de todas las cuentas que ya tienen una: Deporte#QA2026 (hash BCrypt, factor 12).

   | Clave     | Persona                    | Rol deportivo (FK_id_rol)    | Acceso Administrativo al empezar |
   |-----------|----------------------------|------------------------------|----------------------------------|
   | SA1       | Qa Superadmin Uno          | 1 Administrador (Tesorero)   | SuperAdmin (rol_portal 1)        |
   | CAND      | Qa Candidato Deportivo     | 2 Administrativo             | sin acceso (candidato)           |
   | CAND2     | Qa Tesorero Deportivo      | 1 Administrador (Tesorero)   | sin acceso (candidato)           |
   | DEP       | Qa Solo Deportivo          | 4 Director Tecnico           | sin acceso, nunca candidato      |
   | CAND3     | Qa Candidato Pendiente     | 1 Administrador (Tesorero)   | sin acceso (candidato)           |
   | JUG       | Qa Jugador Prueba          | (jugador, sin cuenta)        | -                                |
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() NOT LIKE N'CCACUnificada[_]QA[_]HU061%'
BEGIN
    RAISERROR(N'Este script solo corre sobre una base CCACUnificada_QA_HU061*.', 16, 1);
    RETURN;
END;

IF EXISTS (SELECT 1 FROM dbo.USUARIOS)
BEGIN
    RAISERROR(N'La base ya tiene usuarios: recreala con 00_preparar_entorno.ps1.', 16, 1);
    RETURN;
END;

DECLARE @hash VARCHAR(255) = '$2a$12$5Q9AZ0gXSz7Jp1fpLkZXMOaQpuBZsNZskZsPnBvN0hEbKHOX0sIOO';
DECLARE @persona INT;

BEGIN TRANSACTION;

INSERT dbo.PERSONAS (nombre, apellido, dni) VALUES ('Qa', 'Superadmin Uno', '90610001');
SET @persona = SCOPE_IDENTITY();
INSERT dbo.USUARIOS (FK_id_persona, FK_id_rol, email, contrasenia, activo, acceso_portal, rol_portal)
VALUES (@persona, 1, 'sa1@cacc.test', @hash, 1, 1, 1);

INSERT dbo.PERSONAS (nombre, apellido, dni) VALUES ('Qa', 'Candidato Deportivo', '90610003');
SET @persona = SCOPE_IDENTITY();
INSERT dbo.USUARIOS (FK_id_persona, FK_id_rol, email, contrasenia, activo, acceso_portal, rol_portal)
VALUES (@persona, 2, 'cand@cacc.test', @hash, 1, 0, NULL);

INSERT dbo.PERSONAS (nombre, apellido, dni) VALUES ('Qa', 'Tesorero Deportivo', '90610004');
SET @persona = SCOPE_IDENTITY();
INSERT dbo.USUARIOS (FK_id_persona, FK_id_rol, email, contrasenia, activo, acceso_portal, rol_portal)
VALUES (@persona, 1, 'cand2@cacc.test', @hash, 1, 0, NULL);

INSERT dbo.PERSONAS (nombre, apellido, dni) VALUES ('Qa', 'Solo Deportivo', '90610005');
SET @persona = SCOPE_IDENTITY();
INSERT dbo.USUARIOS (FK_id_persona, FK_id_rol, email, contrasenia, activo, acceso_portal, rol_portal)
VALUES (@persona, 4, 'dep@cacc.test', @hash, 1, 0, NULL);

INSERT dbo.PERSONAS (nombre, apellido, dni) VALUES ('Qa', 'Candidato Pendiente', '90610006');
SET @persona = SCOPE_IDENTITY();
INSERT dbo.USUARIOS (FK_id_persona, FK_id_rol, email, contrasenia, activo, acceso_portal, rol_portal)
VALUES (@persona, 1, 'cand3@cacc.test', @hash, 1, 0, NULL);

-- Un jugador para que los endpoints con id respondan con datos (genero y categoria son obligatorios).
INSERT dbo.PERSONAS (nombre, apellido, dni, genero) VALUES ('Qa', 'Jugador Prueba', '90610099', 'Masculino');
SET @persona = SCOPE_IDENTITY();
INSERT dbo.JUGADORES (FK_id_persona, FK_id_categoria) VALUES (@persona, 1);

COMMIT TRANSACTION;

SELECT u.PK_id_usuario, p.PK_id_persona, p.dni, u.email, u.FK_id_rol, u.acceso_portal, u.rol_portal, u.token_version
FROM dbo.USUARIOS u JOIN dbo.PERSONAS p ON p.PK_id_persona = u.FK_id_persona
ORDER BY u.PK_id_usuario;
