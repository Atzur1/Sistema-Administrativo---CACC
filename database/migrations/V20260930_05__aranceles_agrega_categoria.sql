-- Un arancel puede definirse por genero o por categoria (una u otra, nunca ambas).
-- Este script solo prepara la tabla: agrega la columna de categoria, permite que genero
-- quede vacio y quita las restricciones que asumian que genero era obligatorio. Las nuevas
-- reglas se crean en el script siguiente (una columna recien agregada no se puede
-- referenciar dentro del mismo lote). Los aranceles existentes quedan como "por genero".
IF COL_LENGTH(N'dbo.ARANCELES', N'FK_id_categoria') IS NULL
    ALTER TABLE dbo.ARANCELES ADD FK_id_categoria INT NULL;

IF EXISTS (SELECT 1 FROM sys.key_constraints
           WHERE name = N'UQ_ARANCELES_GENERO_FECHA' AND parent_object_id = OBJECT_ID(N'dbo.ARANCELES'))
    ALTER TABLE dbo.ARANCELES DROP CONSTRAINT UQ_ARANCELES_GENERO_FECHA;

IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE name = N'CK_ARANCELES_GENERO' AND parent_object_id = OBJECT_ID(N'dbo.ARANCELES'))
    ALTER TABLE dbo.ARANCELES DROP CONSTRAINT CK_ARANCELES_GENERO;

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID(N'dbo.ARANCELES') AND name = N'genero' AND is_nullable = 0)
    ALTER TABLE dbo.ARANCELES ALTER COLUMN genero VARCHAR(20) NULL;
