-- El genero se guarda siempre como "Masculino" o "Femenino" (HU-112).
--
-- Los triggers de genero obligatorio comparan sin distinguir mayusculas y sin espacios de los costados, asi que una
-- persona cargada como 'masculino ' era aceptada como jugador. El codigo, en cambio, compara exacto con "Masculino"
-- (arancel por genero, inscripcion solo masculina) y trataba a esa persona como si no fuera ni varon ni mujer.
--
-- En lugar de rechazar esas variantes (el Portal Deportivo y las importaciones pueden mandarlas), la base las
-- normaliza al guardar:
--  - Esta migracion corrige las filas que ya existen.
--  - El trigger TR_PERSONAS_GENERO_NORMALIZADO corrige las que lleguen (alta o cambio de genero), venga de donde venga.
-- Valores que no son ninguno de los dos generos ("No especificado", NULL) no se tocan: siguen igual que antes.
--
-- Es idempotente.
UPDATE dbo.PERSONAS
SET genero = CASE WHEN LTRIM(RTRIM(genero)) = N'Masculino' THEN N'Masculino' ELSE N'Femenino' END
WHERE LTRIM(RTRIM(genero)) IN (N'Masculino', N'Femenino')
  AND genero COLLATE Latin1_General_100_BIN2 NOT IN (N'Masculino', N'Femenino');

EXEC sys.sp_executesql N'
CREATE OR ALTER TRIGGER dbo.TR_PERSONAS_GENERO_NORMALIZADO ON dbo.PERSONAS AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted
        WHERE LTRIM(RTRIM(genero)) IN (N''Masculino'', N''Femenino'')
          AND genero COLLATE Latin1_General_100_BIN2 NOT IN (N''Masculino'', N''Femenino''))
        UPDATE p
        SET genero = CASE WHEN LTRIM(RTRIM(i.genero)) = N''Masculino'' THEN N''Masculino'' ELSE N''Femenino'' END
        FROM dbo.PERSONAS p
        JOIN inserted i ON i.PK_id_persona = p.PK_id_persona
        WHERE LTRIM(RTRIM(i.genero)) IN (N''Masculino'', N''Femenino'')
          AND i.genero COLLATE Latin1_General_100_BIN2 NOT IN (N''Masculino'', N''Femenino'');
END;';
