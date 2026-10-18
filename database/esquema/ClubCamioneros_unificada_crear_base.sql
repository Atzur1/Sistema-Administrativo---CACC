/* =====================================================================================
   Club Camioneros - Base de datos UNICA v2 (Portal Administrativo + Portal Deportivo)
   Creacion desde cero: estructura + catalogos. SIN personas, usuarios, jugadores, pagos ni becas.
   -------------------------------------------------------------------------------------
   Generado el 2026-10-17. Parte de la base del Portal Administrativo (rama feat/paleta-avisos-azul, con sus
   20 migraciones aplicadas) y de la ultima version publicada del Portal Deportivo (main del 2026-10-15).
   Reemplaza al script del 2026-10-07 del sandbox, que ya no alcanzaba para el codigo actual.

   QUE HACE
     1. Crea la base ClubCamionerosUnificadaV2 (collation Modern_Spanish_CI_AS).
     2. Crea tablas, claves, restricciones, indices, secuencias, la vista VISTA_ESTADO_DEUDA y los triggers.
     3. Carga solo catalogos: ROLES, CATEGORIAS y TIPOS_DESCUENTO. NO carga aranceles (se programan desde el
        Portal Administrativo) ni la configuracion de cobranza.
     4. Registra en dbo.__CaccMigraciones las 20 migraciones que ya existen en el repositorio del Portal
        Administrativo (mismo nombre y mismo hash que calcula la API), para que al iniciar no vuelva a aplicarlas.
   COMO USARLO: sqlcmd -S <servidor> -E -C -b -i este_archivo.sql   (o F5 en SSMS). Es seguro repetirlo:
   si la base ya tiene tablas, no hace nada.

   CONTRATO DE ESTE ESQUEMA (lo que las dos APIs tienen que respetar)
     - NOMBRES: los del Portal Deportivo (PERSONAS, USUARIOS, TIPOS_DESCUENTO, JUGADORES_RESPONSABLES, STAFF_CATEGORIAS).
     - IDs: PAGOS y JUGADORES toman el id de las secuencias SEQ_PAGOS y SEQ_JUGADORES (DEFAULT de la columna): un INSERT
       que omite el id funciona; nadie calcula MAX+1. El resto de las tablas son IDENTITY.
     - Un INSERT ... OUTPUT sin INTO falla (Msg 334) en las tablas con trigger. Usar OUTPUT ... INTO o SCOPE_IDENTITY().
     - Auditoria: al abrir la conexion, EXEC sys.sp_set_session_context @key=N'idUsuario', @value=<id del operador>.
       Sin eso el evento queda sin responsable.
     - PERSONAS.dni: solo digitos (6 a 9), unico. Es el usuario del login. Una persona = una fila.
     - USUARIOS: una cuenta por persona, UNA contrasena (contrasenia, BCrypt) para los dos portales. Una cuenta nueva
       nace SIN acceso al Portal Administrativo (acceso_portal = 0). rol_portal (1 SuperAdmin, 2 Administrador) es
       independiente de FK_id_rol (ROLES, ids fijos del Portal Deportivo).
     - Baja de jugadores: JAMAS se borra. JUGADORES.fecha_baja con fecha = dado de baja; JUGADORES.activo se calcula.
       Reingresar = fecha_baja NULL y fecha_alta = fecha del reingreso. JUGADORES_PERIODOS conserva todas las
       permanencias (el trigger TR_JUGADORES_PERIODOS la mantiene sola, escriba quien escriba).
     - PAGOS.fecha_vencimiento NO es el dia de vencimiento: es el PRIMER DIA DEL MES que cubre la cuota. La cuota vence
       el 10 del mes siguiente. Una cuota = una fila pendiente (estado 0, monto_final = saldo) + una fila por cada
       abono (estado 1). El beneficio (beca/descuento) no se graba en PAGOS: se aplica al leer. Por eso el estado de deuda
       se pide a la API del Portal Administrativo; VISTA_ESTADO_DEUDA es solo un puente transitorio.
     - Categoria de cobro de un jugador = JUGADORES.FK_id_categoria (una sola). JUGADORES_CATEGORIAS es pertenencia
       deportiva (puede ser mas de una).
     - Los indices filtrados exigen SET QUOTED_IDENTIFIER ON en toda conexion que escriba (valor por defecto de SqlClient).
   ===================================================================================== */

IF DB_ID(N'ClubCamionerosUnificadaV2') IS NULL
    CREATE DATABASE [ClubCamionerosUnificadaV2] COLLATE Modern_Spanish_CI_AS;
GO

USE [ClubCamionerosUnificadaV2];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.USUARIOS', N'U') IS NOT NULL OR OBJECT_ID(N'dbo.PERSONAS', N'U') IS NOT NULL
BEGIN
    PRINT N'La base ya tiene tablas del sistema: no se hizo ningun cambio.';
    SET NOEXEC ON;
END
GO

/* ================================ SECUENCIAS ================================ */
-- NO CACHE: los numeros no se saltean al reiniciar el servidor.
CREATE SEQUENCE dbo.SEQ_PAGOS AS INT START WITH 1 INCREMENT BY 1 NO CACHE;
CREATE SEQUENCE dbo.SEQ_JUGADORES AS INT START WITH 1 INCREMENT BY 1 NO CACHE;
GO

/* ================================ TABLAS ================================ */

CREATE TABLE dbo.[__CaccMigraciones] (
    [Nombre] nvarchar(200) NOT NULL,
    [Hash] char(64) NOT NULL,
    [AplicadaEnUtc] datetime2(7) NOT NULL CONSTRAINT [DF___CaccMigraciones_AplicadaEnUtc] DEFAULT (sysutcdatetime()),
    CONSTRAINT [PK___CaccMigraciones] PRIMARY KEY CLUSTERED ([Nombre])
);
GO

-- Parametros del club que no son un arancel. Clave conocida: 'inicio_cobranza' (AAAA-MM-DD) = primer mes que se
-- cobra; el codigo del Portal Administrativo la usara para no generar ni avisar cuotas de meses anteriores.
CREATE TABLE dbo.[CONFIGURACION_CLUB] (
    [clave] varchar(60) NOT NULL,
    [valor] nvarchar(200) NOT NULL,
    [descripcion] nvarchar(400) NULL,
    [actualizado_utc] datetime2(7) NOT NULL CONSTRAINT [DF_CONFIGURACION_CLUB_actualizado] DEFAULT (sysutcdatetime()),
    CONSTRAINT [PK_CONFIGURACION_CLUB] PRIMARY KEY CLUSTERED ([clave])
);
GO

CREATE TABLE dbo.[ARANCELES] (
    [PK_id_arancel] int IDENTITY(1,1) NOT NULL,
    [genero] varchar(20) NULL,
    [monto] decimal(18,2) NOT NULL,
    [vigente_desde] date NOT NULL,
    [FK_id_categoria] int NULL,
    CONSTRAINT [PK_ARANCELES] PRIMARY KEY CLUSTERED ([PK_id_arancel]),
    CONSTRAINT [CK_ARANCELES_GENERO] CHECK ([genero] IS NULL OR [genero]='Masculino' OR [genero]='Femenino'),
    CONSTRAINT [CK_ARANCELES_GENERO_O_CATEGORIA] CHECK ([genero] IS NOT NULL AND [FK_id_categoria] IS NULL OR [genero] IS NULL AND [FK_id_categoria] IS NOT NULL),
    CONSTRAINT [CK_ARANCELES_MONTO] CHECK ([monto]>(0))
);
GO

-- Arancel de inscripcion. genero NULL = aplica a todos los generos (asi funciona el codigo actual, que no manda genero);
-- con genero, solo a ese genero y tiene prioridad. monto 0 = no se cobra inscripcion a ese genero.
CREATE TABLE dbo.[ARANCELES_INSCRIPCION] (
    [PK_id_arancel_inscripcion] int IDENTITY(1,1) NOT NULL,
    [monto] decimal(18,2) NOT NULL,
    [vigente_desde] date NOT NULL,
    [genero] varchar(20) NULL,
    CONSTRAINT [PK_ARANCELES_INSCRIPCION] PRIMARY KEY CLUSTERED ([PK_id_arancel_inscripcion]),
    CONSTRAINT [CK_ARANCELES_INSCRIPCION_MONTO] CHECK ([monto]>=(0)),
    CONSTRAINT [CK_ARANCELES_INSCRIPCION_GENERO] CHECK ([genero] IS NULL OR [genero]='Masculino' OR [genero]='Femenino')
);
GO

