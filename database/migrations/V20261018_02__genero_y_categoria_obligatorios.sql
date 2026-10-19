-- Genero y categoria obligatorios para todo jugador.
--
-- El arancel que se le cobra a un jugador sale de su categoria (si esa categoria tiene arancel propio) o, si no, de su
-- genero; la inscripcion depende del genero. Un jugador sin alguno de los dos datos no se puede facturar y, sin
-- categoria, ademas desaparece de Deudas y Morosidad y de los listados de Cuotas y Pagos. En lugar de avisar despues,
-- la base impide cargarlo incompleto, venga el alta de donde venga (Portal Administrativo, Portal Deportivo o una importacion).
--
--  - JUGADORES.FK_id_categoria pasa a ser obligatoria.
--  - Un jugador necesita una persona con genero Masculino o Femenino (trigger TR_JUGADORES_GENERO_OBLIGATORIO), y esa
--    persona no puede quedar sin genero mientras sea jugador (trigger TR_PERSONAS_GENERO_DE_JUGADOR).
--  - PERSONAS.genero sigue siendo opcional para el resto de las personas (personal, tutores): el Portal Deportivo carga
--    al personal con "No especificado", asi que la restriccion alcanza solo a quienes son jugadores.
--
-- Es idempotente. Si ya hay jugadores que no cumplen, se detiene con un mensaje: hay que completar esos datos primero.
-- Los triggers se crean con SQL dinamico porque CREATE TRIGGER debe ser la primera sentencia de su lote.
IF EXISTS (SELECT 1 FROM dbo.JUGADORES WHERE FK_id_categoria IS NULL)
    THROW 51000, N'Hay jugadores sin categoria. Completar la categoria de cada uno antes de aplicar esta migracion.', 1;

IF EXISTS (SELECT 1 FROM dbo.JUGADORES j LEFT JOIN dbo.PERSONAS p ON p.PK_id_persona = j.FK_id_persona
           WHERE p.PK_id_persona IS NULL OR p.genero IS NULL OR LTRIM(RTRIM(p.genero)) NOT IN (N'Masculino', N'Femenino'))
    THROW 51001, N'Hay jugadores cuya persona no tiene genero Masculino o Femenino. Completar el genero de cada uno antes de aplicar esta migracion.', 1;

-- El indice sobre la columna impide cambiarle la nulabilidad: se saca y se vuelve a crear igual.
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID(N'dbo.JUGADORES') AND name = N'FK_id_categoria' AND is_nullable = 1)
    EXEC sys.sp_executesql N'
        DROP INDEX IF EXISTS IX_JUGADORES_categoria ON dbo.JUGADORES;
        ALTER TABLE dbo.JUGADORES ALTER COLUMN FK_id_categoria INT NOT NULL;
        CREATE NONCLUSTERED INDEX IX_JUGADORES_categoria ON dbo.JUGADORES (FK_id_categoria);';

EXEC sys.sp_executesql N'
CREATE OR ALTER TRIGGER dbo.TR_JUGADORES_GENERO_OBLIGATORIO ON dbo.JUGADORES AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(FK_id_persona) AND EXISTS (
        SELECT 1 FROM inserted i LEFT JOIN dbo.PERSONAS p ON p.PK_id_persona = i.FK_id_persona
        WHERE p.PK_id_persona IS NULL OR p.genero IS NULL OR LTRIM(RTRIM(p.genero)) NOT IN (N''Masculino'', N''Femenino''))
        THROW 51010, N''Un jugador necesita su persona con genero Masculino o Femenino: sin ese dato no se sabe que arancel cobrarle.'', 1;
END;';

EXEC sys.sp_executesql N'
CREATE OR ALTER TRIGGER dbo.TR_PERSONAS_GENERO_DE_JUGADOR ON dbo.PERSONAS AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(genero) AND EXISTS (
        SELECT 1 FROM inserted i JOIN dbo.JUGADORES j ON j.FK_id_persona = i.PK_id_persona
        WHERE i.genero IS NULL OR LTRIM(RTRIM(i.genero)) NOT IN (N''Masculino'', N''Femenino''))
        THROW 51011, N''No se puede dejar sin genero (o con uno invalido) a una persona que es jugador: sin ese dato no se sabe que arancel cobrarle.'', 1;
END;';
