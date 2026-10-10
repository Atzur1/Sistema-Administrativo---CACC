/* =====================================================================================
   Club Camioneros (CACC) - Creacion de la base desde cero (solo estructura + catalogos)
   -------------------------------------------------------------------------------------
   Generado el 2026-10-05 a partir de la base de desarrollo del equipo del Portal Administrativo.

   QUE HACE
     1. Crea la base ClubCamionerosCACC (collation Modern_Spanish_CI_AS, la misma de la base de desarrollo).
     2. Crea las 20 tablas, claves, restricciones, indices y los 11 triggers de auditoria.
     3. Carga SOLO los catalogos que el sistema necesita para funcionar (ROLES, CATEGORIAS,
        TIPO_DESCUENTO y el arancel de inscripcion vigente). No hay personas, usuarios ni pagos.
     4. Deja registrado el historial de migraciones (dbo.__CaccMigraciones) para que la API del Portal
        Administrativo arranque contra esta base sin volver a aplicar nada.

   COMO USARLO
     - Ejecutarlo completo (SSMS: F5; o sqlcmd -S <servidor> -E -C -I -b -i este_archivo.sql).
       Los GO separan lotes: no hace falta ninguna otra herramienta.
     - Para otro nombre de base, reemplazar ClubCamionerosCACC en las lineas CREATE DATABASE y USE.
     - Es seguro volver a correrlo: si la base ya tiene tablas, no hace nada y avisa.
     - Necesita permiso para crear bases. Si no lo tenes, crea la base vacia a mano y corre el resto.

   LO QUE CONVIENE SABER (diseno, no errores)
     - USUARIO.PK_id_usuario, PAGOS, JUGADORES y otras tablas NO usan IDENTITY: el id se calcula con
       MAX+1 en el codigo de la API. PERSONA, TIPO_DESCUENTO, JUGADORES_DESCUENTOS, ARANCELES,
       ARANCELES_INSCRIPCION, AUDITORIA_CAMBIOS y TOKEN_ACCESO_CUENTA si son IDENTITY.
     - Los triggers TR_AUDIT_* registran cada cambio en AUDITORIA_CAMBIOS; el responsable sale de
       SESSION_CONTEXT('idUsuario'). Si el codigo que escribe no lo setea, queda sin responsable.
       Cualquier INSERT ... OUTPUT sobre una tabla con trigger debe usar OUTPUT ... INTO (error 334).
     - El indice UX_USUARIO_dni_acceso_portal es filtrado: cualquier conexion que escriba en USUARIO
       necesita SET QUOTED_IDENTIFIER ON (el valor por defecto de ADO.NET / SqlClient).
     - El login del portal usa el DNI de USUARIO. Dos cuentas con acceso_portal = 1 no pueden compartirlo.
   ===================================================================================== */

IF DB_ID(N'ClubCamionerosCACC') IS NULL
    CREATE DATABASE [ClubCamionerosCACC] COLLATE Modern_Spanish_CI_AS;
GO

USE [ClubCamionerosCACC];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.USUARIO', N'U') IS NOT NULL OR OBJECT_ID(N'dbo.PERSONA', N'U') IS NOT NULL
BEGIN
    PRINT N'La base ya tiene tablas del sistema: no se hizo ningun cambio.';
    SET NOEXEC ON;
END
GO

/* ================================ TABLAS ================================ */

