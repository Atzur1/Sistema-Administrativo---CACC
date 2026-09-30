-- Aranceles por genero y fecha de vigencia. Los importes se configuran desde
-- el sistema: no se inventan valores ni se alteran los datos existentes.
IF OBJECT_ID(N'dbo.ARANCELES', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ARANCELES (
        PK_id_arancel INT IDENTITY(1,1) PRIMARY KEY,
        genero VARCHAR(20) NOT NULL,
        monto DECIMAL(18,2) NOT NULL,
        vigente_desde DATE NOT NULL,
        CONSTRAINT UQ_ARANCELES_GENERO_FECHA UNIQUE (genero, vigente_desde),
        CONSTRAINT CK_ARANCELES_GENERO CHECK (genero = 'Masculino' OR genero = 'Femenino'),
        CONSTRAINT CK_ARANCELES_MONTO CHECK (monto > 0)
    );
END;
