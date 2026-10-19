-- Arancel de inscripcion por genero.
--
-- Hasta ahora ARANCELES_INSCRIPCION era un unico arancel (el de la rama masculina). Desde esta migracion cada genero
-- tiene su propia historia de aranceles: las mujeres hoy no pagan inscripcion (no tienen arancel o tienen uno de $0),
-- y el dia que el club decida cobrarla alcanza con programar un arancel femenino, sin tocar codigo.
--
--  - genero pasa a ser obligatorio (Masculino o Femenino). Los aranceles que ya existian eran masculinos.
--  - El indice unico pasa a ser (genero, vigente_desde): el mismo dia puede empezar un arancel de cada genero.
--  - El monto puede ser 0 (ese genero no paga desde esa fecha).
--
-- Es idempotente y sirve para tres puntos de partida: una base sin la columna genero, una con la columna opcional
-- (la que crea el script de la base unica anterior a esta migracion) y una que ya esta en su forma final.
-- Las sentencias que nombran la columna genero van como SQL dinamico: la columna puede no existir al compilar el lote.
IF COL_LENGTH(N'dbo.ARANCELES_INSCRIPCION', N'genero') IS NULL
    EXEC sys.sp_executesql N'ALTER TABLE dbo.ARANCELES_INSCRIPCION ADD genero VARCHAR(20) NULL;';

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID(N'dbo.ARANCELES_INSCRIPCION') AND name = N'genero' AND is_nullable = 1)
BEGIN
    EXEC sys.sp_executesql N'
        DROP INDEX IF EXISTS UX_ARANCELES_INSCRIPCION_GENERAL_FECHA ON dbo.ARANCELES_INSCRIPCION;
        DROP INDEX IF EXISTS UX_ARANCELES_INSCRIPCION_GENERO_FECHA ON dbo.ARANCELES_INSCRIPCION;
        UPDATE dbo.ARANCELES_INSCRIPCION SET genero = ''Masculino'' WHERE genero IS NULL;
        ALTER TABLE dbo.ARANCELES_INSCRIPCION ALTER COLUMN genero VARCHAR(20) NOT NULL;';
END

-- Bases anteriores: restriccion unica solo por fecha (ya no sirve: dos generos pueden empezar el mismo dia).
IF EXISTS (SELECT 1 FROM sys.key_constraints
           WHERE name = N'UQ_ARANCELES_INSCRIPCION_FECHA' AND parent_object_id = OBJECT_ID(N'dbo.ARANCELES_INSCRIPCION'))
    ALTER TABLE dbo.ARANCELES_INSCRIPCION DROP CONSTRAINT UQ_ARANCELES_INSCRIPCION_FECHA;

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'UX_ARANCELES_INSCRIPCION_GENERO_FECHA' AND object_id = OBJECT_ID(N'dbo.ARANCELES_INSCRIPCION'))
    EXEC sys.sp_executesql N'CREATE UNIQUE NONCLUSTERED INDEX UX_ARANCELES_INSCRIPCION_GENERO_FECHA ON dbo.ARANCELES_INSCRIPCION (genero, vigente_desde);';

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = N'CK_ARANCELES_INSCRIPCION_GENERO' AND parent_object_id = OBJECT_ID(N'dbo.ARANCELES_INSCRIPCION'))
    EXEC sys.sp_executesql N'ALTER TABLE dbo.ARANCELES_INSCRIPCION ADD CONSTRAINT CK_ARANCELES_INSCRIPCION_GENERO CHECK (genero IN (''Masculino'', ''Femenino''));';

-- El monto antes tenia que ser mayor a cero; ahora 0 significa "no se cobra inscripcion".
IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE name = N'CK_ARANCELES_INSCRIPCION_MONTO' AND parent_object_id = OBJECT_ID(N'dbo.ARANCELES_INSCRIPCION')
             AND definition NOT LIKE N'%>=%')
BEGIN
    ALTER TABLE dbo.ARANCELES_INSCRIPCION DROP CONSTRAINT CK_ARANCELES_INSCRIPCION_MONTO;
    ALTER TABLE dbo.ARANCELES_INSCRIPCION ADD CONSTRAINT CK_ARANCELES_INSCRIPCION_MONTO CHECK (monto >= 0);
END
