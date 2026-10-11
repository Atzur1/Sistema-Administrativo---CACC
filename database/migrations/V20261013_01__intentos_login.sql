-- Intentos fallidos de ingreso por cuenta (LoginThrottle). Antes vivian en memoria de la API y se perdian al
-- reiniciarla: bastaba con que la API se reiniciara para volver a probar contrasenias contra una cuenta.
-- La clave es un hash SHA-256 del identificador (DNI o correo normalizado): no se guarda el dato en claro, y
-- tampoco revela si la cuenta existe, porque se cuentan los fallos de cualquier identificador.
-- Las filas vencidas (fuera de la ventana de bloqueo) las borra la propia API al registrar fallos.
IF OBJECT_ID('dbo.INTENTOS_LOGIN', 'U') IS NULL
    CREATE TABLE dbo.INTENTOS_LOGIN
    (
        clave CHAR(64) NOT NULL CONSTRAINT PK_INTENTOS_LOGIN PRIMARY KEY,
        fallos INT NOT NULL,
        desde_utc DATETIME2(0) NOT NULL
    );

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_INTENTOS_LOGIN_desde' AND object_id = OBJECT_ID('dbo.INTENTOS_LOGIN'))
    CREATE INDEX IX_INTENTOS_LOGIN_desde ON dbo.INTENTOS_LOGIN (desde_utc);
