-- Reglas del arancel por genero o por categoria:
--  * Cada fila tiene genero o categoria, nunca las dos ni ninguna.
--  * La categoria debe existir en CATEGORIAS.
--  * No se repite el mismo destino (un genero o una categoria) con la misma fecha de vigencia.
-- Los indices unicos son filtrados para que las filas con valor vacio no choquen entre si.
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = N'CK_ARANCELES_GENERO' AND parent_object_id = OBJECT_ID(N'dbo.ARANCELES'))
    ALTER TABLE dbo.ARANCELES ADD CONSTRAINT CK_ARANCELES_GENERO
        CHECK (genero IS NULL OR genero = 'Masculino' OR genero = 'Femenino');

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = N'CK_ARANCELES_GENERO_O_CATEGORIA' AND parent_object_id = OBJECT_ID(N'dbo.ARANCELES'))
    ALTER TABLE dbo.ARANCELES ADD CONSTRAINT CK_ARANCELES_GENERO_O_CATEGORIA
        CHECK ((genero IS NOT NULL AND FK_id_categoria IS NULL)
            OR (genero IS NULL AND FK_id_categoria IS NOT NULL));

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys
               WHERE name = N'FK_ARANCELES_CATEGORIAS' AND parent_object_id = OBJECT_ID(N'dbo.ARANCELES'))
    ALTER TABLE dbo.ARANCELES ADD CONSTRAINT FK_ARANCELES_CATEGORIAS
        FOREIGN KEY (FK_id_categoria) REFERENCES dbo.CATEGORIAS (PK_id_categoria);

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'UX_ARANCELES_GENERO_FECHA' AND object_id = OBJECT_ID(N'dbo.ARANCELES'))
    CREATE UNIQUE INDEX UX_ARANCELES_GENERO_FECHA
        ON dbo.ARANCELES (genero, vigente_desde) WHERE genero IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'UX_ARANCELES_CATEGORIA_FECHA' AND object_id = OBJECT_ID(N'dbo.ARANCELES'))
    CREATE UNIQUE INDEX UX_ARANCELES_CATEGORIA_FECHA
        ON dbo.ARANCELES (FK_id_categoria, vigente_desde) WHERE FK_id_categoria IS NOT NULL;
