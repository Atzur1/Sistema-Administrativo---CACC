-- 5 jugadores de prueba, sin ningún pago cargado (no aparecen en "Pendientes de cobro" ni
-- "Últimos pagos" hasta que se les registre algo). Apellido "PRUEBA" para todos, para poder
-- encontrarlos fácil en el buscador de "Cuotas y Pagos" tipeando "prueba".
-- Nombres sin tildes a propósito (mismo motivo que el seed de usuarios: sqlcmd puede
-- corromper acentos en VARCHAR según la code page de la consola).
-- DNIs en el rango 99000001-99000005: fuera del rango real (40-55 millones) para que no
-- colisionen ni se confundan con los 577 jugadores reales.

USE ClubCamionerosPRUEBA;
GO

IF EXISTS (SELECT 1 FROM PERSONA WHERE Dni = '99000001')
BEGIN
    PRINT 'Los jugadores de prueba ya existen, no se vuelve a ejecutar.';
    RETURN;
END
GO

DECLARE @idJugadorBase INT = (SELECT ISNULL(MAX(PK_id_jugador), 0) FROM JUGADORES);
DECLARE @idPersona INT;

INSERT INTO PERSONA (genero, fecha_de_nacimiento, Dni, nombre, apellido)
VALUES ('Masculino', '2012-03-10', '99000001', 'NICOLAS', 'PRUEBA');
SET @idPersona = SCOPE_IDENTITY();
INSERT INTO JUGADORES (PK_id_jugador, FK_id_persona, FK_id_categoria)
VALUES (@idJugadorBase + 1, @idPersona, 1);

INSERT INTO PERSONA (genero, fecha_de_nacimiento, Dni, nombre, apellido)
VALUES ('Femenino', '2013-06-22', '99000002', 'SOFIA', 'PRUEBA');
SET @idPersona = SCOPE_IDENTITY();
INSERT INTO JUGADORES (PK_id_jugador, FK_id_persona, FK_id_categoria)
VALUES (@idJugadorBase + 2, @idPersona, 1);

INSERT INTO PERSONA (genero, fecha_de_nacimiento, Dni, nombre, apellido)
VALUES ('Masculino', '2011-11-05', '99000003', 'MATIAS', 'PRUEBA');
SET @idPersona = SCOPE_IDENTITY();
INSERT INTO JUGADORES (PK_id_jugador, FK_id_persona, FK_id_categoria)
VALUES (@idJugadorBase + 3, @idPersona, 1);

INSERT INTO PERSONA (genero, fecha_de_nacimiento, Dni, nombre, apellido)
VALUES ('Femenino', '2012-09-14', '99000004', 'VALENTINA', 'PRUEBA');
SET @idPersona = SCOPE_IDENTITY();
INSERT INTO JUGADORES (PK_id_jugador, FK_id_persona, FK_id_categoria)
VALUES (@idJugadorBase + 4, @idPersona, 1);

INSERT INTO PERSONA (genero, fecha_de_nacimiento, Dni, nombre, apellido)
VALUES ('Masculino', '2013-01-30', '99000005', 'SANTIAGO', 'PRUEBA');
SET @idPersona = SCOPE_IDENTITY();
INSERT INTO JUGADORES (PK_id_jugador, FK_id_persona, FK_id_categoria)
VALUES (@idJugadorBase + 5, @idPersona, 1);
GO

SELECT j.PK_id_jugador, p.nombre, p.apellido, p.Dni, c.nombre_categoria
FROM JUGADORES j
JOIN PERSONA p ON j.FK_id_persona = p.PK_id_persona
JOIN CATEGORIAS c ON j.FK_id_categoria = c.PK_id_categoria
WHERE p.Dni LIKE '99000%'
ORDER BY j.PK_id_jugador;