CREATE TABLE dbo.[AUDITORIA_CAMBIOS] (
    [PK_id_evento] bigint IDENTITY(1,1) NOT NULL,
    [id_usuario] int NULL,
    [email_usuario] nvarchar(254) NULL,
    [entidad] sysname NOT NULL,
    [id_entidad] nvarchar(128) NOT NULL,
    [accion] varchar(10) NOT NULL,
    [fecha_utc] datetime2(7) NOT NULL CONSTRAINT [DF_AUDITORIA_CAMBIOS_fecha_utc] DEFAULT (sysutcdatetime()),
    [datos_antes] nvarchar(MAX) NULL,
    [datos_despues] nvarchar(MAX) NULL,
    [nombre_usuario] nvarchar(100) NULL,
    [apellido_usuario] nvarchar(100) NULL,
    [dni_usuario] nvarchar(20) NULL,
    CONSTRAINT [PK_AUDITORIA_CAMBIOS] PRIMARY KEY CLUSTERED ([PK_id_evento]),
    CONSTRAINT [CK_AUDITORIA_CAMBIOS_accion] CHECK ([accion]='DELETE' OR [accion]='UPDATE' OR [accion]='INSERT'),
    CONSTRAINT [CK_AUDITORIA_CAMBIOS_antes_json] CHECK ([datos_antes] IS NULL OR isjson([datos_antes])=(1)),
    CONSTRAINT [CK_AUDITORIA_CAMBIOS_despues_json] CHECK ([datos_despues] IS NULL OR isjson([datos_despues])=(1))
);
GO

CREATE TABLE dbo.[CATEGORIAS] (
    [PK_id_categoria] int IDENTITY(1,1) NOT NULL,
    [nombre_categoria] varchar(100) NULL,
    [asociacion] varchar(50) NULL,
    CONSTRAINT [PK_CATEGORIAS] PRIMARY KEY CLUSTERED ([PK_id_categoria])
);
GO

CREATE TABLE dbo.[PERSONAS] (
    [PK_id_persona] int IDENTITY(1,1) NOT NULL,
    [genero] varchar(50) NULL,
    [fecha_de_nacimiento] date NULL,
    [dni] varchar(20) NULL,
    [nombre] varchar(100) NULL,
    [apellido] varchar(100) NULL,
    [domicilio] varchar(255) NULL,
    CONSTRAINT [PK_PERSONAS] PRIMARY KEY CLUSTERED ([PK_id_persona]),
    CONSTRAINT [CK_PERSONAS_dni_digitos] CHECK ([dni] IS NULL OR len([dni])>=(6) AND len([dni])<=(9) AND NOT [dni] like '%[^0-9]%')
);
GO

CREATE TABLE dbo.[ROLES] (
    [PK_id_rol] int IDENTITY(1,1) NOT NULL,
    [nombre_rol] varchar(100) NULL,
    [permisos] varchar(255) NULL,
    CONSTRAINT [PK_ROLES] PRIMARY KEY CLUSTERED ([PK_id_rol])
);
GO

CREATE TABLE dbo.[USUARIOS] (
    [PK_id_usuario] int IDENTITY(1,1) NOT NULL,
    [FK_id_persona] int NULL,
    [FK_id_rol] int NULL,
    [email] varchar(255) NULL,
    [contrasenia] varchar(255) NULL,
    [activo] bit NULL,
    [acceso_portal] bit NOT NULL CONSTRAINT [DF_USUARIOS_acceso_portal] DEFAULT ((0)),
    [rol_portal] int NULL,
    [activacion_pendiente] bit NOT NULL CONSTRAINT [DF_USUARIOS_activacion_pendiente] DEFAULT ((0)),
    [token_version] int NOT NULL CONSTRAINT [DF_USUARIOS_token_version] DEFAULT ((0)),
    CONSTRAINT [PK_USUARIOS] PRIMARY KEY CLUSTERED ([PK_id_usuario]),
    CONSTRAINT [CK_USUARIOS_acceso_portal_requiere_rol] CHECK ([acceso_portal]=(0) OR [rol_portal] IS NOT NULL),
    CONSTRAINT [CK_USUARIOS_email_formato] CHECK ([email] IS NULL OR [email] like '%_@_%._%'),
    CONSTRAINT [CK_USUARIOS_rol_portal_valores] CHECK ([rol_portal] IS NULL OR ([rol_portal]=(2) OR [rol_portal]=(1)))
);
GO

CREATE TABLE dbo.[TOKEN_ACCESO_CUENTA] (
    [PK_id_token] bigint IDENTITY(1,1) NOT NULL,
    [id_usuario] int NOT NULL,
    [tipo] varchar(16) NOT NULL,
    [hash_token] char(64) NOT NULL,
    [creado_utc] datetime2(7) NOT NULL CONSTRAINT [DF_TOKEN_ACCESO_CUENTA_creado] DEFAULT (sysutcdatetime()),
    [vence_utc] datetime2(7) NOT NULL,
    [consumido_utc] datetime2(7) NULL,
    CONSTRAINT [PK_TOKEN_ACCESO_CUENTA] PRIMARY KEY CLUSTERED ([PK_id_token]),
    CONSTRAINT [UQ_TOKEN_ACCESO_CUENTA_hash] UNIQUE NONCLUSTERED ([hash_token]),
    CONSTRAINT [CK_TOKEN_ACCESO_CUENTA_tipo] CHECK ([tipo]='RECUPERACION' OR [tipo]='ACTIVACION')
);
GO

-- Intentos fallidos de ingreso por cuenta (LoginThrottle). La clave es un hash del identificador.
CREATE TABLE dbo.[INTENTOS_LOGIN] (
    [clave] char(64) NOT NULL,
    [fallos] int NOT NULL,
    [desde_utc] datetime2(0) NOT NULL,
    CONSTRAINT [PK_INTENTOS_LOGIN] PRIMARY KEY CLUSTERED ([clave])
);
GO

CREATE TABLE dbo.[STAFF] (
    [PK_id_staff] int IDENTITY(1,1) NOT NULL,
    [FK_id_usuario] int NULL,
    CONSTRAINT [PK_STAFF] PRIMARY KEY CLUSTERED ([PK_id_staff])
);
GO

CREATE TABLE dbo.[STAFF_CATEGORIAS] (
    [PK_id_staff_categoria] int IDENTITY(1,1) NOT NULL,
    [FK_id_staff] int NULL,
    [FK_id_categoria] int NULL,
    CONSTRAINT [PK_STAFF_CATEGORIAS] PRIMARY KEY CLUSTERED ([PK_id_staff_categoria])
);
GO

CREATE TABLE dbo.[RESPONSABLES] (
    [PK_id_responsable] int IDENTITY(1,1) NOT NULL,
    [FK_id_persona] int NULL,
    [email] varchar(255) NULL,
    [telefono] varchar(50) NULL,
    CONSTRAINT [PK_RESPONSABLES] PRIMARY KEY CLUSTERED ([PK_id_responsable])
);
GO

