-- 5 usuarios de muestra para probar el login además del admin ya existente (admin@cacc.com).
-- Mismo rol (Administrador General, PK_id_rol = 1) porque hoy es el único rol funcional:
-- el sistema todavía no filtra portales por rol (ver AGENTS.md, "gotchas").
-- Contraseña en texto plano a propósito, igual que el resto de USUARIO: es deuda técnica
-- conocida de AuthDao (compara texto plano, no hashea), no algo que este script deba resolver.
-- Nombres sin tildes a propósito: sqlcmd puede reinterpretar mal UTF-8 en columnas VARCHAR
-- según la code page de la consola y corromper los acentos al insertar.

USE ClubCamionerosPRUEBA;
GO

IF EXISTS (SELECT 1 FROM USUARIO WHERE email = 'maria.gonzalez@cacc.com')
BEGIN
    PRINT 'Los usuarios de muestra ya existen, no se vuelve a ejecutar.';
    RETURN;
END
GO

DECLARE @idUsuarioBase INT = (SELECT ISNULL(MAX(PK_id_usuario), 0) FROM USUARIO);
DECLARE @idPersona INT;

INSERT INTO PERSONA (genero, fecha_de_nacimiento, Dni, nombre, apellido)
VALUES ('Femenino', '1990-05-14', '30111222', 'MARIA', 'GONZALEZ');
SET @idPersona = SCOPE_IDENTITY();
INSERT INTO USUARIO (PK_id_usuario, FK_id_persona, FK_id_rol, email, contrasenia, activo)
VALUES (@idUsuarioBase + 1, @idPersona, 1, 'maria.gonzalez@cacc.com', 'cacc123', 1);

INSERT INTO PERSONA (genero, fecha_de_nacimiento, Dni, nombre, apellido)
VALUES ('Masculino', '1985-11-02', '30222333', 'CARLOS', 'FERNANDEZ');
SET @idPersona = SCOPE_IDENTITY();
INSERT INTO USUARIO (PK_id_usuario, FK_id_persona, FK_id_rol, email, contrasenia, activo)
VALUES (@idUsuarioBase + 2, @idPersona, 1, 'carlos.fernandez@cacc.com', 'cacc123', 1);

INSERT INTO PERSONA (genero, fecha_de_nacimiento, Dni, nombre, apellido)
VALUES ('Femenino', '1993-03-21', '30333444', 'LUCIA', 'MARTINEZ');
SET @idPersona = SCOPE_IDENTITY();
INSERT INTO USUARIO (PK_id_usuario, FK_id_persona, FK_id_rol, email, contrasenia, activo)
VALUES (@idUsuarioBase + 3, @idPersona, 1, 'lucia.martinez@cacc.com', 'cacc123', 1);

INSERT INTO PERSONA (genero, fecha_de_nacimiento, Dni, nombre, apellido)
VALUES ('Masculino', '1988-07-09', '30444555', 'DIEGO', 'ROMERO');
SET @idPersona = SCOPE_IDENTITY();
INSERT INTO USUARIO (PK_id_usuario, FK_id_persona, FK_id_rol, email, contrasenia, activo)
VALUES (@idUsuarioBase + 4, @idPersona, 1, 'diego.romero@cacc.com', 'cacc123', 1);

INSERT INTO PERSONA (genero, fecha_de_nacimiento, Dni, nombre, apellido)
VALUES ('Femenino', '1991-09-30', '30555666', 'SOFIA', 'ACOSTA');
SET @idPersona = SCOPE_IDENTITY();
INSERT INTO USUARIO (PK_id_usuario, FK_id_persona, FK_id_rol, email, contrasenia, activo)
VALUES (@idUsuarioBase + 5, @idPersona, 1, 'sofia.acosta@cacc.com', 'cacc123', 1);
GO

SELECT u.PK_id_usuario, p.nombre, p.apellido, u.email, u.contrasenia, r.nombre_rol
FROM USUARIO u
JOIN PERSONA p ON u.FK_id_persona = p.PK_id_persona
JOIN ROLES r ON u.FK_id_rol = r.PK_id_rol
ORDER BY u.PK_id_usuario;
