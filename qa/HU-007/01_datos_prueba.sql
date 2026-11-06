/* HU-007 - Datos de prueba (todos ficticios: DNI 9007xxxx, correos @cacc.test).
   Se ejecuta sobre una base recien creada con database/esquema/ClubCamioneros_unificada_crear_base.sql cambiando el
   nombre por CCACUnificada_QA_HU007*. NO ejecutar sobre ClubCamionerosPRUEBA ni CCACUnificada.
   Contrasena de todas las cuentas: Deporte#QA2026 (hash BCrypt, factor 12).

   | Clave | Persona                | Rol deportivo (FK_id_rol) | Administrativo | Categorias del staff (STAFF_CATEGORIAS) |
   |-------|------------------------|---------------------------|----------------|-----------------------------------------|
   | SA    | Qa Superadmin Siete    | 1 Administrador (Tesorero)| SuperAdmin     | -                                       |
   | ADM   | Qa Administrador Siete | 2 Administrativo          | Administrador  | -                                       |
   | DT2   | Qa Tecnico Dos         | 4 Director Tecnico        | sin acceso     | 2 y 3                                   |
   | PF1   | Qa Preparador Uno      | 6 Preparador Fisico       | sin acceso     | 2                                       |
   | DT0   | Qa Tecnico Sin Staff   | 4 Director Tecnico        | sin acceso     | (sin fila en STAFF: configuracion incompleta) |
   | MED   | Qa Medico Siete        | 3 Medico                  | sin acceso     | - (ve a todos por su rol)               |
   | NADA  | Qa Sin Portal          | (ninguno)                 | sin acceso     | -                                       |

   Jugadores: J2 categoria 2; J3 categoria 3; J4 categoria 4; J5 categoria de cobro 4 pero tambien pertenece a la 3
   (JUGADORES_CATEGORIAS).
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() NOT LIKE N'CCACUnificada[_]QA[_]HU007%'
BEGIN
    RAISERROR(N'Este script solo corre sobre una base CCACUnificada_QA_HU007*.', 16, 1);
    RETURN;
END;

IF EXISTS (SELECT 1 FROM dbo.USUARIOS)
BEGIN
    RAISERROR(N'La base ya tiene usuarios: usá una base recien creada.', 16, 1);
    RETURN;
END;

DECLARE @hash VARCHAR(255) = '$2a$12$5Q9AZ0gXSz7Jp1fpLkZXMOaQpuBZsNZskZsPnBvN0hEbKHOX0sIOO';
DECLARE @persona INT, @usuario INT, @staff INT, @jugador INT;
DECLARE @nuevo TABLE (id INT);

BEGIN TRANSACTION;

INSERT dbo.PERSONAS (nombre, apellido, dni) VALUES ('Qa', 'Superadmin Siete', '90070001');
INSERT dbo.USUARIOS (FK_id_persona, FK_id_rol, email, contrasenia, activo, acceso_portal, rol_portal)
VALUES (SCOPE_IDENTITY(), 1, 'sa7@cacc.test', @hash, 1, 1, 1);

INSERT dbo.PERSONAS (nombre, apellido, dni) VALUES ('Qa', 'Administrador Siete', '90070002');
INSERT dbo.USUARIOS (FK_id_persona, FK_id_rol, email, contrasenia, activo, acceso_portal, rol_portal)
VALUES (SCOPE_IDENTITY(), 2, 'adm7@cacc.test', @hash, 1, 1, 2);

INSERT dbo.PERSONAS (nombre, apellido, dni) VALUES ('Qa', 'Tecnico Dos', '90070003');
DELETE @nuevo;
INSERT dbo.USUARIOS (FK_id_persona, FK_id_rol, email, contrasenia, activo, acceso_portal, rol_portal)
OUTPUT INSERTED.PK_id_usuario INTO @nuevo VALUES (SCOPE_IDENTITY(), 4, 'dt2@cacc.test', @hash, 1, 0, NULL);
SELECT @usuario = id FROM @nuevo;
INSERT dbo.STAFF (FK_id_usuario) VALUES (@usuario);
SET @staff = SCOPE_IDENTITY();
INSERT dbo.STAFF_CATEGORIAS (FK_id_staff, FK_id_categoria) VALUES (@staff, 2), (@staff, 3);

INSERT dbo.PERSONAS (nombre, apellido, dni) VALUES ('Qa', 'Preparador Uno', '90070004');
DELETE @nuevo;
INSERT dbo.USUARIOS (FK_id_persona, FK_id_rol, email, contrasenia, activo, acceso_portal, rol_portal)
OUTPUT INSERTED.PK_id_usuario INTO @nuevo VALUES (SCOPE_IDENTITY(), 6, 'pf1@cacc.test', @hash, 1, 0, NULL);
SELECT @usuario = id FROM @nuevo;
INSERT dbo.STAFF (FK_id_usuario) VALUES (@usuario);
INSERT dbo.STAFF_CATEGORIAS (FK_id_staff, FK_id_categoria) VALUES (SCOPE_IDENTITY(), 2);

INSERT dbo.PERSONAS (nombre, apellido, dni) VALUES ('Qa', 'Tecnico Sin Staff', '90070005');
INSERT dbo.USUARIOS (FK_id_persona, FK_id_rol, email, contrasenia, activo, acceso_portal, rol_portal)
VALUES (SCOPE_IDENTITY(), 4, 'dt0@cacc.test', @hash, 1, 0, NULL);

INSERT dbo.PERSONAS (nombre, apellido, dni) VALUES ('Qa', 'Medico Siete', '90070006');
INSERT dbo.USUARIOS (FK_id_persona, FK_id_rol, email, contrasenia, activo, acceso_portal, rol_portal)
VALUES (SCOPE_IDENTITY(), 3, 'med7@cacc.test', @hash, 1, 0, NULL);

INSERT dbo.PERSONAS (nombre, apellido, dni) VALUES ('Qa', 'Sin Portal', '90070007');
INSERT dbo.USUARIOS (FK_id_persona, FK_id_rol, email, contrasenia, activo, acceso_portal, rol_portal)
VALUES (SCOPE_IDENTITY(), NULL, 'nada7@cacc.test', @hash, 1, 0, NULL);

-- Jugadores (genero y categoria obligatorios). El id sale de la secuencia SEQ_JUGADORES.
INSERT dbo.PERSONAS (nombre, apellido, dni, genero) VALUES ('Qa', 'Jugador Cat Dos', '90070102', 'Masculino');
INSERT dbo.JUGADORES (FK_id_persona, FK_id_categoria) VALUES (SCOPE_IDENTITY(), 2);
INSERT dbo.PERSONAS (nombre, apellido, dni, genero) VALUES ('Qa', 'Jugador Cat Tres', '90070103', 'Masculino');
INSERT dbo.JUGADORES (FK_id_persona, FK_id_categoria) VALUES (SCOPE_IDENTITY(), 3);
INSERT dbo.PERSONAS (nombre, apellido, dni, genero) VALUES ('Qa', 'Jugador Cat Cuatro', '90070104', 'Masculino');
INSERT dbo.JUGADORES (FK_id_persona, FK_id_categoria) VALUES (SCOPE_IDENTITY(), 4);
INSERT dbo.PERSONAS (nombre, apellido, dni, genero) VALUES ('Qa', 'Jugador Cuatro Y Tres', '90070105', 'Masculino');
SET @persona = SCOPE_IDENTITY();
INSERT dbo.JUGADORES (FK_id_persona, FK_id_categoria) VALUES (@persona, 4);
SELECT @jugador = PK_id_jugador FROM dbo.JUGADORES WHERE FK_id_persona = @persona;
INSERT dbo.JUGADORES_CATEGORIAS (FK_id_jugador, FK_id_categoria) VALUES (@jugador, 3);

COMMIT TRANSACTION;

SELECT u.PK_id_usuario, p.dni, u.email, ISNULL(CONVERT(varchar, u.FK_id_rol), 'NULL') AS rol_deportivo,
       u.acceso_portal, ISNULL(CONVERT(varchar, u.rol_portal), 'NULL') AS rol_portal,
       ISNULL((SELECT STRING_AGG(CONVERT(varchar, sc.FK_id_categoria), ',') FROM dbo.STAFF s
               JOIN dbo.STAFF_CATEGORIAS sc ON sc.FK_id_staff = s.PK_id_staff WHERE s.FK_id_usuario = u.PK_id_usuario), '-') AS categorias
FROM dbo.USUARIOS u JOIN dbo.PERSONAS p ON p.PK_id_persona = u.FK_id_persona ORDER BY u.PK_id_usuario;