CREATE TABLE dbo.[__CaccMigraciones] (
    [Nombre] nvarchar(200) NOT NULL,
    [Hash] char(64) NOT NULL,
    [AplicadaEnUtc] datetime2(7) NOT NULL CONSTRAINT [DF___CaccMigraciones_AplicadaEnUtc] DEFAULT (sysutcdatetime()),
    CONSTRAINT [PK___CaccMigraciones] PRIMARY KEY CLUSTERED ([Nombre])
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

CREATE TABLE dbo.[ARANCELES_INSCRIPCION] (
    [PK_id_arancel_inscripcion] int IDENTITY(1,1) NOT NULL,
    [monto] decimal(18,2) NOT NULL,
    [vigente_desde] date NOT NULL,
    CONSTRAINT [PK_ARANCELES_INSCRIPCION] PRIMARY KEY CLUSTERED ([PK_id_arancel_inscripcion]),
    CONSTRAINT [UQ_ARANCELES_INSCRIPCION_FECHA] UNIQUE NONCLUSTERED ([vigente_desde]),
    CONSTRAINT [CK_ARANCELES_INSCRIPCION_MONTO] CHECK ([monto]>(0))
);
GO

CREATE TABLE dbo.[ASISTENCIAS] (
    [PK_id_asistencia] int NOT NULL,
    [FK_id_jugador] int NULL,
    [FK_id_staff] int NULL,
    [fecha_hora] datetime NULL,
    [FK_id_estado_deuda] int NULL,
    [estado_acceso] varchar(50) NULL,
    CONSTRAINT [PK_ASISTENCIAS] PRIMARY KEY CLUSTERED ([PK_id_asistencia])
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
    [PK_id_categoria] int NOT NULL,
    [nombre_categoria] varchar(100) NULL,
    CONSTRAINT [PK_CATEGORIAS] PRIMARY KEY CLUSTERED ([PK_id_categoria])
);
GO

CREATE TABLE dbo.[FICHAS_MEDICAS] (
    [PK_id_ficha_medica] int NOT NULL,
    [FK_id_jugador] int NULL,
    [obra_social] varchar(150) NULL,
    [numero_afiliado_OS] int NULL,
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

CREATE TABLE dbo.[Jugadore_Responsable] (
    [PK_id_jugador_responsable] int NOT NULL,
    [FK_id_Responsable] int NULL,
    [FK_id_Jugador] int NULL,
    CONSTRAINT [PK_Jugadore_Responsable] PRIMARY KEY CLUSTERED ([PK_id_jugador_responsable])
);
GO

CREATE TABLE dbo.[JUGADORES] (
    [PK_id_jugador] int NOT NULL,
    [FK_id_persona] int NULL,
    [FK_id_categoria] int NULL,
    [club_origen] varchar(100) NULL,
    [FK_id_jugador_responsable] int NULL,
    [FK_id_Ficha_Medica] int NULL,
    [Ficha_medica_liga] bit NULL,
    [Posicion_cancha] varchar(50) NULL,
    [fecha_alta] date NULL,
    CONSTRAINT [PK_JUGADORES] PRIMARY KEY CLUSTERED ([PK_id_jugador])
);
GO

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
    CONSTRAINT [PK_JUGADORES_DESCUENTOS] PRIMARY KEY CLUSTERED ([PK_id_jugador_descuento]),
    CONSTRAINT [CK_JUGDESC_MONTO_POSITIVO] CHECK ([monto_fijo] IS NULL OR [monto_fijo]>(0)),
    CONSTRAINT [CK_JUGDESC_PORCENTAJE_RANGO] CHECK ([porcentaje] IS NULL OR [porcentaje]>(0) AND [porcentaje]<=(100)),
    CONSTRAINT [CK_JUGDESC_TIPO_VALOR] CHECK ([tipo_valor]='$' OR [tipo_valor]='%'),
    CONSTRAINT [CK_JUGDESC_VALOR_EXCLUYENTE] CHECK ([tipo_valor]='%' AND [porcentaje] IS NOT NULL AND [monto_fijo] IS NULL OR [tipo_valor]='$' AND [monto_fijo] IS NOT NULL AND [porcentaje] IS NULL),
    CONSTRAINT [CK_JUGDESC_VIGENCIA] CHECK ([fecha_fin]>[fecha_inicio])
);
GO

CREATE TABLE dbo.[PAGOS] (
    [PK_id_pago] int NOT NULL,
    [FK_id_jugador] int NULL,
    [monto_base] decimal(18,2) NULL,
    [FK_id_jugador_descuento] int NULL,
    [monto_final] decimal(18,2) NULL,
    [fecha_pago] date NULL,
    [metodo_pago] varchar(50) NULL,
    [fecha_vencimiento] date NULL,
    [estado] bit NULL,
    [fecha_hora_registro] datetime2(7) NULL,
    [concepto] varchar(20) NOT NULL CONSTRAINT [DF_PAGOS_CONCEPTO] DEFAULT ('Cuota'),
    [FK_id_usuario_registro] int NULL,
    CONSTRAINT [PK_PAGOS] PRIMARY KEY CLUSTERED ([PK_id_pago]),
    CONSTRAINT [CK_PAGOS_CONCEPTO] CHECK ([concepto]='Inscripcion' OR [concepto]='Cuota')
);
GO

CREATE TABLE dbo.[PERSONA] (
    [PK_id_persona] int IDENTITY(1,1) NOT NULL,
    [genero] varchar(50) NULL,
    [fecha_de_nacimiento] date NULL,
    [Dni] varchar(20) NULL,
    [nombre] varchar(100) NULL,
    [apellido] varchar(100) NULL,
    [QR_TOKEN] varchar(255) NULL,
    [FOTO] varbinary(MAX) NULL,
    CONSTRAINT [PK_PERSONA] PRIMARY KEY CLUSTERED ([PK_id_persona])
);
GO

CREATE TABLE dbo.[RESPONSABLES] (
    [PK_id_responsable] int NOT NULL,
    [FK_id_persona] int NULL,
    [email] varchar(255) NULL,
    [telefono] varchar(50) NULL,
    CONSTRAINT [PK_RESPONSABLES] PRIMARY KEY CLUSTERED ([PK_id_responsable])
);
GO

CREATE TABLE dbo.[ROLES] (
    [PK_id_rol] int NOT NULL,
    [nombre_rol] varchar(100) NULL,
    [Permisos] varchar(255) NULL,
    CONSTRAINT [PK_ROLES] PRIMARY KEY CLUSTERED ([PK_id_rol])
);
GO

CREATE TABLE dbo.[STAFF] (
    [PK_id_staff] int NOT NULL,
    [FK_id_usuario] int NULL,
    [FK_id_staff_categoria] int NULL,
    CONSTRAINT [PK_STAFF] PRIMARY KEY CLUSTERED ([PK_id_staff])
);
GO

CREATE TABLE dbo.[STAFF_CATEGORIA] (
    [PK_id_staff_categoria] int NOT NULL,
    [FK_id_staff] int NULL,
    [FK_id_categoria] int NULL,
    CONSTRAINT [PK_STAFF_CATEGORIA] PRIMARY KEY CLUSTERED ([PK_id_staff_categoria])
);
GO

CREATE TABLE dbo.[TIPO_DESCUENTO] (
    [PK_id_descuento] int IDENTITY(1,1) NOT NULL,
    [tipo_descuento] varchar(100) NOT NULL,
    CONSTRAINT [PK_TIPO_DESCUENTO] PRIMARY KEY CLUSTERED ([PK_id_descuento])
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

CREATE TABLE dbo.[USUARIO] (
    [PK_id_usuario] int NOT NULL,
    [FK_id_persona] int NULL,
    [FK_id_rol] int NULL,
    [email] varchar(255) NULL,
    [contrasenia] nvarchar(MAX) NULL,
    [activo] bit NULL,
    [password_hash] nvarchar(512) NULL,
    [activacion_pendiente] bit NOT NULL CONSTRAINT [DF_USUARIO_activacion_pendiente] DEFAULT ((0)),
    [token_version] int NOT NULL CONSTRAINT [DF_USUARIO_token_version] DEFAULT ((0)),
    [nombre] nvarchar(100) NULL,
    [apellido] nvarchar(100) NULL,
    [dni] nvarchar(20) NULL,
    [acceso_portal] bit NOT NULL CONSTRAINT [DF_USUARIO_acceso_portal] DEFAULT ((0)),
    [rol_portal] int NULL,
    CONSTRAINT [PK_USUARIO] PRIMARY KEY CLUSTERED ([PK_id_usuario]),
    CONSTRAINT [CK_USUARIO_acceso_portal_requiere_rol] CHECK ([acceso_portal]=(0) OR [rol_portal] IS NOT NULL),
    CONSTRAINT [CK_USUARIO_rol_portal_valores] CHECK ([rol_portal] IS NULL OR ([rol_portal]=(2) OR [rol_portal]=(1)))
);
GO

CREATE TABLE dbo.[VISTA_ESTADO_DEUDA] (
    [PK_id_estado_deuda] int NOT NULL,
    [FK_id_jugador] int NULL,
    [FK_id_pago] int NULL,
    [deuda] decimal(18,2) NULL,
    CONSTRAINT [PK_VISTA_ESTADO_DEUDA] PRIMARY KEY CLUSTERED ([PK_id_estado_deuda])
);
GO

/* ===================== CATALOGOS (sin datos de personas) ===================== */
-- Se cargan antes de crear los triggers y las claves foraneas, para no llenar la auditoria con la carga inicial.

-- ROLES (2 filas)
INSERT dbo.[ROLES] ([PK_id_rol], [nombre_rol], [Permisos]) VALUES (1, N'Administrador General', N'ALL');
INSERT dbo.[ROLES] ([PK_id_rol], [nombre_rol], [Permisos]) VALUES (2, N'Administrador', N'COBRANZAS');

-- CATEGORIAS (13 filas)
INSERT dbo.[CATEGORIAS] ([PK_id_categoria], [nombre_categoria]) VALUES (1, N'AFA 20067');
INSERT dbo.[CATEGORIAS] ([PK_id_categoria], [nombre_categoria]) VALUES (2, N'Pt  AFA 2008');
INSERT dbo.[CATEGORIAS] ([PK_id_categoria], [nombre_categoria]) VALUES (3, N'Pt  AFA 2009');
INSERT dbo.[CATEGORIAS] ([PK_id_categoria], [nombre_categoria]) VALUES (4, N'Pt  AFA 2010');
INSERT dbo.[CATEGORIAS] ([PK_id_categoria], [nombre_categoria]) VALUES (5, N'Pt  AFA 2011');
INSERT dbo.[CATEGORIAS] ([PK_id_categoria], [nombre_categoria]) VALUES (6, N'Pt  AFA 2012');
INSERT dbo.[CATEGORIAS] ([PK_id_categoria], [nombre_categoria]) VALUES (7, N'Pt  preAFA 2013');
INSERT dbo.[CATEGORIAS] ([PK_id_categoria], [nombre_categoria]) VALUES (8, N'Pt  preAFA 2014');
INSERT dbo.[CATEGORIAS] ([PK_id_categoria], [nombre_categoria]) VALUES (9, N'Pt  preAFA 2015');
INSERT dbo.[CATEGORIAS] ([PK_id_categoria], [nombre_categoria]) VALUES (10, N'Pt 2016');
INSERT dbo.[CATEGORIAS] ([PK_id_categoria], [nombre_categoria]) VALUES (11, N'Pt 2017');
INSERT dbo.[CATEGORIAS] ([PK_id_categoria], [nombre_categoria]) VALUES (12, N'Cebollitas');
INSERT dbo.[CATEGORIAS] ([PK_id_categoria], [nombre_categoria]) VALUES (13, N'Femenino');

-- TIPO_DESCUENTO (2 filas)
SET IDENTITY_INSERT dbo.[TIPO_DESCUENTO] ON;
INSERT dbo.[TIPO_DESCUENTO] ([PK_id_descuento], [tipo_descuento]) VALUES (1, N'Becado');
INSERT dbo.[TIPO_DESCUENTO] ([PK_id_descuento], [tipo_descuento]) VALUES (3, N'Descuento');
SET IDENTITY_INSERT dbo.[TIPO_DESCUENTO] OFF;

-- ARANCELES_INSCRIPCION (1 filas)
SET IDENTITY_INSERT dbo.[ARANCELES_INSCRIPCION] ON;
INSERT dbo.[ARANCELES_INSCRIPCION] ([PK_id_arancel_inscripcion], [monto], [vigente_desde]) VALUES (1, 70000.00, '2026-09-28');
SET IDENTITY_INSERT dbo.[ARANCELES_INSCRIPCION] OFF;

GO

/* ================================ INDICES ================================ */

CREATE UNIQUE NONCLUSTERED INDEX [UX_ARANCELES_CATEGORIA_FECHA] ON dbo.[ARANCELES] ([FK_id_categoria], [vigente_desde]) WHERE ([FK_id_categoria] IS NOT NULL);
CREATE UNIQUE NONCLUSTERED INDEX [UX_ARANCELES_GENERO_FECHA] ON dbo.[ARANCELES] ([genero], [vigente_desde]) WHERE ([genero] IS NOT NULL);
CREATE NONCLUSTERED INDEX [IX_AUDITORIA_CAMBIOS_entidad_evento] ON dbo.[AUDITORIA_CAMBIOS] ([entidad], [id_entidad], [accion], [fecha_utc], [PK_id_evento]) INCLUDE ([id_usuario]);
CREATE NONCLUSTERED INDEX [IX_AUDITORIA_CAMBIOS_fecha] ON dbo.[AUDITORIA_CAMBIOS] ([fecha_utc] DESC, [PK_id_evento] DESC);
CREATE NONCLUSTERED INDEX [IX_AUDITORIA_CAMBIOS_usuario_fecha] ON dbo.[AUDITORIA_CAMBIOS] ([id_usuario], [fecha_utc] DESC, [PK_id_evento] DESC);
CREATE NONCLUSTERED INDEX [IX_JUGDESC_JUGADOR_VIGENCIA] ON dbo.[JUGADORES_DESCUENTOS] ([FK_id_jugador], [estado_activo], [fecha_inicio], [fecha_fin]);
CREATE NONCLUSTERED INDEX [IX_PAGOS_Estado_Pendientes] ON dbo.[PAGOS] ([estado]) INCLUDE ([FK_id_jugador], [monto_base], [monto_final], [fecha_vencimiento], [concepto]);
CREATE NONCLUSTERED INDEX [IX_PAGOS_Jugador_Concepto_Periodo_Estado] ON dbo.[PAGOS] ([FK_id_jugador], [concepto], [fecha_vencimiento], [estado]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_PAGOS_CuotaPendiente_Jugador_Periodo] ON dbo.[PAGOS] ([FK_id_jugador], [fecha_vencimiento], [concepto]) WHERE ([estado]=(0) AND [concepto]='Cuota' AND [fecha_vencimiento] IS NOT NULL);
CREATE UNIQUE NONCLUSTERED INDEX [UX_PAGOS_InscripcionPendiente_Jugador] ON dbo.[PAGOS] ([FK_id_jugador]) WHERE ([estado]=(0) AND [concepto]='Inscripcion');
CREATE UNIQUE NONCLUSTERED INDEX [UX_TIPODESC_NOMBRE] ON dbo.[TIPO_DESCUENTO] ([tipo_descuento]);
CREATE NONCLUSTERED INDEX [IX_TOKEN_ACCESO_CUENTA_usuario_tipo] ON dbo.[TOKEN_ACCESO_CUENTA] ([id_usuario], [tipo], [consumido_utc], [vence_utc]);

-- Login por DNI (migracion V20261004_01): un DNI identifica a una sola cuenta con acceso al portal.
-- Filtrado a acceso_portal = 1 a proposito: USUARIO es compartida con el otro equipo y una restriccion sobre toda
-- la tabla podria rechazar sus altas. Quien todavia no tiene acceso se normaliza al habilitarlo.
CREATE UNIQUE INDEX [UX_USUARIO_dni_acceso_portal] ON dbo.[USUARIO] ([dni]) WHERE acceso_portal = 1 AND dni IS NOT NULL AND dni <> '';
GO

/* ============================= CLAVES FORANEAS ============================= */

ALTER TABLE dbo.[ARANCELES] WITH CHECK ADD CONSTRAINT [FK_ARANCELES_CATEGORIAS] FOREIGN KEY ([FK_id_categoria]) REFERENCES dbo.[CATEGORIAS] ([PK_id_categoria]);
ALTER TABLE dbo.[ASISTENCIAS] WITH CHECK ADD CONSTRAINT [FK_ASISTENCIA_ESTADODEUDA] FOREIGN KEY ([FK_id_estado_deuda]) REFERENCES dbo.[VISTA_ESTADO_DEUDA] ([PK_id_estado_deuda]);
ALTER TABLE dbo.[ASISTENCIAS] WITH CHECK ADD CONSTRAINT [FK_ASISTENCIA_JUGADOR] FOREIGN KEY ([FK_id_jugador]) REFERENCES dbo.[JUGADORES] ([PK_id_jugador]);
ALTER TABLE dbo.[ASISTENCIAS] WITH CHECK ADD CONSTRAINT [FK_ASISTENCIA_STAFF] FOREIGN KEY ([FK_id_staff]) REFERENCES dbo.[STAFF] ([PK_id_staff]);
ALTER TABLE dbo.[FICHAS_MEDICAS] WITH CHECK ADD CONSTRAINT [FK_FichMed_Jugador] FOREIGN KEY ([FK_id_jugador]) REFERENCES dbo.[JUGADORES] ([PK_id_jugador]);
ALTER TABLE dbo.[Jugadore_Responsable] WITH CHECK ADD CONSTRAINT [FK_JugRes_Jugador] FOREIGN KEY ([FK_id_Jugador]) REFERENCES dbo.[JUGADORES] ([PK_id_jugador]);
ALTER TABLE dbo.[Jugadore_Responsable] WITH CHECK ADD CONSTRAINT [FK_JugRes_Responsable] FOREIGN KEY ([FK_id_Responsable]) REFERENCES dbo.[RESPONSABLES] ([PK_id_responsable]);
ALTER TABLE dbo.[JUGADORES] WITH CHECK ADD CONSTRAINT [FK_JUGADOR_CATEGORIA] FOREIGN KEY ([FK_id_categoria]) REFERENCES dbo.[CATEGORIAS] ([PK_id_categoria]);
ALTER TABLE dbo.[JUGADORES] WITH CHECK ADD CONSTRAINT [FK_JUGADOR_FICHAMEDICA] FOREIGN KEY ([FK_id_Ficha_Medica]) REFERENCES dbo.[FICHAS_MEDICAS] ([PK_id_ficha_medica]);
ALTER TABLE dbo.[JUGADORES] WITH CHECK ADD CONSTRAINT [FK_JUGADOR_JUGRES] FOREIGN KEY ([FK_id_jugador_responsable]) REFERENCES dbo.[Jugadore_Responsable] ([PK_id_jugador_responsable]);
ALTER TABLE dbo.[JUGADORES] WITH CHECK ADD CONSTRAINT [FK_JUGADOR_PERSONA] FOREIGN KEY ([FK_id_persona]) REFERENCES dbo.[PERSONA] ([PK_id_persona]);
ALTER TABLE dbo.[JUGADORES_DESCUENTOS] WITH CHECK ADD CONSTRAINT [FK_JUGDESC_JUGADOR] FOREIGN KEY ([FK_id_jugador]) REFERENCES dbo.[JUGADORES] ([PK_id_jugador]);
ALTER TABLE dbo.[JUGADORES_DESCUENTOS] WITH CHECK ADD CONSTRAINT [FK_JUGDESC_TIPODESC] FOREIGN KEY ([FK_id_descuento]) REFERENCES dbo.[TIPO_DESCUENTO] ([PK_id_descuento]);
ALTER TABLE dbo.[PAGOS] WITH CHECK ADD CONSTRAINT [FK_PAGOS_JUGADOR] FOREIGN KEY ([FK_id_jugador]) REFERENCES dbo.[JUGADORES] ([PK_id_jugador]);
ALTER TABLE dbo.[PAGOS] WITH CHECK ADD CONSTRAINT [FK_PAGOS_JUGDESC] FOREIGN KEY ([FK_id_jugador_descuento]) REFERENCES dbo.[JUGADORES_DESCUENTOS] ([PK_id_jugador_descuento]);
ALTER TABLE dbo.[PAGOS] WITH CHECK ADD CONSTRAINT [FK_PAGOS_USUARIO_REGISTRO] FOREIGN KEY ([FK_id_usuario_registro]) REFERENCES dbo.[USUARIO] ([PK_id_usuario]);
ALTER TABLE dbo.[RESPONSABLES] WITH CHECK ADD CONSTRAINT [FK_RESPONSABLE_PERSONA] FOREIGN KEY ([FK_id_persona]) REFERENCES dbo.[PERSONA] ([PK_id_persona]);
ALTER TABLE dbo.[STAFF] WITH CHECK ADD CONSTRAINT [FK_STAFF_STAFFCAT] FOREIGN KEY ([FK_id_staff_categoria]) REFERENCES dbo.[STAFF_CATEGORIA] ([PK_id_staff_categoria]);
ALTER TABLE dbo.[STAFF] WITH CHECK ADD CONSTRAINT [FK_STAFF_USUARIO] FOREIGN KEY ([FK_id_usuario]) REFERENCES dbo.[USUARIO] ([PK_id_usuario]);
ALTER TABLE dbo.[STAFF_CATEGORIA] WITH CHECK ADD CONSTRAINT [FK_STAFFCAT_CATEGORIA] FOREIGN KEY ([FK_id_categoria]) REFERENCES dbo.[CATEGORIAS] ([PK_id_categoria]);
ALTER TABLE dbo.[STAFF_CATEGORIA] WITH CHECK ADD CONSTRAINT [FK_STAFFCAT_STAFF] FOREIGN KEY ([FK_id_staff]) REFERENCES dbo.[STAFF] ([PK_id_staff]);
ALTER TABLE dbo.[TOKEN_ACCESO_CUENTA] WITH CHECK ADD CONSTRAINT [FK_TOKEN_ACCESO_CUENTA_USUARIO] FOREIGN KEY ([id_usuario]) REFERENCES dbo.[USUARIO] ([PK_id_usuario]);
ALTER TABLE dbo.[USUARIO] WITH CHECK ADD CONSTRAINT [FK_USUARIO_PERSONA] FOREIGN KEY ([FK_id_persona]) REFERENCES dbo.[PERSONA] ([PK_id_persona]);
ALTER TABLE dbo.[USUARIO] WITH CHECK ADD CONSTRAINT [FK_USUARIO_ROLES] FOREIGN KEY ([FK_id_rol]) REFERENCES dbo.[ROLES] ([PK_id_rol]);
ALTER TABLE dbo.[VISTA_ESTADO_DEUDA] WITH CHECK ADD CONSTRAINT [FK_DEUDA_JUGADOR] FOREIGN KEY ([FK_id_jugador]) REFERENCES dbo.[JUGADORES] ([PK_id_jugador]);
ALTER TABLE dbo.[VISTA_ESTADO_DEUDA] WITH CHECK ADD CONSTRAINT [FK_DEUDA_PAGO] FOREIGN KEY ([FK_id_pago]) REFERENCES dbo.[PAGOS] ([PK_id_pago]);
GO

/* ======================== TRIGGERS DE AUDITORIA ======================== */

-- TR_AUDIT_ARANCELES sobre ARANCELES
GO
-- La auditoria de ARANCELES ahora tambien registra la categoria (idCategoria), ya que un
-- arancel puede ser por genero o por categoria. Mismo trigger de V20260929_03 con ese campo extra.
CREATE   TRIGGER dbo.TR_AUDIT_ARANCELES ON dbo.ARANCELES AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIO WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'ARANCELES', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_arancel, d.PK_id_arancel)),
        CASE WHEN d.PK_id_arancel IS NULL THEN 'INSERT' WHEN i.PK_id_arancel IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_arancel IS NULL THEN NULL ELSE (SELECT d.genero, d.FK_id_categoria AS idCategoria, d.monto, d.vigente_desde FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_arancel IS NULL THEN NULL ELSE (SELECT i.genero, i.FK_id_categoria AS idCategoria, i.monto, i.vigente_desde FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_arancel = i.PK_id_arancel;
END;
GO

-- TR_AUDIT_ARANCELES_INSCRIPCION sobre ARANCELES_INSCRIPCION
GO
CREATE   TRIGGER dbo.TR_AUDIT_ARANCELES_INSCRIPCION ON dbo.ARANCELES_INSCRIPCION AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIO WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'ARANCELES_INSCRIPCION', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_arancel_inscripcion, d.PK_id_arancel_inscripcion)),
        CASE WHEN d.PK_id_arancel_inscripcion IS NULL THEN 'INSERT' WHEN i.PK_id_arancel_inscripcion IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_arancel_inscripcion IS NULL THEN NULL ELSE (SELECT d.monto, d.vigente_desde FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_arancel_inscripcion IS NULL THEN NULL ELSE (SELECT i.monto, i.vigente_desde FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_arancel_inscripcion = i.PK_id_arancel_inscripcion;
END;
GO

-- TR_AUDITORIA_IDENTIDAD_ACTOR sobre AUDITORIA_CAMBIOS
GO
-- Freeze the operator identity on each audit event. Historical rows remain NULL
-- because the identity at the time cannot be reconstructed reliably.
CREATE   TRIGGER dbo.TR_AUDITORIA_IDENTIDAD_ACTOR
ON dbo.AUDITORIA_CAMBIOS
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE audit
    SET nombre_usuario = usuario.nombre,
        apellido_usuario = usuario.apellido,
        dni_usuario = usuario.dni
    FROM dbo.AUDITORIA_CAMBIOS audit
    INNER JOIN inserted event ON event.PK_id_evento = audit.PK_id_evento
    LEFT JOIN dbo.USUARIO usuario ON usuario.PK_id_usuario = event.id_usuario;
END;
GO

-- TR_AUDIT_CATEGORIAS sobre CATEGORIAS
GO
CREATE   TRIGGER dbo.TR_AUDIT_CATEGORIAS ON dbo.CATEGORIAS AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIO WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'CATEGORIAS', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_categoria, d.PK_id_categoria)),
        CASE WHEN d.PK_id_categoria IS NULL THEN 'INSERT' WHEN i.PK_id_categoria IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_categoria IS NULL THEN NULL ELSE (SELECT d.nombre_categoria FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_categoria IS NULL THEN NULL ELSE (SELECT i.nombre_categoria FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_categoria = i.PK_id_categoria;
END;
GO

-- TR_AUDIT_JUGADORES sobre JUGADORES
GO
CREATE   TRIGGER dbo.TR_AUDIT_JUGADORES ON dbo.JUGADORES AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIO WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'JUGADORES', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_jugador, d.PK_id_jugador)),
        CASE WHEN d.PK_id_jugador IS NULL THEN 'INSERT' WHEN i.PK_id_jugador IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_jugador IS NULL THEN NULL ELSE (SELECT d.FK_id_persona AS idPersona, d.FK_id_categoria AS idCategoria, d.fecha_alta FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_jugador IS NULL THEN NULL ELSE (SELECT i.FK_id_persona AS idPersona, i.FK_id_categoria AS idCategoria, i.fecha_alta FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_jugador = i.PK_id_jugador;
END;
GO

-- TR_AUDIT_JUGADORES_DESCUENTOS sobre JUGADORES_DESCUENTOS
GO
CREATE   TRIGGER dbo.TR_AUDIT_JUGADORES_DESCUENTOS ON dbo.JUGADORES_DESCUENTOS AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIO WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'JUGADORES_DESCUENTOS', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_jugador_descuento, d.PK_id_jugador_descuento)),
        CASE WHEN d.PK_id_jugador_descuento IS NULL THEN 'INSERT' WHEN i.PK_id_jugador_descuento IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_jugador_descuento IS NULL THEN NULL ELSE (SELECT d.FK_id_jugador AS idJugador, d.FK_id_descuento AS idTipo, d.estado_activo AS activo, d.tipo_valor AS tipoValor, d.porcentaje, d.monto_fijo AS montoFijo, d.fecha_inicio AS desde, d.fecha_fin AS hasta, d.fecha_cancelacion AS fechaCancelacion FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_jugador_descuento IS NULL THEN NULL ELSE (SELECT i.FK_id_jugador AS idJugador, i.FK_id_descuento AS idTipo, i.estado_activo AS activo, i.tipo_valor AS tipoValor, i.porcentaje, i.monto_fijo AS montoFijo, i.fecha_inicio AS desde, i.fecha_fin AS hasta, i.fecha_cancelacion AS fechaCancelacion FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_jugador_descuento = i.PK_id_jugador_descuento;
END;
GO

-- TR_AUDIT_PAGOS sobre PAGOS
GO
-- Triggers write the audit row in the same transaction as each domain change.
-- Actor context is set by SqlConnectionFactory from the validated JWT claim.
CREATE   TRIGGER dbo.TR_AUDIT_PAGOS ON dbo.PAGOS AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIO WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'PAGOS', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_pago, d.PK_id_pago)),
        CASE WHEN d.PK_id_pago IS NULL THEN 'INSERT' WHEN i.PK_id_pago IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_pago IS NULL THEN NULL ELSE (SELECT d.FK_id_jugador AS idJugador, d.monto_base AS montoBase, d.FK_id_jugador_descuento AS idDescuento, d.monto_final AS montoFinal, d.fecha_pago AS fechaPago, d.metodo_pago AS metodoPago, d.fecha_vencimiento AS vencimiento, d.estado, d.concepto FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_pago IS NULL THEN NULL ELSE (SELECT i.FK_id_jugador AS idJugador, i.monto_base AS montoBase, i.FK_id_jugador_descuento AS idDescuento, i.monto_final AS montoFinal, i.fecha_pago AS fechaPago, i.metodo_pago AS metodoPago, i.fecha_vencimiento AS vencimiento, i.estado, i.concepto FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_pago = i.PK_id_pago;
END;
GO

-- TR_AUDIT_PERSONA sobre PERSONA
GO
-- PERSONA contains sensitive identity data. Record the affected row and action but
-- deliberately do not duplicate DNI, name, date of birth, or other personal values.
CREATE   TRIGGER dbo.TR_AUDIT_PERSONA ON dbo.PERSONA AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIO WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion)
    SELECT @actor, @email, N'PERSONA', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_persona, d.PK_id_persona)),
        CASE WHEN d.PK_id_persona IS NULL THEN 'INSERT' WHEN i.PK_id_persona IS NULL THEN 'DELETE' ELSE 'UPDATE' END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_persona = i.PK_id_persona;