-- JUGADORES.activo se calcula: nadie lo escribe y no puede contradecir a fecha_baja.
CREATE TABLE dbo.[JUGADORES] (
    [PK_id_jugador] int NOT NULL CONSTRAINT [DF_JUGADORES_PK_id_jugador] DEFAULT (NEXT VALUE FOR dbo.SEQ_JUGADORES),
    [FK_id_persona] int NULL,
    [FK_id_categoria] int NULL,
    [club_origen] varchar(100) NULL,
    [ficha_medica_liga] bit NULL,
    [posicion_cancha] varchar(50) NULL,
    [peso] decimal(5,2) NULL,
    [altura] decimal(4,2) NULL,
    [pie_habil] varchar(20) NULL,
    [grupo_sanguineo] varchar(5) NULL,
    [fecha_alta] date NULL CONSTRAINT [DF_JUGADORES_fecha_alta] DEFAULT (CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Argentina Standard Time' AS DATE)),
    [fecha_baja] date NULL,
    [activo] AS (CONVERT(bit, CASE WHEN [fecha_baja] IS NULL THEN 1 ELSE 0 END)),
    CONSTRAINT [PK_JUGADORES] PRIMARY KEY CLUSTERED ([PK_id_jugador])
);
GO

-- Historial de permanencias en el club: una fila por periodo (alta -> baja). Lo mantiene TR_JUGADORES_PERIODOS.
CREATE TABLE dbo.[JUGADORES_PERIODOS] (
    [PK_id_periodo] int IDENTITY(1,1) NOT NULL,
    [FK_id_jugador] int NOT NULL,
    [fecha_alta] date NOT NULL,
    [fecha_baja] date NULL,
    CONSTRAINT [PK_JUGADORES_PERIODOS] PRIMARY KEY CLUSTERED ([PK_id_periodo]),
    CONSTRAINT [CK_JUGADORES_PERIODOS_fechas] CHECK ([fecha_baja] IS NULL OR [fecha_baja]>=[fecha_alta])
);
GO

CREATE TABLE dbo.[JUGADORES_CATEGORIAS] (
    [PK_id_jugador_categoria] int IDENTITY(1,1) NOT NULL,
    [FK_id_jugador] int NOT NULL,
    [FK_id_categoria] int NOT NULL,
    [fecha_asignacion] datetime NULL CONSTRAINT [DF_JUGADORES_CATEGORIAS_fecha_asignacion] DEFAULT (getdate()),
    CONSTRAINT [PK_JUGADORES_CATEGORIAS] PRIMARY KEY CLUSTERED ([PK_id_jugador_categoria]),
    CONSTRAINT [UQ_JUGADORES_CATEGORIAS] UNIQUE NONCLUSTERED ([FK_id_jugador], [FK_id_categoria])
);
GO

CREATE TABLE dbo.[JUGADORES_RESPONSABLES] (
    [PK_id_jugador_responsable] int IDENTITY(1,1) NOT NULL,
    [FK_id_responsable] int NULL,
    [FK_id_jugador] int NULL,
    [parentesco] varchar(50) NULL,
    CONSTRAINT [PK_JUGADORES_RESPONSABLES] PRIMARY KEY CLUSTERED ([PK_id_jugador_responsable])
);
GO

CREATE TABLE dbo.[FICHAS_MEDICAS] (
    [PK_id_ficha_medica] int IDENTITY(1,1) NOT NULL,
    [FK_id_jugador] int NULL,
    [obra_social] varchar(150) NULL,
    [numero_afiliado_os] int NULL,
    [grupo_sanguineo] varchar(10) NULL,
    [factor] char(1) NULL,
    [antecedentes_salud] varchar(255) NULL,
    [medicamentos_cronicos] varchar(255) NULL,
    [condiciones_cronicas] varchar(255) NULL,
    [apto_fisico_vencimiento] date NULL,
    [observaciones] varchar(255) NULL,
    [fecha_actualizacion] datetime NULL,
    CONSTRAINT [PK_FICHAS_MEDICAS] PRIMARY KEY CLUSTERED ([PK_id_ficha_medica])
);
GO

CREATE TABLE dbo.[HISTORIAL_PARTIDOS] (
    [PK_id_partido] int IDENTITY(1,1) NOT NULL,
    [FK_id_categoria] int NULL,
    [fecha_partido] date NULL,
    [rival] varchar(100) NULL,
    [condicion_localia] varchar(20) NULL,
    [resultado] varchar(20) NULL,
    [observaciones] varchar(255) NULL,
    CONSTRAINT [PK_HISTORIAL_PARTIDOS] PRIMARY KEY CLUSTERED ([PK_id_partido])
);
GO

CREATE TABLE dbo.[JUGADORES_PARTIDOS] (
    [PK_id_jugador_partido] int IDENTITY(1,1) NOT NULL,
    [FK_id_partido] int NULL,
    [FK_id_jugador] int NULL,
    [minutos_jugados] int NULL,
    [goles] int NULL,
    [tarjetas_amarillas] int NULL,
    [tarjeta_roja] bit NULL,
    CONSTRAINT [PK_JUGADORES_PARTIDOS] PRIMARY KEY CLUSTERED ([PK_id_jugador_partido])
);
GO

CREATE TABLE dbo.[ASISTENCIAS] (
    [PK_id_asistencia] int IDENTITY(1,1) NOT NULL,
    [FK_id_jugador] int NULL,
    [FK_id_staff] int NULL,
    [fecha_hora] datetime NULL,
    [estado_acceso] varchar(50) NULL,
    [FK_id_persona] int NULL,
    [observaciones] nvarchar(500) NULL,
    CONSTRAINT [PK_ASISTENCIAS] PRIMARY KEY CLUSTERED ([PK_id_asistencia])
);
GO

CREATE TABLE dbo.[CREDENCIALES_QR] (
    [id_credencial] uniqueidentifier NOT NULL,
    [FK_id_persona] int NOT NULL,
    [activa] bit NOT NULL CONSTRAINT [DF_CREDENCIALES_QR_activa] DEFAULT ((1)),
    [fecha_emision] datetime2(7) NOT NULL CONSTRAINT [DF_CREDENCIALES_QR_fecha_emision] DEFAULT (sysutcdatetime()),
    [fecha_revocacion] datetime2(7) NULL,
    CONSTRAINT [PK_CREDENCIALES_QR] PRIMARY KEY CLUSTERED ([id_credencial])
);
GO

CREATE TABLE dbo.[TIPOS_DESCUENTO] (
    [PK_id_descuento] int IDENTITY(1,1) NOT NULL,
    [tipo_descuento] varchar(100) NOT NULL,
    CONSTRAINT [PK_TIPOS_DESCUENTO] PRIMARY KEY CLUSTERED ([PK_id_descuento])
);
GO

-- Becas y descuentos. Cubren cuotas enteras (inicio = primer dia de un mes, fin = ultimo dia de un mes).
-- Estado derivado: anulado = 1 -> Anulada; estado_activo = 0 -> Cancelada; el resto por fechas (ver DiscountDao).
CREATE TABLE dbo.[JUGADORES_DESCUENTOS] (
    [PK_id_jugador_descuento] int IDENTITY(1,1) NOT NULL,
    [FK_id_jugador] int NOT NULL,
    [FK_id_descuento] int NOT NULL,
    [estado_activo] bit NOT NULL CONSTRAINT [DF_JUGDESC_ESTADO] DEFAULT ((1)),
    [tipo_valor] varchar(10) NULL,
    [porcentaje] decimal(5,2) NULL,
    [monto_fijo] decimal(10,2) NULL,
    [fecha_inicio] date NOT NULL,
    [fecha_fin] date NOT NULL,
    [fecha_cancelacion] date NULL,
    [anulado] bit NOT NULL CONSTRAINT [DF_JUGDESC_ANULADO] DEFAULT ((0)),
    [cancelada_por_baja] bit NOT NULL CONSTRAINT [DF_JUGDESC_CANCELADA_POR_BAJA] DEFAULT ((0)),
    [fecha_asignacion] date NULL,
    CONSTRAINT [PK_JUGADORES_DESCUENTOS] PRIMARY KEY CLUSTERED ([PK_id_jugador_descuento]),
    CONSTRAINT [CK_JUGDESC_MONTO_POSITIVO] CHECK ([monto_fijo] IS NULL OR [monto_fijo]>(0)),
    CONSTRAINT [CK_JUGDESC_PORCENTAJE_RANGO] CHECK ([porcentaje] IS NULL OR [porcentaje]>(0) AND [porcentaje]<=(100)),
    CONSTRAINT [CK_JUGDESC_TIPO_VALOR] CHECK ([tipo_valor]='$' OR [tipo_valor]='%'),
    CONSTRAINT [CK_JUGDESC_VALOR_EXCLUYENTE] CHECK ([tipo_valor]='%' AND [porcentaje] IS NOT NULL AND [monto_fijo] IS NULL OR [tipo_valor]='$' AND [monto_fijo] IS NOT NULL AND [porcentaje] IS NULL),
    CONSTRAINT [CK_JUGDESC_VIGENCIA] CHECK ([fecha_fin]>[fecha_inicio])
);
GO

CREATE TABLE dbo.[PAGOS] (
    [PK_id_pago] int NOT NULL CONSTRAINT [DF_PAGOS_PK_id_pago] DEFAULT (NEXT VALUE FOR dbo.SEQ_PAGOS),
    [FK_id_jugador] int NULL,
    [monto_base] decimal(18,2) NULL,
    [FK_id_jugador_descuento] int NULL,
    [monto_final] decimal(18,2) NULL,
    [fecha_pago] date NULL,
    [metodo_pago] varchar(50) NULL,
    [fecha_vencimiento] date NULL,
    [estado] bit NULL,
    [concepto] varchar(20) NOT NULL CONSTRAINT [DF_PAGOS_CONCEPTO] DEFAULT ('Cuota'),
    [fecha_hora_registro] datetime2(7) NULL,
    [FK_id_usuario_registro] int NULL,
    CONSTRAINT [PK_PAGOS] PRIMARY KEY CLUSTERED ([PK_id_pago]),
    CONSTRAINT [CK_PAGOS_CONCEPTO] CHECK ([concepto]='Inscripcion' OR [concepto]='Cuota')
);
GO

-- Registro permanente (solo se agrega) de los pagos anulados por error.
CREATE TABLE dbo.[PAGOS_ANULADOS] (
    [PK_id_anulacion] int IDENTITY(1,1) NOT NULL,
    [id_pago] int NOT NULL,
    [FK_id_jugador] int NOT NULL,
    [concepto] varchar(20) NOT NULL,
    [monto] decimal(18,2) NOT NULL,
    [monto_base] decimal(18,2) NULL,
    [metodo_pago] varchar(50) NULL,
    [fecha_pago] date NULL,
    [fecha_vencimiento] date NULL,
    [fecha_hora_registro_pago] datetime2(7) NULL,
    [FK_id_usuario_registro_pago] int NULL,
    [motivo] nvarchar(500) NOT NULL,
    [FK_id_usuario_anulacion] int NOT NULL,
    [fecha_hora_anulacion] datetime2(7) NOT NULL,
    CONSTRAINT [PK_PAGOS_ANULADOS] PRIMARY KEY CLUSTERED ([PK_id_anulacion]),
    CONSTRAINT [CK_PAGOS_ANULADOS_MOTIVO] CHECK (LEN(LTRIM(RTRIM([motivo])))>=(5))
);
GO

/* ===================== CATALOGOS (sin datos de personas) ===================== */
-- Se cargan antes de crear los triggers y las claves foraneas, para no llenar la auditoria con la carga inicial.

-- ROLES: los ids son los que el Portal Deportivo tiene escritos en su codigo. No renumerar.
SET IDENTITY_INSERT dbo.[ROLES] ON;
INSERT dbo.[ROLES] ([PK_id_rol], [nombre_rol], [permisos]) VALUES (1, N'Administrador (Tesorero)', N'ACCESO_TOTAL');
INSERT dbo.[ROLES] ([PK_id_rol], [nombre_rol], [permisos]) VALUES (2, N'Administrativo', N'FINANZAS_PAGOS_DEUDA');
INSERT dbo.[ROLES] ([PK_id_rol], [nombre_rol], [permisos]) VALUES (3, N'M' + NCHAR(233) + N'dico', N'SALUD_FICHAS_MEDICAS');
INSERT dbo.[ROLES] ([PK_id_rol], [nombre_rol], [permisos]) VALUES (4, N'Director T' + NCHAR(233) + N'cnico', N'DEPORTIVO_CATEGORIA');
INSERT dbo.[ROLES] ([PK_id_rol], [nombre_rol], [permisos]) VALUES (5, N'Don QR', N'ASISTENCIA_SEMAFORO_QR');
INSERT dbo.[ROLES] ([PK_id_rol], [nombre_rol], [permisos]) VALUES (6, N'Preparador F' + NCHAR(237) + N'sico', N'DEPORTIVO_CATEGORIA_FISICO');
INSERT dbo.[ROLES] ([PK_id_rol], [nombre_rol], [permisos]) VALUES (7, N'Coordinador', N'DEPORTIVO_GENERAL');
SET IDENTITY_INSERT dbo.[ROLES] OFF;

-- CATEGORIAS (13): la asociacion sigue la regla del Portal Deportivo (nombre con 'AFA' -> AFA; si no, Liga Cordobesa).
SET IDENTITY_INSERT dbo.[CATEGORIAS] ON;
INSERT dbo.[CATEGORIAS] ([PK_id_categoria], [nombre_categoria], [asociacion]) VALUES (1, N'AFA 20067', 'AFA');
INSERT dbo.[CATEGORIAS] ([PK_id_categoria], [nombre_categoria], [asociacion]) VALUES (2, N'Pt  AFA 2008', 'AFA');
INSERT dbo.[CATEGORIAS] ([PK_id_categoria], [nombre_categoria], [asociacion]) VALUES (3, N'Pt  AFA 2009', 'AFA');
INSERT dbo.[CATEGORIAS] ([PK_id_categoria], [nombre_categoria], [asociacion]) VALUES (4, N'Pt  AFA 2010', 'AFA');
INSERT dbo.[CATEGORIAS] ([PK_id_categoria], [nombre_categoria], [asociacion]) VALUES (5, N'Pt  AFA 2011', 'AFA');
INSERT dbo.[CATEGORIAS] ([PK_id_categoria], [nombre_categoria], [asociacion]) VALUES (6, N'Pt  AFA 2012', 'AFA');
INSERT dbo.[CATEGORIAS] ([PK_id_categoria], [nombre_categoria], [asociacion]) VALUES (7, N'Pt  preAFA 2013', 'AFA');
INSERT dbo.[CATEGORIAS] ([PK_id_categoria], [nombre_categoria], [asociacion]) VALUES (8, N'Pt  preAFA 2014', 'AFA');
INSERT dbo.[CATEGORIAS] ([PK_id_categoria], [nombre_categoria], [asociacion]) VALUES (9, N'Pt  preAFA 2015', 'AFA');
INSERT dbo.[CATEGORIAS] ([PK_id_categoria], [nombre_categoria], [asociacion]) VALUES (10, N'Pt 2016', 'Liga Cordobesa');
INSERT dbo.[CATEGORIAS] ([PK_id_categoria], [nombre_categoria], [asociacion]) VALUES (11, N'Pt 2017', 'Liga Cordobesa');
INSERT dbo.[CATEGORIAS] ([PK_id_categoria], [nombre_categoria], [asociacion]) VALUES (12, N'Cebollitas', 'Liga Cordobesa');
INSERT dbo.[CATEGORIAS] ([PK_id_categoria], [nombre_categoria], [asociacion]) VALUES (13, N'Femenino', 'Liga Cordobesa');
SET IDENTITY_INSERT dbo.[CATEGORIAS] OFF;

-- TIPOS_DESCUENTO: catalogo cerrado de motivos de beca/descuento (no hay ninguna beca cargada).
SET IDENTITY_INSERT dbo.[TIPOS_DESCUENTO] ON;
INSERT dbo.[TIPOS_DESCUENTO] ([PK_id_descuento], [tipo_descuento]) VALUES (1, N'Becado');
INSERT dbo.[TIPOS_DESCUENTO] ([PK_id_descuento], [tipo_descuento]) VALUES (2, N'Descuento');
SET IDENTITY_INSERT dbo.[TIPOS_DESCUENTO] OFF;
GO

/* ================================ INDICES ================================ */

CREATE UNIQUE NONCLUSTERED INDEX [UX_ARANCELES_CATEGORIA_FECHA] ON dbo.[ARANCELES] ([FK_id_categoria], [vigente_desde]) WHERE ([FK_id_categoria] IS NOT NULL);
CREATE UNIQUE NONCLUSTERED INDEX [UX_ARANCELES_GENERO_FECHA] ON dbo.[ARANCELES] ([genero], [vigente_desde]) WHERE ([genero] IS NOT NULL);
CREATE UNIQUE NONCLUSTERED INDEX [UX_ARANCELES_INSCRIPCION_GENERAL_FECHA] ON dbo.[ARANCELES_INSCRIPCION] ([vigente_desde]) WHERE ([genero] IS NULL);
CREATE UNIQUE NONCLUSTERED INDEX [UX_ARANCELES_INSCRIPCION_GENERO_FECHA] ON dbo.[ARANCELES_INSCRIPCION] ([genero], [vigente_desde]) WHERE ([genero] IS NOT NULL);
CREATE NONCLUSTERED INDEX [IX_ASISTENCIAS_persona_fecha] ON dbo.[ASISTENCIAS] ([FK_id_persona], [fecha_hora]);
CREATE NONCLUSTERED INDEX [IX_ASISTENCIAS_jugador_fecha] ON dbo.[ASISTENCIAS] ([FK_id_jugador], [fecha_hora]);
CREATE NONCLUSTERED INDEX [IX_AUDITORIA_CAMBIOS_entidad_evento] ON dbo.[AUDITORIA_CAMBIOS] ([entidad], [id_entidad], [accion], [fecha_utc], [PK_id_evento]) INCLUDE ([id_usuario]);
CREATE NONCLUSTERED INDEX [IX_AUDITORIA_CAMBIOS_fecha] ON dbo.[AUDITORIA_CAMBIOS] ([fecha_utc] DESC, [PK_id_evento] DESC);
CREATE NONCLUSTERED INDEX [IX_AUDITORIA_CAMBIOS_usuario_fecha] ON dbo.[AUDITORIA_CAMBIOS] ([id_usuario], [fecha_utc] DESC, [PK_id_evento] DESC);
CREATE UNIQUE NONCLUSTERED INDEX [UX_CREDENCIALES_QR_PERSONA_ACTIVA] ON dbo.[CREDENCIALES_QR] ([FK_id_persona]) WHERE ([activa]=(1) AND [fecha_revocacion] IS NULL);
CREATE NONCLUSTERED INDEX [IX_INTENTOS_LOGIN_desde] ON dbo.[INTENTOS_LOGIN] ([desde_utc]);
CREATE NONCLUSTERED INDEX [IX_JUGADORES_categoria] ON dbo.[JUGADORES] ([FK_id_categoria]);
CREATE NONCLUSTERED INDEX [IX_JUGADORES_persona] ON dbo.[JUGADORES] ([FK_id_persona]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_JUGADORES_PERIODOS_ABIERTO] ON dbo.[JUGADORES_PERIODOS] ([FK_id_jugador]) WHERE ([fecha_baja] IS NULL);
CREATE NONCLUSTERED INDEX [IX_JUGADORES_PERIODOS_jugador] ON dbo.[JUGADORES_PERIODOS] ([FK_id_jugador], [fecha_alta]);
CREATE NONCLUSTERED INDEX [IX_JUGDESC_JUGADOR_VIGENCIA] ON dbo.[JUGADORES_DESCUENTOS] ([FK_id_jugador], [estado_activo], [fecha_inicio], [fecha_fin]);
CREATE NONCLUSTERED INDEX [IX_PAGOS_Estado_Pendientes] ON dbo.[PAGOS] ([estado]) INCLUDE ([FK_id_jugador], [monto_base], [monto_final], [fecha_vencimiento], [concepto]);
CREATE NONCLUSTERED INDEX [IX_PAGOS_Jugador_Concepto_Periodo_Estado] ON dbo.[PAGOS] ([FK_id_jugador], [concepto], [fecha_vencimiento], [estado]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_PAGOS_CuotaPendiente_Jugador_Periodo] ON dbo.[PAGOS] ([FK_id_jugador], [fecha_vencimiento], [concepto]) WHERE ([estado]=(0) AND [concepto]='Cuota' AND [fecha_vencimiento] IS NOT NULL);
CREATE UNIQUE NONCLUSTERED INDEX [UX_PAGOS_InscripcionPendiente_Jugador] ON dbo.[PAGOS] ([FK_id_jugador]) WHERE ([estado]=(0) AND [concepto]='Inscripcion');
CREATE NONCLUSTERED INDEX [IX_PAGOS_ANULADOS_jugador] ON dbo.[PAGOS_ANULADOS] ([FK_id_jugador], [fecha_hora_anulacion]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_PERSONAS_dni] ON dbo.[PERSONAS] ([dni]) WHERE ([dni] IS NOT NULL);
CREATE UNIQUE NONCLUSTERED INDEX [UX_TIPOSDESC_NOMBRE] ON dbo.[TIPOS_DESCUENTO] ([tipo_descuento]);
CREATE NONCLUSTERED INDEX [IX_TOKEN_ACCESO_CUENTA_usuario_tipo] ON dbo.[TOKEN_ACCESO_CUENTA] ([id_usuario], [tipo], [consumido_utc], [vence_utc]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_USUARIOS_email] ON dbo.[USUARIOS] ([email]) WHERE ([email] IS NOT NULL);
CREATE UNIQUE NONCLUSTERED INDEX [UX_USUARIOS_persona] ON dbo.[USUARIOS] ([FK_id_persona]) WHERE ([FK_id_persona] IS NOT NULL);
GO

/* ============================= CLAVES FORANEAS ============================= */

ALTER TABLE dbo.[ARANCELES] WITH CHECK ADD CONSTRAINT [FK_ARANCELES_CATEGORIAS] FOREIGN KEY ([FK_id_categoria]) REFERENCES dbo.[CATEGORIAS] ([PK_id_categoria]);
ALTER TABLE dbo.[ASISTENCIAS] WITH CHECK ADD CONSTRAINT [FK_ASISTENCIAS_JUGADORES] FOREIGN KEY ([FK_id_jugador]) REFERENCES dbo.[JUGADORES] ([PK_id_jugador]);
ALTER TABLE dbo.[ASISTENCIAS] WITH CHECK ADD CONSTRAINT [FK_ASISTENCIAS_PERSONAS] FOREIGN KEY ([FK_id_persona]) REFERENCES dbo.[PERSONAS] ([PK_id_persona]);
ALTER TABLE dbo.[ASISTENCIAS] WITH CHECK ADD CONSTRAINT [FK_ASISTENCIAS_STAFF] FOREIGN KEY ([FK_id_staff]) REFERENCES dbo.[STAFF] ([PK_id_staff]);
ALTER TABLE dbo.[CREDENCIALES_QR] WITH CHECK ADD CONSTRAINT [FK_CREDENCIALES_QR_PERSONAS] FOREIGN KEY ([FK_id_persona]) REFERENCES dbo.[PERSONAS] ([PK_id_persona]);
ALTER TABLE dbo.[FICHAS_MEDICAS] WITH CHECK ADD CONSTRAINT [FK_FICHAS_MEDICAS_JUGADORES] FOREIGN KEY ([FK_id_jugador]) REFERENCES dbo.[JUGADORES] ([PK_id_jugador]);
ALTER TABLE dbo.[HISTORIAL_PARTIDOS] WITH CHECK ADD CONSTRAINT [FK_HISTORIAL_CATEGORIA] FOREIGN KEY ([FK_id_categoria]) REFERENCES dbo.[CATEGORIAS] ([PK_id_categoria]);
ALTER TABLE dbo.[JUGADORES] WITH CHECK ADD CONSTRAINT [FK_JUGADORES_CATEGORIAS] FOREIGN KEY ([FK_id_categoria]) REFERENCES dbo.[CATEGORIAS] ([PK_id_categoria]);
ALTER TABLE dbo.[JUGADORES] WITH CHECK ADD CONSTRAINT [FK_JUGADORES_PERSONAS] FOREIGN KEY ([FK_id_persona]) REFERENCES dbo.[PERSONAS] ([PK_id_persona]);
ALTER TABLE dbo.[JUGADORES_PERIODOS] WITH CHECK ADD CONSTRAINT [FK_JUGPER_JUGADORES] FOREIGN KEY ([FK_id_jugador]) REFERENCES dbo.[JUGADORES] ([PK_id_jugador]);
ALTER TABLE dbo.[JUGADORES_CATEGORIAS] WITH CHECK ADD CONSTRAINT [FK_JUGCAT_CATEGORIAS] FOREIGN KEY ([FK_id_categoria]) REFERENCES dbo.[CATEGORIAS] ([PK_id_categoria]);
ALTER TABLE dbo.[JUGADORES_CATEGORIAS] WITH CHECK ADD CONSTRAINT [FK_JUGCAT_JUGADORES] FOREIGN KEY ([FK_id_jugador]) REFERENCES dbo.[JUGADORES] ([PK_id_jugador]);
ALTER TABLE dbo.[JUGADORES_DESCUENTOS] WITH CHECK ADD CONSTRAINT [FK_JUGDESC_JUGADORES] FOREIGN KEY ([FK_id_jugador]) REFERENCES dbo.[JUGADORES] ([PK_id_jugador]);
ALTER TABLE dbo.[JUGADORES_DESCUENTOS] WITH CHECK ADD CONSTRAINT [FK_JUGDESC_TIPODESC] FOREIGN KEY ([FK_id_descuento]) REFERENCES dbo.[TIPOS_DESCUENTO] ([PK_id_descuento]);
ALTER TABLE dbo.[JUGADORES_PARTIDOS] WITH CHECK ADD CONSTRAINT [FK_JUGPART_JUGADOR] FOREIGN KEY ([FK_id_jugador]) REFERENCES dbo.[JUGADORES] ([PK_id_jugador]);
ALTER TABLE dbo.[JUGADORES_PARTIDOS] WITH CHECK ADD CONSTRAINT [FK_JUGPART_PARTIDO] FOREIGN KEY ([FK_id_partido]) REFERENCES dbo.[HISTORIAL_PARTIDOS] ([PK_id_partido]);
ALTER TABLE dbo.[JUGADORES_RESPONSABLES] WITH CHECK ADD CONSTRAINT [FK_JugRes_JUGADORES] FOREIGN KEY ([FK_id_jugador]) REFERENCES dbo.[JUGADORES] ([PK_id_jugador]);
ALTER TABLE dbo.[JUGADORES_RESPONSABLES] WITH CHECK ADD CONSTRAINT [FK_JugRes_RESPONSABLES] FOREIGN KEY ([FK_id_responsable]) REFERENCES dbo.[RESPONSABLES] ([PK_id_responsable]);
ALTER TABLE dbo.[PAGOS] WITH CHECK ADD CONSTRAINT [FK_PAGOS_JUGADORES] FOREIGN KEY ([FK_id_jugador]) REFERENCES dbo.[JUGADORES] ([PK_id_jugador]);
ALTER TABLE dbo.[PAGOS] WITH CHECK ADD CONSTRAINT [FK_PAGOS_JUGDESC] FOREIGN KEY ([FK_id_jugador_descuento]) REFERENCES dbo.[JUGADORES_DESCUENTOS] ([PK_id_jugador_descuento]);
ALTER TABLE dbo.[PAGOS] WITH CHECK ADD CONSTRAINT [FK_PAGOS_USUARIO_REGISTRO] FOREIGN KEY ([FK_id_usuario_registro]) REFERENCES dbo.[USUARIOS] ([PK_id_usuario]);
ALTER TABLE dbo.[PAGOS_ANULADOS] WITH CHECK ADD CONSTRAINT [FK_PAGOS_ANULADOS_JUGADOR] FOREIGN KEY ([FK_id_jugador]) REFERENCES dbo.[JUGADORES] ([PK_id_jugador]);
ALTER TABLE dbo.[PAGOS_ANULADOS] WITH CHECK ADD CONSTRAINT [FK_PAGOS_ANULADOS_USUARIO] FOREIGN KEY ([FK_id_usuario_anulacion]) REFERENCES dbo.[USUARIOS] ([PK_id_usuario]);
ALTER TABLE dbo.[RESPONSABLES] WITH CHECK ADD CONSTRAINT [FK_RESPONSABLES_PERSONAS] FOREIGN KEY ([FK_id_persona]) REFERENCES dbo.[PERSONAS] ([PK_id_persona]);
ALTER TABLE dbo.[STAFF] WITH CHECK ADD CONSTRAINT [FK_STAFF_USUARIOS] FOREIGN KEY ([FK_id_usuario]) REFERENCES dbo.[USUARIOS] ([PK_id_usuario]);
ALTER TABLE dbo.[STAFF_CATEGORIAS] WITH CHECK ADD CONSTRAINT [FK_STAFFCAT_CATEGORIAS] FOREIGN KEY ([FK_id_categoria]) REFERENCES dbo.[CATEGORIAS] ([PK_id_categoria]);
ALTER TABLE dbo.[STAFF_CATEGORIAS] WITH CHECK ADD CONSTRAINT [FK_STAFFCAT_STAFF] FOREIGN KEY ([FK_id_staff]) REFERENCES dbo.[STAFF] ([PK_id_staff]);
ALTER TABLE dbo.[TOKEN_ACCESO_CUENTA] WITH CHECK ADD CONSTRAINT [FK_TOKEN_ACCESO_CUENTA_USUARIOS] FOREIGN KEY ([id_usuario]) REFERENCES dbo.[USUARIOS] ([PK_id_usuario]);
ALTER TABLE dbo.[USUARIOS] WITH CHECK ADD CONSTRAINT [FK_USUARIOS_PERSONAS] FOREIGN KEY ([FK_id_persona]) REFERENCES dbo.[PERSONAS] ([PK_id_persona]);
ALTER TABLE dbo.[USUARIOS] WITH CHECK ADD CONSTRAINT [FK_USUARIOS_ROLES] FOREIGN KEY ([FK_id_rol]) REFERENCES dbo.[ROLES] ([PK_id_rol]);
GO

/* ============================ VISTA (PUENTE TRANSITORIO) ============================
   VISTA_ESTADO_DEUDA existe porque el Portal Deportivo la consulta (AsistenciaDao y JugadorDao). La fuente de verdad del
   estado de deuda es la API del Portal Administrativo; esta vista reproduce SUS reglas (DescuentosSql.ApplyDescuentoActivo,
   SqlCalendarioCuotas.EstaVencida) hasta que el Deportivo consuma la API, y despues se elimina.
   Columnas (las que lee el Deportivo):
     FK_id_jugador
     deuda_total              = lo que debe hoy (cuotas e inscripcion pendientes, con la beca ya descontada)
     cantidad_cuotas_vencidas = cuotas con saldo > 0 cuyo vencimiento (dia 10 del mes siguiente) ya llego, hora argentina
   El semaforo: 0 vencidas = habilitado, 1 = solo entrenamientos, 2 o mas = inhabilitado. */
CREATE VIEW dbo.VISTA_ESTADO_DEUDA AS
SELECT s.FK_id_jugador,
       SUM(s.saldo) AS deuda_total,
       COUNT(CASE WHEN s.concepto = 'Cuota'
                   AND DATEADD(DAY, 9, DATEADD(MONTH, 1, s.fecha_vencimiento)) <= CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Argentina Standard Time' AS DATE)
                  THEN 1 END) AS cantidad_cuotas_vencidas
FROM (
    SELECT pg.FK_id_jugador, pg.concepto, pg.fecha_vencimiento,
           CASE WHEN x.saldo_bruto < 0 THEN 0 ELSE x.saldo_bruto END AS saldo
    FROM dbo.PAGOS pg
    OUTER APPLY (
        SELECT TOP (1) jd.tipo_valor, jd.porcentaje, jd.monto_fijo
        FROM dbo.JUGADORES_DESCUENTOS jd
        WHERE jd.FK_id_jugador = pg.FK_id_jugador
          AND pg.concepto = 'Cuota'
          AND jd.fecha_inicio <= EOMONTH(pg.fecha_vencimiento) AND jd.fecha_fin >= pg.fecha_vencimiento
          AND (jd.fecha_cancelacion IS NULL OR pg.fecha_vencimiento <= jd.fecha_cancelacion)
        ORDER BY jd.fecha_inicio DESC
    ) AS d
    CROSS APPLY (
        SELECT CASE WHEN d.tipo_valor = '%' THEN pg.monto_final - (pg.monto_base * d.porcentaje / 100.0)
                    WHEN d.tipo_valor = '$' THEN pg.monto_final - d.monto_fijo
                    ELSE pg.monto_final END AS saldo_bruto
    ) AS x
    WHERE pg.estado = 0
) AS s
WHERE s.saldo > 0
GROUP BY s.FK_id_jugador;
GO

/* ======================== TRIGGERS DE AUDITORIA ======================== */
-- Cada cambio queda en AUDITORIA_CAMBIOS con el operador de SESSION_CONTEXT('idUsuario').

CREATE TRIGGER dbo.TR_AUDIT_ARANCELES ON dbo.ARANCELES AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIOS WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'ARANCELES', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_arancel, d.PK_id_arancel)),
        CASE WHEN d.PK_id_arancel IS NULL THEN 'INSERT' WHEN i.PK_id_arancel IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_arancel IS NULL THEN NULL ELSE (SELECT d.genero, d.FK_id_categoria AS idCategoria, d.monto, d.vigente_desde FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_arancel IS NULL THEN NULL ELSE (SELECT i.genero, i.FK_id_categoria AS idCategoria, i.monto, i.vigente_desde FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_arancel = i.PK_id_arancel;
END;
GO

CREATE TRIGGER dbo.TR_AUDIT_ARANCELES_INSCRIPCION ON dbo.ARANCELES_INSCRIPCION AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIOS WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'ARANCELES_INSCRIPCION', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_arancel_inscripcion, d.PK_id_arancel_inscripcion)),
        CASE WHEN d.PK_id_arancel_inscripcion IS NULL THEN 'INSERT' WHEN i.PK_id_arancel_inscripcion IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_arancel_inscripcion IS NULL THEN NULL ELSE (SELECT d.genero, d.monto, d.vigente_desde FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_arancel_inscripcion IS NULL THEN NULL ELSE (SELECT i.genero, i.monto, i.vigente_desde FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_arancel_inscripcion = i.PK_id_arancel_inscripcion;
END;
GO

CREATE TRIGGER dbo.TR_AUDITORIA_IDENTIDAD_ACTOR
ON dbo.AUDITORIA_CAMBIOS
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    -- Congela la identidad del operador en cada evento. El nombre y el DNI salen de PERSONAS (fuente unica de identidad).
    UPDATE audit
    SET nombre_usuario = persona.nombre,
        apellido_usuario = persona.apellido,
        dni_usuario = persona.dni
    FROM dbo.AUDITORIA_CAMBIOS audit
    INNER JOIN inserted event ON event.PK_id_evento = audit.PK_id_evento
    LEFT JOIN dbo.USUARIOS usuario ON usuario.PK_id_usuario = event.id_usuario
    LEFT JOIN dbo.PERSONAS persona ON persona.PK_id_persona = usuario.FK_id_persona;
END;
GO

CREATE TRIGGER dbo.TR_AUDIT_CATEGORIAS ON dbo.CATEGORIAS AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIOS WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'CATEGORIAS', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_categoria, d.PK_id_categoria)),
        CASE WHEN d.PK_id_categoria IS NULL THEN 'INSERT' WHEN i.PK_id_categoria IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_categoria IS NULL THEN NULL ELSE (SELECT d.nombre_categoria, d.asociacion FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_categoria IS NULL THEN NULL ELSE (SELECT i.nombre_categoria, i.asociacion FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_categoria = i.PK_id_categoria;
END;
GO

CREATE TRIGGER dbo.TR_AUDIT_JUGADORES ON dbo.JUGADORES AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIOS WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'JUGADORES', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_jugador, d.PK_id_jugador)),
        CASE WHEN d.PK_id_jugador IS NULL THEN 'INSERT' WHEN i.PK_id_jugador IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_jugador IS NULL THEN NULL ELSE (SELECT d.FK_id_persona AS idPersona, d.FK_id_categoria AS idCategoria, d.fecha_alta, d.fecha_baja FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_jugador IS NULL THEN NULL ELSE (SELECT i.FK_id_persona AS idPersona, i.FK_id_categoria AS idCategoria, i.fecha_alta, i.fecha_baja FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_jugador = i.PK_id_jugador;
END;
GO

-- Mantiene JUGADORES_PERIODOS sin que ningun sistema tenga que escribirla:
--   jugador nuevo -> abre un periodo; baja -> cierra el abierto; reingreso (baja a NULL) -> abre uno nuevo con la
--   fecha de alta del reingreso; correccion de fechas -> corrige el periodo vigente. Los periodos cerrados no se tocan.
CREATE TRIGGER dbo.TR_JUGADORES_PERIODOS ON dbo.JUGADORES AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @hoy DATE = CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Argentina Standard Time' AS DATE);

    -- Jugador nuevo.
    INSERT dbo.JUGADORES_PERIODOS (FK_id_jugador, fecha_alta, fecha_baja)
    SELECT i.PK_id_jugador, COALESCE(i.fecha_alta, @hoy), i.fecha_baja
    FROM inserted i
    WHERE NOT EXISTS (SELECT 1 FROM deleted d WHERE d.PK_id_jugador = i.PK_id_jugador);

    -- Baja.
    UPDATE p SET fecha_baja = i.fecha_baja
    FROM dbo.JUGADORES_PERIODOS p
    JOIN inserted i ON i.PK_id_jugador = p.FK_id_jugador
    JOIN deleted d ON d.PK_id_jugador = i.PK_id_jugador
    WHERE d.fecha_baja IS NULL AND i.fecha_baja IS NOT NULL AND p.fecha_baja IS NULL;

    -- Reingreso.
    INSERT dbo.JUGADORES_PERIODOS (FK_id_jugador, fecha_alta, fecha_baja)
    SELECT i.PK_id_jugador, COALESCE(i.fecha_alta, @hoy), NULL
    FROM inserted i JOIN deleted d ON d.PK_id_jugador = i.PK_id_jugador
    WHERE d.fecha_baja IS NOT NULL AND i.fecha_baja IS NULL;

    -- Correccion de la fecha de alta del periodo abierto.
    UPDATE p SET fecha_alta = i.fecha_alta
    FROM dbo.JUGADORES_PERIODOS p
    JOIN inserted i ON i.PK_id_jugador = p.FK_id_jugador
    JOIN deleted d ON d.PK_id_jugador = i.PK_id_jugador
    WHERE d.fecha_baja IS NULL AND i.fecha_baja IS NULL AND p.fecha_baja IS NULL
      AND i.fecha_alta IS NOT NULL AND i.fecha_alta <> p.fecha_alta;

    -- Correccion de la fecha de baja del ultimo periodo cerrado.
    UPDATE p SET fecha_baja = i.fecha_baja
    FROM dbo.JUGADORES_PERIODOS p
    JOIN inserted i ON i.PK_id_jugador = p.FK_id_jugador
    JOIN deleted d ON d.PK_id_jugador = i.PK_id_jugador
    WHERE d.fecha_baja IS NOT NULL AND i.fecha_baja IS NOT NULL AND i.fecha_baja <> d.fecha_baja
      AND p.fecha_baja = d.fecha_baja
      AND p.fecha_alta = (SELECT MAX(q.fecha_alta) FROM dbo.JUGADORES_PERIODOS q WHERE q.FK_id_jugador = p.FK_id_jugador);
END;
GO

CREATE TRIGGER dbo.TR_AUDIT_JUGADORES_DESCUENTOS ON dbo.JUGADORES_DESCUENTOS AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIOS WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'JUGADORES_DESCUENTOS', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_jugador_descuento, d.PK_id_jugador_descuento)),
        CASE WHEN d.PK_id_jugador_descuento IS NULL THEN 'INSERT' WHEN i.PK_id_jugador_descuento IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_jugador_descuento IS NULL THEN NULL ELSE (SELECT d.FK_id_jugador AS idJugador, d.FK_id_descuento AS idTipo, d.estado_activo AS activo, d.tipo_valor AS tipoValor, d.porcentaje, d.monto_fijo AS montoFijo, d.fecha_inicio AS desde, d.fecha_fin AS hasta, d.fecha_cancelacion AS fechaCancelacion, d.anulado, d.cancelada_por_baja AS canceladaPorBaja FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_jugador_descuento IS NULL THEN NULL ELSE (SELECT i.FK_id_jugador AS idJugador, i.FK_id_descuento AS idTipo, i.estado_activo AS activo, i.tipo_valor AS tipoValor, i.porcentaje, i.monto_fijo AS montoFijo, i.fecha_inicio AS desde, i.fecha_fin AS hasta, i.fecha_cancelacion AS fechaCancelacion, i.anulado, i.cancelada_por_baja AS canceladaPorBaja FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_jugador_descuento = i.PK_id_jugador_descuento;
END;
GO

CREATE TRIGGER dbo.TR_AUDIT_PAGOS ON dbo.PAGOS AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIOS WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'PAGOS', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_pago, d.PK_id_pago)),
        CASE WHEN d.PK_id_pago IS NULL THEN 'INSERT' WHEN i.PK_id_pago IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_pago IS NULL THEN NULL ELSE (SELECT d.FK_id_jugador AS idJugador, d.monto_base AS montoBase, d.FK_id_jugador_descuento AS idDescuento, d.monto_final AS montoFinal, d.fecha_pago AS fechaPago, d.metodo_pago AS metodoPago, d.fecha_vencimiento AS vencimiento, d.estado, d.concepto FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_pago IS NULL THEN NULL ELSE (SELECT i.FK_id_jugador AS idJugador, i.monto_base AS montoBase, i.FK_id_jugador_descuento AS idDescuento, i.monto_final AS montoFinal, i.fecha_pago AS fechaPago, i.metodo_pago AS metodoPago, i.fecha_vencimiento AS vencimiento, i.estado, i.concepto FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_pago = i.PK_id_pago;
END;
GO

CREATE TRIGGER dbo.TR_AUDIT_PERSONAS ON dbo.PERSONAS AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIOS WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion)
    SELECT @actor, @email, N'PERSONAS', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_persona, d.PK_id_persona)),
        CASE WHEN d.PK_id_persona IS NULL THEN 'INSERT' WHEN i.PK_id_persona IS NULL THEN 'DELETE' ELSE 'UPDATE' END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_persona = i.PK_id_persona;
