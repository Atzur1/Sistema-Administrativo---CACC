-- HU-056: alta de jugadores desde el Portal Deportivo a traves de la API de integracion.
--
-- Hasta ahora el Deportivo grababa la persona y el jugador directo en la base compartida y despues avisaba para que se
-- generaran los cargos. Con HU-056 el alta la hace el Administrativo (POST api/integracion/v1/jugadores), en una sola
-- transaccion y con una sola cola de escritura de cobros, de modo que una persona nunca termina con dos jugadores ni con
-- cargos repetidos. Esta migracion agrega las dos redes de seguridad que necesita la base:
--
--  1. UX_JUGADORES_persona: una persona tiene A LO SUMO un jugador. La API ya lo respeta (serializa las altas y busca antes de
--     insertar); el indice lo garantiza tambien frente a quien escriba directo en la base. Es filtrado (FK_id_persona acepta
--     NULL y varios NULL no son un duplicado), igual que UX_PERSONAS_dni.
--     Si ya hubiera una persona con mas de un jugador, la migracion se detiene: decidir cual de los dos es el verdadero (o
--     unificarlos, con sus pagos) es una tarea humana, no de un script. Se corrige a mano y se vuelve a aplicar.
--     IX_JUGADORES_persona (no unico, misma columna) queda: el indice nuevo lo cubre, pero la base es compartida con el Portal
--     Deportivo y borrarlo no aporta nada con unos cientos de filas. Se puede quitar mas adelante sin riesgo.
--
--  2. INTEGRACION_RECHAZOS: cada pedido de alta o de cambio de categoria que la integracion rechaza por una regla (datos
--     invalidos, jugador dado de baja, genero que no coincide...) deja una fila, para que el equipo del Deportivo y el del
--     Administrativo puedan ver QUE se rechazo y por que sin leer logs. Guarda solo ids internos y el motivo en texto: nunca el
--     DNI ni nombres. Es un registro de solo agregar: el usuario de la API puede insertar y leer, no modificar ni borrar (ver
--     database/produccion/03_permisos_usuario_api.sql).
--
-- Es idempotente. Va en un solo lote: el SQL dinamico evita depender de que la tabla exista al compilar el lote.

IF EXISTS (SELECT 1 FROM dbo.JUGADORES WHERE FK_id_persona IS NOT NULL GROUP BY FK_id_persona HAVING COUNT(*) > 1)
    THROW 51300, 'HU-056: hay personas con mas de un jugador en JUGADORES. Resolverlo a mano antes de aplicar esta migracion (SELECT FK_id_persona, COUNT(*) FROM dbo.JUGADORES GROUP BY FK_id_persona HAVING COUNT(*) > 1).', 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.JUGADORES') AND name = N'UX_JUGADORES_persona')
    EXEC sys.sp_executesql N'
CREATE UNIQUE NONCLUSTERED INDEX UX_JUGADORES_persona
ON dbo.JUGADORES (FK_id_persona)
WHERE FK_id_persona IS NOT NULL;';

IF OBJECT_ID(N'dbo.INTEGRACION_RECHAZOS', N'U') IS NULL
    EXEC sys.sp_executesql N'
CREATE TABLE dbo.INTEGRACION_RECHAZOS (
    PK_id_rechazo int IDENTITY(1,1) NOT NULL,
    fecha_utc datetime2 NOT NULL CONSTRAINT DF_INTEGRACION_RECHAZOS_fecha DEFAULT (sysutcdatetime()),
    origen_sistema nvarchar(50) NULL,
    operacion nvarchar(50) NOT NULL,
    id_usuario int NULL,
    id_persona int NULL,
    id_jugador int NULL,
    error_code varchar(30) NOT NULL,
    motivo nvarchar(400) NOT NULL,
    correlacion nvarchar(100) NULL,
    CONSTRAINT PK_INTEGRACION_RECHAZOS PRIMARY KEY CLUSTERED (PK_id_rechazo)
);';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.INTEGRACION_RECHAZOS') AND name = N'IX_INTEGRACION_RECHAZOS_fecha')
    EXEC sys.sp_executesql N'CREATE NONCLUSTERED INDEX IX_INTEGRACION_RECHAZOS_fecha ON dbo.INTEGRACION_RECHAZOS (fecha_utc);';
