/*
  Normaliza nombre y apellido de PERSONAS: la primera letra de cada palabra en mayuscula, el resto en minuscula
  ("JUAN CARLOS perez" -> "Juan Carlos Perez"). Misma regla que EntityLibrary.NombresPropios (y que aplica el Portal
  Deportivo): de, del, la, las, los, y, e van en minuscula salvo al principio; guiones y apostrofos reinician la mayuscula;
  "Mc" lleva mayuscula tambien en la letra siguiente.

  Es un script de UNA SOLA VEZ para datos cargados antes de que los portales normalizaran al guardar. No es una migracion:
  no se ejecuta solo.

  Uso:
    1. Cambiar la base en la linea USE.
    2. Correrlo tal cual: muestra que cambiaria (no modifica nada).
    3. Si esta bien, poner @aplicar = 1 y correrlo de nuevo.
  Los cambios quedan en la auditoria a nombre de @idUsuario (poner el id de usuario de quien lo corre).
*/
USE [ClubCamionerosUnificadaV2];
GO
SET NOCOUNT ON;
DECLARE @aplicar BIT = 0;
DECLARE @idUsuario INT = 1;

IF OBJECT_ID('tempdb..#cambios') IS NOT NULL DROP TABLE #cambios;
CREATE TABLE #cambios (id INT, campo VARCHAR(10), actual NVARCHAR(200), nuevo NVARCHAR(200));

DECLARE @id INT, @campo VARCHAR(10), @texto NVARCHAR(200);
DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT PK_id_persona, 'nombre', nombre FROM dbo.PERSONAS WHERE nombre IS NOT NULL
    UNION ALL
    SELECT PK_id_persona, 'apellido', apellido FROM dbo.PERSONAS WHERE apellido IS NOT NULL;
OPEN cur;
FETCH NEXT FROM cur INTO @id, @campo, @texto;
WHILE @@FETCH_STATUS = 0
BEGIN
    DECLARE @salida NVARCHAR(200) = N'', @palabra NVARCHAR(200), @i INT, @c NCHAR(1), @mayus BIT, @n INT = 0;
    DECLARE @pendiente NVARCHAR(200) = LTRIM(RTRIM(@texto));
    -- colapsa espacios repetidos
    WHILE CHARINDEX(N'  ', @pendiente) > 0 SET @pendiente = REPLACE(@pendiente, N'  ', N' ');
    SET @pendiente = @pendiente + N' ';
    WHILE LEN(@pendiente) > 0
    BEGIN
        SET @palabra = LOWER(LEFT(@pendiente, CHARINDEX(N' ', @pendiente) - 1));
        SET @pendiente = SUBSTRING(@pendiente, CHARINDEX(N' ', @pendiente) + 1, 200);
        IF @n > 0 AND @palabra IN (N'de', N'del', N'la', N'las', N'los', N'y', N'e')
            SET @salida = @salida + N' ' + @palabra;
        ELSE
        BEGIN
            DECLARE @cap NVARCHAR(200) = N''; SET @i = 1; SET @mayus = 1;
            WHILE @i <= LEN(@palabra)
            BEGIN
                SET @c = SUBSTRING(@palabra, @i, 1);
                SET @cap = @cap + CASE WHEN @mayus = 1 THEN UPPER(@c) ELSE @c END;
                SET @mayus = CASE WHEN @c IN (N'-', N'''', NCHAR(8217)) THEN 1 ELSE 0 END;
                SET @i += 1;
            END
            IF LEN(@cap) > 2 AND LEFT(@cap, 2) = N'Mc' AND SUBSTRING(@cap, 3, 1) LIKE N'[A-Za-z]'
                SET @cap = N'Mc' + UPPER(SUBSTRING(@cap, 3, 1)) + SUBSTRING(@cap, 4, 200);
            SET @salida = @salida + CASE WHEN @n > 0 THEN N' ' ELSE N'' END + @cap;
        END
        SET @n += 1;
    END
    -- comparacion sensible a mayusculas: si solo cambia la capitalizacion igual se detecta
    IF @salida COLLATE Latin1_General_BIN2 <> @texto COLLATE Latin1_General_BIN2
        INSERT #cambios VALUES (@id, @campo, @texto, @salida);
    FETCH NEXT FROM cur INTO @id, @campo, @texto;
END
CLOSE cur; DEALLOCATE cur;

SELECT id AS PK_id_persona, campo, actual, nuevo FROM #cambios ORDER BY id, campo;
DECLARE @cantidad INT = (SELECT COUNT(*) FROM #cambios);
PRINT CONCAT(N'Valores a cambiar: ', @cantidad);

IF @aplicar = 1
BEGIN
    EXEC sys.sp_set_session_context @key = N'idUsuario', @value = @idUsuario;
    BEGIN TRAN;
    UPDATE p SET nombre = c.nuevo FROM dbo.PERSONAS p JOIN #cambios c ON c.id = p.PK_id_persona AND c.campo = 'nombre';
    UPDATE p SET apellido = c.nuevo FROM dbo.PERSONAS p JOIN #cambios c ON c.id = p.PK_id_persona AND c.campo = 'apellido';
    COMMIT;
    PRINT N'Aplicado.';
END
ELSE PRINT N'Simulacion: no se modifico nada. Poner @aplicar = 1 para aplicar.';