END;
GO

CREATE TRIGGER dbo.TR_AUDIT_TIPOS_DESCUENTO ON dbo.TIPOS_DESCUENTO AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIOS WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'TIPOS_DESCUENTO', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_descuento, d.PK_id_descuento)),
        CASE WHEN d.PK_id_descuento IS NULL THEN 'INSERT' WHEN i.PK_id_descuento IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_descuento IS NULL THEN NULL ELSE (SELECT d.tipo_descuento FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_descuento IS NULL THEN NULL ELSE (SELECT i.tipo_descuento FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_descuento = i.PK_id_descuento;
END;
GO

CREATE TRIGGER dbo.TR_AUDIT_TOKEN_ACCESO_CUENTA ON dbo.TOKEN_ACCESO_CUENTA AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = COALESCE(TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario')), (SELECT TOP (1) id_usuario FROM inserted));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIOS WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'TOKEN_ACCESO_CUENTA', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_token, d.PK_id_token)),
        CASE WHEN d.PK_id_token IS NULL THEN 'INSERT' WHEN i.PK_id_token IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_token IS NULL THEN NULL ELSE (SELECT d.id_usuario AS idUsuario, d.tipo, d.vence_utc AS venceUtc, d.consumido_utc AS consumidoUtc FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_token IS NULL THEN NULL ELSE (SELECT i.id_usuario AS idUsuario, i.tipo, i.vence_utc AS venceUtc, i.consumido_utc AS consumidoUtc FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_token = i.PK_id_token;
END;
GO

CREATE TRIGGER dbo.TR_AUDIT_USUARIOS ON dbo.USUARIOS AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIOS WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'USUARIOS', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_usuario, d.PK_id_usuario)),
        CASE WHEN d.PK_id_usuario IS NULL THEN 'INSERT' WHEN i.PK_id_usuario IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_usuario IS NULL THEN NULL ELSE
            (SELECT d.email, d.FK_id_rol AS idRol, d.activo,
                d.activacion_pendiente AS activacionPendiente, d.acceso_portal AS accesoPortal, d.rol_portal AS rolPortal,
                CONVERT(bit, CASE WHEN i.PK_id_usuario IS NOT NULL AND ISNULL(d.contrasenia, '') <> ISNULL(i.contrasenia, '') THEN 1 ELSE 0 END) AS credencialActualizada
             FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_usuario IS NULL THEN NULL ELSE
            (SELECT i.email, i.FK_id_rol AS idRol, i.activo,
                i.activacion_pendiente AS activacionPendiente, i.acceso_portal AS accesoPortal, i.rol_portal AS rolPortal,
                CONVERT(bit, CASE WHEN d.PK_id_usuario IS NOT NULL AND ISNULL(d.contrasenia, '') <> ISNULL(i.contrasenia, '') THEN 1 ELSE 0 END) AS credencialActualizada
             FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_usuario = i.PK_id_usuario;
END;
GO

/* ============ HISTORIAL DE MIGRACIONES (lo usa la API del Portal Administrativo) ============ */
-- Las 20 migraciones de database/migrations/ del repositorio ya estan incluidas en esta estructura. Se registran con
-- el mismo nombre y el mismo hash (SHA-256 del texto normalizado) que calcula ApiGestion.Database.Migracion, asi la API
-- no intenta volver a aplicarlas. Una migracion nueva (V2026101x en adelante) se aplicara normalmente.
INSERT dbo.[__CaccMigraciones] ([Nombre], [Hash]) VALUES (N'V20260923_01__crear_aranceles.sql', 'A97278DCA85530B2967796CDFE6219442FFD289B6D9904772F4A64D91C5287C8');
INSERT dbo.[__CaccMigraciones] ([Nombre], [Hash]) VALUES (N'V20260923_02__index_pagos_pendientes.sql', 'B3E4563795F9B78279DD8E65AAB28547081FAEB36F14765388622F74EB292698');
INSERT dbo.[__CaccMigraciones] ([Nombre], [Hash]) VALUES (N'V20260930_01__verificar_base_equipo.sql', '975002FF7C7628D604757F43C87768CB21C79F66235F684E7E71F8CEE9691F11');
INSERT dbo.[__CaccMigraciones] ([Nombre], [Hash]) VALUES (N'V20260930_02__agrega_usuario_registro_pagos.sql', '078567DE6345407CE74DA75844FA34DF1AF79A592B972ECC0EEEDC41BA15871F');
INSERT dbo.[__CaccMigraciones] ([Nombre], [Hash]) VALUES (N'V20260930_03__aranceles_agrega_categoria.sql', 'D64308600F7AC43C619D7601405F4DE723756907FAAABB03FB94DB04D64E4496');
INSERT dbo.[__CaccMigraciones] ([Nombre], [Hash]) VALUES (N'V20260930_04__aranceles_reglas_por_genero_o_categoria.sql', '24FC50756168581144154C886389FF2FAAB152199A7320296ADDCF810C32F035');
INSERT dbo.[__CaccMigraciones] ([Nombre], [Hash]) VALUES (N'V20260930_05__auditoria_aranceles_categoria.sql', '91815906C411FCF5EB8012BEAE6ED3BE83EE2EDA4425EFEF05CC0B34126C6060');
INSERT dbo.[__CaccMigraciones] ([Nombre], [Hash]) VALUES (N'V20261004_01__usuario_dni_unico_para_login.sql', 'DED8CA006D1E16A752282E6A4D6E623BCA0C6B690BBD296CD030BDEB6ED2DF85');
INSERT dbo.[__CaccMigraciones] ([Nombre], [Hash]) VALUES (N'V20261010_01__agrega_anulado_descuentos.sql', 'D8BE02FEBC44B80303B35B8AC37E7710A2C6ED513244C57C2910DA7A94A84709');
INSERT dbo.[__CaccMigraciones] ([Nombre], [Hash]) VALUES (N'V20261010_02__marca_descuentos_anulados.sql', '196F8D280DAC02C53F3CE1F11666160E34811E7792F8771D5C88C0D64B3BD342');
INSERT dbo.[__CaccMigraciones] ([Nombre], [Hash]) VALUES (N'V20261011_01__jugadores_agrega_fecha_baja.sql', '98B09B8418C6CAD09736AE78EB2200C9B9CF70BDE45556166CD8C7347510004C');
INSERT dbo.[__CaccMigraciones] ([Nombre], [Hash]) VALUES (N'V20261011_02__auditoria_jugadores_fecha_baja.sql', 'E83C732F9A78F35EC1B5AAE8013DD78F6E447B4DEAED0D59E95CD34669224925');
INSERT dbo.[__CaccMigraciones] ([Nombre], [Hash]) VALUES (N'V20261011_03__crea_pagos_anulados.sql', '1FD41DEB4086C56B39D18CD1B1E9B69080AB7D4A49D0192A8D9539F754805F4D');
INSERT dbo.[__CaccMigraciones] ([Nombre], [Hash]) VALUES (N'V20261011_04__secuencias_ids_pagos_jugadores.sql', '96DA8392EEEF0A1D704ECF9A667AAA3C8E080431F971578B7014492C240914EE');
INSERT dbo.[__CaccMigraciones] ([Nombre], [Hash]) VALUES (N'V20261011_05__defaults_ids_desde_secuencias.sql', '9F868A6974BCB003261456026002E0AA756A12B64C351B5B13A2AF32B4D7D686');
INSERT dbo.[__CaccMigraciones] ([Nombre], [Hash]) VALUES (N'V20261011_06__borra_contrasenias_texto_plano_portal.sql', '5476223B038151BF7AD8C1CB3A3DB819124C15804341B6B35F17CBF224833239');
INSERT dbo.[__CaccMigraciones] ([Nombre], [Hash]) VALUES (N'V20261012_01__descuentos_cancelada_por_baja.sql', 'C5C683283D136046B94A9B9B7E7162DDD472BE7723F2837D9F4207C3B34585BA');
INSERT dbo.[__CaccMigraciones] ([Nombre], [Hash]) VALUES (N'V20261012_02__auditoria_descuentos_anulado_y_baja.sql', '77A1A8CB116F63545BBC788F73CC9DD11B579BD0D258FB89696EBB7587F60C91');
INSERT dbo.[__CaccMigraciones] ([Nombre], [Hash]) VALUES (N'V20261013_01__intentos_login.sql', '38F3DDA371B7F6F13B145B4F05D3C01940D04219A4AC01357BC1B44C64629106');
INSERT dbo.[__CaccMigraciones] ([Nombre], [Hash]) VALUES (N'V20261014_01__descuentos_agrega_fecha_asignacion.sql', 'C0966FF47F0619D0743F36C4637316231C7121AAE4A14D49A800A3D0E816DA56');
GO

/* ========== OPCIONAL: primer SuperAdmin ==========
   La base sale sin usuarios y sin ellos nadie entra. En produccion el primer SuperAdmin se aprovisiona con un procedimiento
   controlado (pendiente de definir). Para una base de PRUEBAS: descomentar, cambiar DNI, correo y contrasena (hash BCrypt)
   y ejecutar. Luego crea su contrasena con "Olvidaste tu contrasena?" si se deja contrasenia en NULL.

INSERT dbo.PERSONAS (nombre, apellido, dni) VALUES (N'Nombre', N'Apellido', '00000000');
INSERT dbo.USUARIOS (FK_id_persona, FK_id_rol, email, contrasenia, activo, acceso_portal, rol_portal)
VALUES (SCOPE_IDENTITY(), 1, 'correo@ejemplo.com', NULL, 1, 1, 1);
*/

SET NOEXEC OFF;
PRINT N'Listo.';
GO
