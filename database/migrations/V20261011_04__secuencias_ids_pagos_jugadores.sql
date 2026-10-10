-- Ids de PAGOS y JUGADORES con SEQUENCE en lugar de MAX(id) + 1.
--
-- Esas columnas no tienen IDENTITY, y la API calculaba el proximo id con MAX + 1 tomando un bloqueo
-- exclusivo de TODA la tabla (TABLOCKX) hasta el final de la transaccion: cada cobro frenaba a cualquier otro cobro, a la
-- generacion de cuotas y a las lecturas, y un id lo podia repetir otro sistema que tambien escribiera.
-- Una secuencia entrega numeros unicos sin bloquear la tabla. Se usa SEQUENCE y no IDENTITY porque
-- agregar IDENTITY a una columna existente obliga a recrear la tabla con todas sus claves foraneas.
--
-- NO CACHE: los numeros no se saltean si se reinicia el servidor (solo cuando una transaccion se revierte).
-- En una base con datos, la secuencia arranca despues del id mas alto. El valor inicial se calcula, y
-- ALTER SEQUENCE ... RESTART WITH solo acepta un literal: por eso ese paso usa sp_executesql.
IF OBJECT_ID(N'dbo.SEQ_PAGOS', N'SO') IS NULL
    CREATE SEQUENCE dbo.SEQ_PAGOS AS INT START WITH 1 INCREMENT BY 1 NO CACHE;

IF OBJECT_ID(N'dbo.SEQ_JUGADORES', N'SO') IS NULL
    CREATE SEQUENCE dbo.SEQ_JUGADORES AS INT START WITH 1 INCREMENT BY 1 NO CACHE;

DECLARE @siguientePago INT = (SELECT ISNULL(MAX(PK_id_pago), 0) + 1 FROM dbo.PAGOS WITH (TABLOCKX, HOLDLOCK));
DECLARE @siguienteJugador INT = (SELECT ISNULL(MAX(PK_id_jugador), 0) + 1 FROM dbo.JUGADORES WITH (TABLOCKX, HOLDLOCK));
DECLARE @reinicio NVARCHAR(200);

SET @reinicio = N'ALTER SEQUENCE dbo.SEQ_PAGOS RESTART WITH ' + CONVERT(NVARCHAR(20), @siguientePago) + N';';
EXEC sys.sp_executesql @reinicio;

SET @reinicio = N'ALTER SEQUENCE dbo.SEQ_JUGADORES RESTART WITH ' + CONVERT(NVARCHAR(20), @siguienteJugador) + N';';
EXEC sys.sp_executesql @reinicio;