END;
GO

-- TR_AUDIT_TIPO_DESCUENTO sobre TIPO_DESCUENTO
GO
CREATE   TRIGGER dbo.TR_AUDIT_TIPO_DESCUENTO ON dbo.TIPO_DESCUENTO AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIO WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'TIPO_DESCUENTO', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_descuento, d.PK_id_descuento)),
        CASE WHEN d.PK_id_descuento IS NULL THEN 'INSERT' WHEN i.PK_id_descuento IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_descuento IS NULL THEN NULL ELSE (SELECT d.tipo_descuento FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_descuento IS NULL THEN NULL ELSE (SELECT i.tipo_descuento FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_descuento = i.PK_id_descuento;
END;
GO

-- TR_AUDIT_TOKEN_ACCESO_CUENTA sobre TOKEN_ACCESO_CUENTA
GO
CREATE   TRIGGER dbo.TR_AUDIT_TOKEN_ACCESO_CUENTA ON dbo.TOKEN_ACCESO_CUENTA AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = COALESCE(TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario')), (SELECT TOP (1) id_usuario FROM inserted));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIO WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'TOKEN_ACCESO_CUENTA', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_token, d.PK_id_token)),
        CASE WHEN d.PK_id_token IS NULL THEN 'INSERT' WHEN i.PK_id_token IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_token IS NULL THEN NULL ELSE (SELECT d.id_usuario AS idUsuario, d.tipo, d.vence_utc AS venceUtc, d.consumido_utc AS consumidoUtc FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_token IS NULL THEN NULL ELSE (SELECT i.id_usuario AS idUsuario, i.tipo, i.vence_utc AS venceUtc, i.consumido_utc AS consumidoUtc FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_token = i.PK_id_token;
END;
GO

-- TR_AUDIT_USUARIO sobre USUARIO
GO
-- Redefine el snapshot de auditorÃ­a para incluir las columnas nuevas. El resto
-- del cuerpo es idÃ©ntico al de V20260929_05 (identidad del operador).
CREATE   TRIGGER dbo.TR_AUDIT_USUARIO ON dbo.USUARIO AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N'idUsuario'));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIO WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N'USUARIO', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_usuario, d.PK_id_usuario)),
        CASE WHEN d.PK_id_usuario IS NULL THEN 'INSERT' WHEN i.PK_id_usuario IS NULL THEN 'DELETE' ELSE 'UPDATE' END,
        CASE WHEN d.PK_id_usuario IS NULL THEN NULL ELSE
            (SELECT d.email, d.nombre, d.apellido, d.dni, d.FK_id_rol AS idRol, d.activo,
                d.activacion_pendiente AS activacionPendiente, d.acceso_portal AS accesoPortal, d.rol_portal AS rolPortal,
                CONVERT(bit, CASE WHEN d.PK_id_usuario IS NOT NULL AND i.PK_id_usuario IS NOT NULL AND ISNULL(d.password_hash, N'') <> ISNULL(i.password_hash, N'') THEN 1 ELSE 0 END) AS credencialActualizada
             FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_usuario IS NULL THEN NULL ELSE
            (SELECT i.email, i.nombre, i.apellido, i.dni, i.FK_id_rol AS idRol, i.activo,
                i.activacion_pendiente AS activacionPendiente, i.acceso_portal AS accesoPortal, i.rol_portal AS rolPortal,
                CONVERT(bit, CASE WHEN d.PK_id_usuario IS NOT NULL AND i.PK_id_usuario IS NOT NULL AND ISNULL(d.password_hash, N'') <> ISNULL(i.password_hash, N'') THEN 1 ELSE 0 END) AS credencialActualizada
             FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_usuario = i.PK_id_usuario;
END;
GO

/* ============ HISTORIAL DE MIGRACIONES (lo usa la API del Portal) ============ */
-- Registra las migraciones que esta estructura ya incluye, con el mismo hash que calcula la API,
-- para que al iniciar no intente volver a aplicarlas.

INSERT dbo.[__CaccMigraciones] ([Nombre], [Hash]) VALUES (N'V20260923_01__crear_aranceles.sql', 'A97278DCA85530B2967796CDFE6219442FFD289B6D9904772F4A64D91C5287C8');
INSERT dbo.[__CaccMigraciones] ([Nombre], [Hash]) VALUES (N'V20260923_02__index_pagos_pendientes.sql', 'B3E4563795F9B78279DD8E65AAB28547081FAEB36F14765388622F74EB292698');
INSERT dbo.[__CaccMigraciones] ([Nombre], [Hash]) VALUES (N'V20260930_01__verificar_base_equipo.sql', '975002FF7C7628D604757F43C87768CB21C79F66235F684E7E71F8CEE9691F11');
INSERT dbo.[__CaccMigraciones] ([Nombre], [Hash]) VALUES (N'V20260930_02__agrega_usuario_registro_pagos.sql', '078567DE6345407CE74DA75844FA34DF1AF79A592B972ECC0EEEDC41BA15871F');
INSERT dbo.[__CaccMigraciones] ([Nombre], [Hash]) VALUES (N'V20260930_03__aranceles_agrega_categoria.sql', 'D64308600F7AC43C619D7601405F4DE723756907FAAABB03FB94DB04D64E4496');
INSERT dbo.[__CaccMigraciones] ([Nombre], [Hash]) VALUES (N'V20260930_04__aranceles_reglas_por_genero_o_categoria.sql', '24FC50756168581144154C886389FF2FAAB152199A7320296ADDCF810C32F035');
INSERT dbo.[__CaccMigraciones] ([Nombre], [Hash]) VALUES (N'V20260930_05__auditoria_aranceles_categoria.sql', '91815906C411FCF5EB8012BEAE6ED3BE83EE2EDA4425EFEF05CC0B34126C6060');
INSERT dbo.[__CaccMigraciones] ([Nombre], [Hash]) VALUES (N'V20261004_01__usuario_dni_unico_para_login.sql', 'DED8CA006D1E16A752282E6A4D6E623BCA0C6B690BBD296CD030BDEB6ED2DF85');
GO

/* ========== OPCIONAL: SuperAdmin de PRUEBA para poder entrar al portal ==========
   La base queda sin usuarios. Para probar el portal hace falta una primera cuenta SuperAdmin. En el sistema
   real, las personas y los usuarios los da de alta el otro equipo y el SuperAdmin los habilita desde
   "Usuarios y Permisos"; esto solo resuelve el arranque en una base de PRUEBAS vacia.
   Para usarlo: descomentar, cambiar DNI, correo y contrasena, y ejecutar. Entra con el DNI. En el primer
   ingreso la API guarda la contrasena cifrada y borra este texto plano. NO usar en produccion.

INSERT dbo.USUARIO (PK_id_usuario, nombre, apellido, dni, email, contrasenia, FK_id_rol, activo, acceso_portal, rol_portal)
VALUES (1, N'Super', N'Admin Prueba', N'12345678', N'superadmin.prueba@ejemplo.com', N'CambiarEsta#2026', 1, 1, 1, 1);
*/

SET NOEXEC OFF;
PRINT N'Listo.';
GO
