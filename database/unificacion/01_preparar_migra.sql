/* =====================================================================================
   HU-053 - Paso 1 de la unificacion: esquema de trabajo "migra" en la base DESTINO.
   Se ejecuta sobre una base recien creada con esquema/ClubCamioneros_unificada_crear_base.sql.
   Es idempotente: si los objetos ya existen, no los toca.

   Tablas:
     migra.ejecucion          una fila por intento de importacion (OK / BLOQUEADA / FALLO)
     migra.persona_origen     personas de A y B normalizadas (DNI limpio, dependientes); la recalcula el paso 2
     migra.jugador_origen     jugadores de A y B con el grupo de persona al que pertenecen; la recalcula el paso 2
     migra.persona_plan       a que persona unificada va cada persona de origen; la recalcula el paso 2
     migra.conflicto_dni      grupos de DNI que no se pueden unificar solos (los calcula el paso 2)
     migra.resolucion_dni     decision HUMANA para cada conflicto (la carga una persona, nunca el script)
     migra.mapa               id de origen -> id unificado, por entidad (OldIdB -> UnifiedId)
     migra.transformacion     cada dato que se completo, infirio o descarto, con su valor original
     migra.conteo             conteos de origen y destino por entidad
     migra.validacion         resultado de cada verificacion del paso 4
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

-- Solo sobre una base con el esquema unificado: nunca en master ni en una base de origen.
IF DB_NAME() IN (N'master', N'model', N'msdb', N'tempdb')
   OR OBJECT_ID(N'dbo.PERSONAS', N'U') IS NULL OR OBJECT_ID(N'dbo.USUARIOS', N'U') IS NULL OR OBJECT_ID(N'dbo.SEQ_PAGOS', N'SO') IS NULL
    THROW 53050, N'Esta no es una base con el esquema unificado: el esquema migra va solo en la base destino de la prueba.', 1;
GO

IF SCHEMA_ID(N'migra') IS NULL
    EXEC (N'CREATE SCHEMA migra AUTHORIZATION dbo;');
GO

IF OBJECT_ID(N'migra.ejecucion', N'U') IS NULL
CREATE TABLE migra.ejecucion (
    PK_id_ejecucion int IDENTITY(1,1) NOT NULL CONSTRAINT PK_migra_ejecucion PRIMARY KEY,
    base_a sysname NOT NULL,
    base_b sysname NOT NULL,
    inicio_utc datetime2(0) NOT NULL CONSTRAINT DF_migra_ejecucion_inicio DEFAULT (SYSUTCDATETIME()),
    fin_utc datetime2(0) NULL,
    estado varchar(10) NOT NULL CONSTRAINT CK_migra_ejecucion_estado CHECK (estado IN ('EN_CURSO', 'OK', 'BLOQUEADA', 'FALLO')),
    detalle nvarchar(4000) NULL
);
GO

IF OBJECT_ID(N'migra.persona_origen', N'U') IS NULL
CREATE TABLE migra.persona_origen (
    origen char(1) NOT NULL CONSTRAINT CK_migra_persona_origen_origen CHECK (origen IN ('A', 'B')),
    id_origen int NOT NULL,
    dni_original varchar(20) NULL,
    dni varchar(20) NULL,              -- normalizado y valido (6 a 9 digitos); NULL si falta o es invalido
    grupo varchar(40) NOT NULL,        -- el DNI, o SIN_DNI:<origen>:<id> si no tiene uno valido
    nombre varchar(100) NULL,
    apellido varchar(100) NULL,
    genero varchar(50) NULL,
    fecha_nacimiento date NULL,
    domicilio varchar(255) NULL,
    n_jugadores int NOT NULL,
    n_usuarios int NOT NULL,
    n_responsables int NOT NULL,
    CONSTRAINT PK_migra_persona_origen PRIMARY KEY (origen, id_origen)
);
GO

IF OBJECT_ID(N'migra.jugador_origen', N'U') IS NULL
CREATE TABLE migra.jugador_origen (
    origen char(1) NOT NULL,
    id_origen int NOT NULL,
    id_persona_origen int NULL,
    grupo varchar(40) NULL,            -- grupo de la persona; NULL = jugador sin persona (no se puede importar)
    FK_id_categoria int NULL,
    CONSTRAINT PK_migra_jugador_origen PRIMARY KEY (origen, id_origen)
);
GO

IF OBJECT_ID(N'migra.persona_plan', N'U') IS NULL
CREATE TABLE migra.persona_plan (
    origen char(1) NOT NULL,
    id_origen int NOT NULL,
    grupo varchar(40) NOT NULL,
    es_canonica bit NOT NULL,
    regla varchar(40) NOT NULL,        -- UNICA / MISMO_DNI_ENTRE_SISTEMAS / DUPLICADO_IDENTICO / RESOLUCION_HUMANA
    CONSTRAINT PK_migra_persona_plan PRIMARY KEY (origen, id_origen)
);
GO

IF OBJECT_ID(N'migra.conflicto_dni', N'U') IS NULL
CREATE TABLE migra.conflicto_dni (
    grupo varchar(40) NOT NULL,
    origen char(1) NOT NULL,
    id_persona_origen int NOT NULL,
    nombre varchar(100) NULL,
    apellido varchar(100) NULL,
    fecha_nacimiento date NULL,
    dependientes int NOT NULL,
    motivo nvarchar(1000) NOT NULL,
    CONSTRAINT PK_migra_conflicto_dni PRIMARY KEY (grupo, origen, id_persona_origen)
);
GO

-- La carga una PERSONA tras revisar el conflicto. Por cada fila del grupo en conflicto: CANONICA (exactamente una
-- por grupo: su persona y sus datos quedan) o FUSIONAR (se une a la canonica; sus dependientes pasan a ella).
-- es_prueba = 1 marca decisiones cargadas solo para probar el mecanismo: el paso 4 las informa como tales.
IF OBJECT_ID(N'migra.resolucion_dni', N'U') IS NULL
CREATE TABLE migra.resolucion_dni (
    grupo varchar(40) NOT NULL,
    origen char(1) NOT NULL,
    id_persona_origen int NOT NULL,
    accion varchar(10) NOT NULL CONSTRAINT CK_migra_resolucion_accion CHECK (accion IN ('CANONICA', 'FUSIONAR')),
    decidido_por nvarchar(200) NOT NULL,
    motivo nvarchar(1000) NOT NULL,
    es_prueba bit NOT NULL,
    registrado_utc datetime2(0) NOT NULL CONSTRAINT DF_migra_resolucion_registrado DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_migra_resolucion_dni PRIMARY KEY (grupo, origen, id_persona_origen)
);
GO

IF OBJECT_ID(N'migra.mapa', N'U') IS NULL
CREATE TABLE migra.mapa (
    entidad sysname NOT NULL,
    origen char(1) NOT NULL,
    id_origen int NOT NULL,
    id_unificado int NOT NULL,
    regla varchar(40) NOT NULL,        -- ID_CONSERVADO / ID_NUEVO / FUSIONADO
    CONSTRAINT PK_migra_mapa PRIMARY KEY (entidad, origen, id_origen)
);
GO

IF OBJECT_ID(N'migra.transformacion', N'U') IS NULL
CREATE TABLE migra.transformacion (
    PK_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_migra_transformacion PRIMARY KEY,
    entidad sysname NOT NULL,
    origen char(1) NOT NULL,
    id_origen int NOT NULL,
    columna sysname NOT NULL,
    valor_origen nvarchar(4000) NULL,
    valor_destino nvarchar(4000) NULL,
    regla varchar(60) NOT NULL
);
GO

IF OBJECT_ID(N'migra.conteo', N'U') IS NULL
CREATE TABLE migra.conteo (
    etapa varchar(10) NOT NULL CONSTRAINT CK_migra_conteo_etapa CHECK (etapa IN ('ORIGEN', 'DESTINO')),
    entidad sysname NOT NULL,
    origen char(1) NOT NULL,           -- A, B o U (unificada)
    cantidad int NOT NULL,
    CONSTRAINT PK_migra_conteo PRIMARY KEY (etapa, entidad, origen)
);
GO

IF OBJECT_ID(N'migra.validacion', N'U') IS NULL
CREATE TABLE migra.validacion (
    PK_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_migra_validacion PRIMARY KEY,
    grupo varchar(20) NOT NULL,
    verificacion nvarchar(300) NOT NULL,
    esperado bigint NULL,
    obtenido bigint NULL,
    resultado varchar(10) NOT NULL CONSTRAINT CK_migra_validacion_resultado CHECK (resultado IN ('PASS', 'FAIL', 'INFO')),
    detalle nvarchar(2000) NULL
);
GO

PRINT N'Esquema migra listo.';
GO
