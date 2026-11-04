/* =====================================================================================
   HU-053 - Paso 3: importacion de A y B a la base unificada. Requiere el paso 2 sin conflictos pendientes.
   Variables sqlcmd: BaseA, BaseB. Se ejecuta sobre la base DESTINO.

   NO ES REPETIBLE SOBRE LA MISMA BASE: exige una base recien creada (sin personas, usuarios, jugadores ni pagos) y la
   rechaza si ya tiene una importacion. Para repetir la prueba se recrea la base (ver 00_ejecutar_prueba.ps1).
   Todo ocurre en UNA transaccion: si algo falla no queda nada a medias y la ejecucion queda como FALLO.

   Identificadores
     - A se importa primero y CONSERVA sus ids (IDENTITY_INSERT en las tablas IDENTITY; valor explicito en JUGADORES y
       PAGOS, que toman el id de SEQ_JUGADORES / SEQ_PAGOS). Asi siguen valiendo comprobantes y auditoria de A.
     - Despues se adelantan las secuencias por encima del mayor id presente (nunca hacia atras).
     - B recibe ids nuevos (IDENTITY o secuencia) y cada id viejo queda en migra.mapa (OldIdB -> UnifiedId). Ninguna FK de B
       se copia tal cual: todas se traducen por el mapa.
   Personas y jugadores
     - Una persona unificada por grupo de DNI (paso 2). Datos de la canonica; los vacios se completan del resto (A antes que B).
     - Un jugador unificado por persona unificada (mismos criterios). Sus dependientes pasan al jugador unificado.
     - Genero faltante de un jugador: se infiere por categoria (Femenino -> Femenino; resto -> Masculino), decision HU-053.
     - Todo valor completado, inferido o descartado queda en migra.transformacion con su valor original.
   Triggers
     - Los de auditoria (TR_AUDIT_*, TR_AUDITORIA_IDENTIDAD_ACTOR) se deshabilitan durante la carga: copiar datos no es una
       operacion de un usuario. La carga queda registrada en migra.ejecucion. Se vuelven a habilitar antes del COMMIT y se
       verifica que esten todos activos. Los de reglas (genero obligatorio, periodos de jugadores) quedan ACTIVOS.
   Las tablas temporales declaran COLLATE DATABASE_DEFAULT: si no, toman la de tempdb y las comparaciones fallan.
   ===================================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/* --------------------------- Precondiciones (sin escribir) --------------------------- */
IF EXISTS (SELECT 1 FROM migra.ejecucion WHERE estado IN ('OK', 'EN_CURSO'))
    THROW 53100, N'Esta base ya tiene una importacion hecha (o en curso). Recrear la base destino para repetir la prueba.', 1;

IF EXISTS (SELECT 1 FROM dbo.PERSONAS) OR EXISTS (SELECT 1 FROM dbo.USUARIOS) OR EXISTS (SELECT 1 FROM dbo.JUGADORES)
   OR EXISTS (SELECT 1 FROM dbo.PAGOS) OR EXISTS (SELECT 1 FROM dbo.RESPONSABLES) OR EXISTS (SELECT 1 FROM dbo.STAFF)
   OR EXISTS (SELECT 1 FROM dbo.JUGADORES_DESCUENTOS) OR EXISTS (SELECT 1 FROM dbo.AUDITORIA_CAMBIOS)
    THROW 53101, N'La importacion exige una base recien creada: esta ya tiene personas, usuarios, jugadores, pagos o auditoria.', 1;

IF (SELECT COUNT(*) FROM migra.persona_plan)
   <> (SELECT COUNT(*) FROM [$(BaseA)].dbo.PERSONA) + (SELECT COUNT(*) FROM [$(BaseB)].dbo.PERSONAS)
    THROW 53102, N'El plan del paso 2 no coincide con las personas de origen: volver a ejecutar 02_analizar_identidades.sql.', 1;

IF EXISTS (SELECT grupo FROM migra.persona_plan GROUP BY grupo HAVING SUM(CAST(es_canonica AS int)) <> 1)
    THROW 53103, N'Hay grupos de DNI sin exactamente una persona canonica: volver a ejecutar el paso 2.', 1;

IF EXISTS (SELECT 1 FROM migra.jugador_origen WHERE grupo IS NULL)
    THROW 53104, N'Hay jugadores de origen sin persona: no se pueden importar (la base exige el genero de su persona).', 1;

-- Catalogos: mismos ids y nombres. Si no coinciden, traducir por id seria falso.
IF EXISTS (SELECT 1 FROM [$(BaseA)].dbo.CATEGORIAS o LEFT JOIN dbo.CATEGORIAS d
               ON d.PK_id_categoria = o.PK_id_categoria AND d.nombre_categoria = o.nombre_categoria COLLATE DATABASE_DEFAULT
           WHERE d.PK_id_categoria IS NULL)
   OR EXISTS (SELECT 1 FROM [$(BaseB)].dbo.CATEGORIAS o LEFT JOIN dbo.CATEGORIAS d
               ON d.PK_id_categoria = o.PK_id_categoria AND d.nombre_categoria = o.nombre_categoria COLLATE DATABASE_DEFAULT
           WHERE d.PK_id_categoria IS NULL)
    THROW 53110, N'Las categorias de A o B no coinciden (id y nombre) con el catalogo unificado.', 1;

IF EXISTS (SELECT 1 FROM [$(BaseB)].dbo.ROLES o LEFT JOIN dbo.ROLES d
               ON d.PK_id_rol = o.PK_id_rol AND d.nombre_rol = o.nombre_rol COLLATE DATABASE_DEFAULT
           WHERE d.PK_id_rol IS NULL)
    THROW 53111, N'Los roles de B no coinciden (id y nombre) con ROLES unificado: sus ids estan escritos en el codigo del Portal Deportivo.', 1;

-- Roles de A: no hay equivalencia por id (el 2 de A es "Usuario Comun"; el 2 unificado es "Administrativo").
-- Se traducen con esta tabla cerrada (ver la seccion Roles de QA-HU-053.md); un rol de A que no figure detiene la carga.
IF EXISTS (SELECT 1 FROM [$(BaseA)].dbo.ROLES o
           WHERE NOT EXISTS (SELECT 1 FROM (VALUES (1, N'Administrador General'), (2, N'Usuario Comun')) AS r (id, nombre)
                             WHERE r.id = o.PK_id_rol AND r.nombre = o.nombre_rol COLLATE DATABASE_DEFAULT))
    THROW 53112, N'ROLES de A tiene un rol no contemplado en la traduccion de roles de HU-053.', 1;

IF EXISTS (SELECT 1 FROM [$(BaseA)].dbo.TIPO_DESCUENTO o
           WHERE NOT EXISTS (SELECT 1 FROM dbo.TIPOS_DESCUENTO d WHERE d.tipo_descuento = o.tipo_descuento COLLATE DATABASE_DEFAULT))
   OR EXISTS (SELECT 1 FROM [$(BaseB)].dbo.TIPOS_DESCUENTO o
           WHERE NOT EXISTS (SELECT 1 FROM dbo.TIPOS_DESCUENTO d WHERE d.tipo_descuento = o.tipo_descuento COLLATE DATABASE_DEFAULT))
    THROW 53113, N'Hay tipos de descuento de origen que no existen en TIPOS_DESCUENTO unificado (catalogo cerrado).', 1;

-- Datos de origen sin traduccion definida en esta HU: si aparecen, se frena en lugar de perderlos.
IF EXISTS (SELECT 1 FROM [$(BaseB)].dbo.JUGADORES_DESCUENTOS)
    THROW 53114, N'B tiene becas/descuentos sin fechas de vigencia (obligatorias en la base unificada): falta definir su traduccion.', 1;
IF EXISTS (SELECT 1 FROM [$(BaseA)].dbo.ARANCELES) OR EXISTS (SELECT 1 FROM [$(BaseA)].dbo.ARANCELES_INSCRIPCION)
    THROW 53115, N'A tiene aranceles cargados: esta HU no define su traduccion (se programan desde el Portal Administrativo).', 1;
IF EXISTS (SELECT 1 FROM [$(BaseA)].dbo.TOKEN_ACCESO_CUENTA)
    THROW 53116, N'A tiene invitaciones o recuperaciones de cuenta: esta HU no las traslada (se vuelven a emitir).', 1;
IF EXISTS (SELECT 1 FROM [$(BaseA)].dbo.PERSONA WHERE QR_TOKEN IS NOT NULL OR FOTO IS NOT NULL)
    THROW 53117, N'A tiene QR_TOKEN o FOTO en PERSONA: la base unificada no tiene esas columnas.', 1;
IF EXISTS (SELECT 1 FROM [$(BaseA)].dbo.JUGADORES WHERE FK_id_jugador_responsable IS NOT NULL OR FK_id_Ficha_Medica IS NOT NULL)
    THROW 53118, N'A tiene JUGADORES.FK_id_jugador_responsable o FK_id_Ficha_Medica cargados: la base unificada no tiene esas columnas.', 1;
IF EXISTS (SELECT 1 FROM [$(BaseA)].dbo.STAFF WHERE FK_id_staff_categoria IS NOT NULL)
    THROW 53119, N'A tiene STAFF.FK_id_staff_categoria cargado: la base unificada no tiene esa columna.', 1;
IF EXISTS (SELECT 1 FROM [$(BaseA)].dbo.ASISTENCIAS WHERE FK_id_estado_deuda IS NOT NULL)
    THROW 53121, N'A tiene ASISTENCIAS.FK_id_estado_deuda cargado: la base unificada no tiene esa columna.', 1;
GO

/* ------------------------------------ Importacion ------------------------------------ */
INSERT migra.ejecucion (base_a, base_b, estado) VALUES (N'$(BaseA)', N'$(BaseB)', 'EN_CURSO');
DECLARE @ejecucion int = SCOPE_IDENTITY();
DECLARE @sql nvarchar(max);
DECLARE @siguiente bigint;
DECLARE @hoy date = CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Argentina Standard Time' AS date);

BEGIN TRY
    BEGIN TRANSACTION;

    TRUNCATE TABLE migra.mapa;
    DELETE migra.transformacion;
    DELETE migra.conteo;

    INSERT migra.conteo (etapa, entidad, origen, cantidad)
    SELECT 'ORIGEN', v.entidad, v.origen, v.cantidad
    FROM (VALUES
        (N'PERSONAS', 'A', (SELECT COUNT(*) FROM [$(BaseA)].dbo.PERSONA)),
        (N'PERSONAS', 'B', (SELECT COUNT(*) FROM [$(BaseB)].dbo.PERSONAS)),
        (N'USUARIOS', 'A', (SELECT COUNT(*) FROM [$(BaseA)].dbo.USUARIO)),
        (N'USUARIOS', 'B', (SELECT COUNT(*) FROM [$(BaseB)].dbo.USUARIOS)),
        (N'JUGADORES', 'A', (SELECT COUNT(*) FROM [$(BaseA)].dbo.JUGADORES)),
        (N'JUGADORES', 'B', (SELECT COUNT(*) FROM [$(BaseB)].dbo.JUGADORES)),
        (N'STAFF', 'A', (SELECT COUNT(*) FROM [$(BaseA)].dbo.STAFF)),
        (N'STAFF', 'B', (SELECT COUNT(*) FROM [$(BaseB)].dbo.STAFF)),
        (N'STAFF_CATEGORIAS', 'A', (SELECT COUNT(*) FROM [$(BaseA)].dbo.STAFF_CATEGORIA)),
        (N'STAFF_CATEGORIAS', 'B', (SELECT COUNT(*) FROM [$(BaseB)].dbo.STAFF_CATEGORIAS)),
        (N'RESPONSABLES', 'A', (SELECT COUNT(*) FROM [$(BaseA)].dbo.RESPONSABLES)),
        (N'RESPONSABLES', 'B', (SELECT COUNT(*) FROM [$(BaseB)].dbo.RESPONSABLES)),
        (N'JUGADORES_RESPONSABLES', 'A', (SELECT COUNT(*) FROM [$(BaseA)].dbo.Jugadore_Responsable)),
        (N'JUGADORES_RESPONSABLES', 'B', (SELECT COUNT(*) FROM [$(BaseB)].dbo.JUGADORES_RESPONSABLES)),
        (N'FICHAS_MEDICAS', 'A', (SELECT COUNT(*) FROM [$(BaseA)].dbo.FICHAS_MEDICAS)),
        (N'FICHAS_MEDICAS', 'B', (SELECT COUNT(*) FROM [$(BaseB)].dbo.FICHAS_MEDICAS)),
        (N'HISTORIAL_PARTIDOS', 'B', (SELECT COUNT(*) FROM [$(BaseB)].dbo.HISTORIAL_PARTIDOS)),
        (N'JUGADORES_PARTIDOS', 'B', (SELECT COUNT(*) FROM [$(BaseB)].dbo.JUGADORES_PARTIDOS)),
        (N'ASISTENCIAS', 'A', (SELECT COUNT(*) FROM [$(BaseA)].dbo.ASISTENCIAS)),
        (N'ASISTENCIAS', 'B', (SELECT COUNT(*) FROM [$(BaseB)].dbo.ASISTENCIAS)),
        (N'JUGADORES_DESCUENTOS', 'A', (SELECT COUNT(*) FROM [$(BaseA)].dbo.JUGADORES_DESCUENTOS)),
        (N'JUGADORES_DESCUENTOS', 'B', (SELECT COUNT(*) FROM [$(BaseB)].dbo.JUGADORES_DESCUENTOS)),
        (N'PAGOS', 'A', (SELECT COUNT(*) FROM [$(BaseA)].dbo.PAGOS)),
        (N'PAGOS', 'B', (SELECT COUNT(*) FROM [$(BaseB)].dbo.PAGOS)),
        (N'AUDITORIA_CAMBIOS', 'A', (SELECT COUNT(*) FROM [$(BaseA)].dbo.AUDITORIA_CAMBIOS))
    ) AS v (entidad, origen, cantidad);

    -- Auditoria apagada durante la carga (y solo durante la carga).
    DECLARE @triggers TABLE (nombre sysname PRIMARY KEY, tabla nvarchar(300) NOT NULL);
    INSERT @triggers (nombre, tabla)
    SELECT t.name, QUOTENAME(OBJECT_SCHEMA_NAME(t.parent_id)) + N'.' + QUOTENAME(OBJECT_NAME(t.parent_id))
    FROM sys.triggers t
    WHERE t.parent_class = 1 AND t.is_disabled = 0 AND (t.name LIKE N'TR[_]AUDIT[_]%' OR t.name = N'TR_AUDITORIA_IDENTIDAD_ACTOR');

    SET @sql = (SELECT STRING_AGG(CONVERT(nvarchar(max), N'DISABLE TRIGGER dbo.' + QUOTENAME(nombre) + N' ON ' + tabla + N';'), N' ') FROM @triggers);
    EXEC sys.sp_executesql @sql;

    DECLARE @categoriaFemenino int = (SELECT PK_id_categoria FROM dbo.CATEGORIAS WHERE nombre_categoria = N'Femenino');
    IF @categoriaFemenino IS NULL
        THROW 53120, N'No existe la categoria Femenino: sin ella no se puede aplicar la regla de genero acordada.', 1;

    /* ================================ PERSONAS (plan) ================================ */
    -- Prioridad de cada registro dentro de su grupo: canonica, luego A, luego B, luego id.
    CREATE TABLE #po (
        origen char(1) COLLATE DATABASE_DEFAULT NOT NULL, id_origen int NOT NULL, grupo varchar(40) COLLATE DATABASE_DEFAULT NOT NULL,
        dni_original varchar(20) COLLATE DATABASE_DEFAULT NULL, dni varchar(20) COLLATE DATABASE_DEFAULT NULL,
        nombre varchar(100) COLLATE DATABASE_DEFAULT NULL, apellido varchar(100) COLLATE DATABASE_DEFAULT NULL,
        genero varchar(50) COLLATE DATABASE_DEFAULT NULL, fecha_nacimiento date NULL, domicilio varchar(255) COLLATE DATABASE_DEFAULT NULL,
        regla varchar(40) COLLATE DATABASE_DEFAULT NOT NULL, prioridad int NOT NULL,
        PRIMARY KEY (origen, id_origen));

    INSERT #po (origen, id_origen, grupo, dni_original, dni, nombre, apellido, genero, fecha_nacimiento, domicilio, regla, prioridad)
    SELECT po.origen, po.id_origen, po.grupo, po.dni_original, po.dni, po.nombre, po.apellido, po.genero, po.fecha_nacimiento, po.domicilio,
           pp.regla,
           ROW_NUMBER() OVER (PARTITION BY po.grupo ORDER BY pp.es_canonica DESC, CASE po.origen WHEN 'A' THEN 0 ELSE 1 END, po.id_origen)
    FROM migra.persona_origen po
    JOIN migra.persona_plan pp ON pp.origen = po.origen AND pp.id_origen = po.id_origen;

    CREATE TABLE #pf (
        grupo varchar(40) COLLATE DATABASE_DEFAULT NOT NULL PRIMARY KEY,
        id_fijo int NULL,                  -- id de A que se conserva; NULL = id nuevo
        dni varchar(20) COLLATE DATABASE_DEFAULT NULL, nombre varchar(100) COLLATE DATABASE_DEFAULT NULL,
        apellido varchar(100) COLLATE DATABASE_DEFAULT NULL, genero varchar(50) COLLATE DATABASE_DEFAULT NULL,
        fecha_nacimiento date NULL, domicilio varchar(255) COLLATE DATABASE_DEFAULT NULL,
        id_unificado int NULL);

    INSERT #pf (grupo, id_fijo, dni, nombre, apellido, genero, fecha_nacimiento, domicilio)
    SELECT g.grupo,
           (SELECT TOP (1) x.id_origen FROM #po x WHERE x.grupo = g.grupo AND x.origen = 'A' ORDER BY x.prioridad),
           (SELECT TOP (1) x.dni FROM #po x WHERE x.grupo = g.grupo AND x.dni IS NOT NULL ORDER BY x.prioridad),
           (SELECT TOP (1) x.nombre FROM #po x WHERE x.grupo = g.grupo AND x.nombre IS NOT NULL ORDER BY x.prioridad),
           (SELECT TOP (1) x.apellido FROM #po x WHERE x.grupo = g.grupo AND x.apellido IS NOT NULL ORDER BY x.prioridad),
           (SELECT TOP (1) x.genero FROM #po x WHERE x.grupo = g.grupo AND x.genero IS NOT NULL ORDER BY x.prioridad),
           (SELECT TOP (1) x.fecha_nacimiento FROM #po x WHERE x.grupo = g.grupo AND x.fecha_nacimiento IS NOT NULL ORDER BY x.prioridad),
           (SELECT TOP (1) x.domicilio FROM #po x WHERE x.grupo = g.grupo AND x.domicilio IS NOT NULL ORDER BY x.prioridad)
    FROM (SELECT DISTINCT grupo FROM #po) g;

    /* ================================ JUGADORES (plan) ================================ */
    CREATE TABLE #jo (
        origen char(1) COLLATE DATABASE_DEFAULT NOT NULL, id_origen int NOT NULL, grupo varchar(40) COLLATE DATABASE_DEFAULT NOT NULL,
        FK_id_categoria int NULL, club_origen varchar(100) COLLATE DATABASE_DEFAULT NULL, ficha_medica_liga bit NULL,
        posicion_cancha varchar(50) COLLATE DATABASE_DEFAULT NULL, peso decimal(5,2) NULL, altura decimal(4,2) NULL,
        pie_habil varchar(20) COLLATE DATABASE_DEFAULT NULL, grupo_sanguineo varchar(5) COLLATE DATABASE_DEFAULT NULL,
        fecha_alta date NULL, prioridad int NOT NULL,
        PRIMARY KEY (origen, id_origen));

    INSERT #jo (origen, id_origen, grupo, FK_id_categoria, club_origen, ficha_medica_liga, posicion_cancha, peso, altura, pie_habil,
                grupo_sanguineo, fecha_alta, prioridad)
    SELECT jo.origen, jo.id_origen, jo.grupo, jo.FK_id_categoria,
           a.club_origen, a.ficha_medica_liga, a.posicion_cancha, a.peso, a.altura, a.pie_habil, a.grupo_sanguineo, a.fecha_alta,
           ROW_NUMBER() OVER (PARTITION BY jo.grupo ORDER BY pc.es_canonica DESC, CASE jo.origen WHEN 'A' THEN 0 ELSE 1 END, jo.id_origen)
    FROM migra.jugador_origen jo
    JOIN migra.persona_plan pc ON pc.origen = jo.origen AND pc.id_origen = jo.id_persona_origen
    CROSS APPLY (
        SELECT ja.club_origen COLLATE DATABASE_DEFAULT, ja.Ficha_medica_liga, ja.Posicion_cancha COLLATE DATABASE_DEFAULT,
               CONVERT(decimal(5,2), NULL), CONVERT(decimal(4,2), NULL), CONVERT(varchar(20), NULL), CONVERT(varchar(5), NULL), ja.fecha_alta
        FROM [$(BaseA)].dbo.JUGADORES ja WHERE jo.origen = 'A' AND ja.PK_id_jugador = jo.id_origen
        UNION ALL
        SELECT jb.club_origen COLLATE DATABASE_DEFAULT, jb.ficha_medica_liga, jb.posicion_cancha COLLATE DATABASE_DEFAULT,
               jb.peso, jb.altura, jb.pie_habil COLLATE DATABASE_DEFAULT, jb.grupo_sanguineo COLLATE DATABASE_DEFAULT, CONVERT(date, NULL)
        FROM [$(BaseB)].dbo.JUGADORES jb WHERE jo.origen = 'B' AND jb.PK_id_jugador = jo.id_origen
    ) AS a (club_origen, ficha_medica_liga, posicion_cancha, peso, altura, pie_habil, grupo_sanguineo, fecha_alta);

    CREATE TABLE #jf (
        grupo varchar(40) COLLATE DATABASE_DEFAULT NOT NULL PRIMARY KEY,
        id_fijo int NULL, FK_id_categoria int NOT NULL,
        club_origen varchar(100) COLLATE DATABASE_DEFAULT NULL, ficha_medica_liga bit NULL,
        posicion_cancha varchar(50) COLLATE DATABASE_DEFAULT NULL, peso decimal(5,2) NULL, altura decimal(4,2) NULL,
        pie_habil varchar(20) COLLATE DATABASE_DEFAULT NULL, grupo_sanguineo varchar(5) COLLATE DATABASE_DEFAULT NULL,
        fecha_alta date NULL,
        id_unificado int NULL);

    INSERT #jf (grupo, id_fijo, FK_id_categoria, club_origen, ficha_medica_liga, posicion_cancha, peso, altura, pie_habil, grupo_sanguineo, fecha_alta)
    SELECT g.grupo,
           (SELECT TOP (1) x.id_origen FROM #jo x WHERE x.grupo = g.grupo AND x.origen = 'A' ORDER BY x.prioridad),
           (SELECT TOP (1) x.FK_id_categoria FROM #jo x WHERE x.grupo = g.grupo AND x.FK_id_categoria IS NOT NULL ORDER BY x.prioridad),
           (SELECT TOP (1) x.club_origen FROM #jo x WHERE x.grupo = g.grupo AND x.club_origen IS NOT NULL ORDER BY x.prioridad),
           (SELECT TOP (1) x.ficha_medica_liga FROM #jo x WHERE x.grupo = g.grupo AND x.ficha_medica_liga IS NOT NULL ORDER BY x.prioridad),
           (SELECT TOP (1) x.posicion_cancha FROM #jo x WHERE x.grupo = g.grupo AND x.posicion_cancha IS NOT NULL ORDER BY x.prioridad),
           (SELECT TOP (1) x.peso FROM #jo x WHERE x.grupo = g.grupo AND x.peso IS NOT NULL ORDER BY x.prioridad),
           (SELECT TOP (1) x.altura FROM #jo x WHERE x.grupo = g.grupo AND x.altura IS NOT NULL ORDER BY x.prioridad),
           (SELECT TOP (1) x.pie_habil FROM #jo x WHERE x.grupo = g.grupo AND x.pie_habil IS NOT NULL ORDER BY x.prioridad),
           (SELECT TOP (1) x.grupo_sanguineo FROM #jo x WHERE x.grupo = g.grupo AND x.grupo_sanguineo IS NOT NULL ORDER BY x.prioridad),
           (SELECT TOP (1) x.fecha_alta FROM #jo x WHERE x.grupo = g.grupo AND x.fecha_alta IS NOT NULL ORDER BY x.prioridad)
    FROM (SELECT DISTINCT grupo FROM #jo) g;

    /* ============================ Transformaciones de datos ============================ */
    -- Genero de jugadores: regla acordada en HU-053, solo donde falta o no es valido.
    INSERT migra.transformacion (entidad, origen, id_origen, columna, valor_origen, valor_destino, regla)
    SELECT N'PERSONAS', c.origen, c.id_origen, N'genero', p.genero,
           CASE WHEN j.FK_id_categoria = @categoriaFemenino THEN N'Femenino' ELSE N'Masculino' END, 'GENERO_INFERIDO_POR_CATEGORIA'
    FROM #pf p
    JOIN #jf j ON j.grupo = p.grupo
    JOIN #po c ON c.grupo = p.grupo AND c.prioridad = 1
    WHERE p.genero IS NULL OR LTRIM(RTRIM(p.genero)) NOT IN (N'Masculino', N'Femenino');

    UPDATE p SET genero = CASE WHEN j.FK_id_categoria = @categoriaFemenino THEN 'Femenino' ELSE 'Masculino' END
    FROM #pf p JOIN #jf j ON j.grupo = p.grupo
    WHERE p.genero IS NULL OR LTRIM(RTRIM(p.genero)) NOT IN (N'Masculino', N'Femenino');

    -- Personas: valores descartados (de cualquier registro) y completados en la canonica desde otro registro.
    INSERT migra.transformacion (entidad, origen, id_origen, columna, valor_origen, valor_destino, regla)
    SELECT N'PERSONAS', po.origen, po.id_origen, v.columna, v.valor_origen, v.valor_destino,
           CASE WHEN v.valor_origen IS NULL THEN 'COMPLETADO_DESDE_OTRO_REGISTRO'
                WHEN po.regla = 'RESOLUCION_HUMANA' THEN 'DESCARTADO_POR_RESOLUCION'
                ELSE 'DESCARTADO_PRECEDENCIA_A' END
    FROM #po po
    JOIN #pf f ON f.grupo = po.grupo
    CROSS APPLY (VALUES
        (N'nombre', CONVERT(nvarchar(4000), po.nombre), CONVERT(nvarchar(4000), f.nombre)),
        (N'apellido', CONVERT(nvarchar(4000), po.apellido), CONVERT(nvarchar(4000), f.apellido)),
        (N'genero', CONVERT(nvarchar(4000), po.genero), CONVERT(nvarchar(4000), f.genero)),
        (N'fecha_de_nacimiento', CONVERT(nvarchar(4000), po.fecha_nacimiento, 23), CONVERT(nvarchar(4000), f.fecha_nacimiento, 23)),
        (N'domicilio', CONVERT(nvarchar(4000), po.domicilio), CONVERT(nvarchar(4000), f.domicilio))
    ) AS v (columna, valor_origen, valor_destino)
    WHERE (v.valor_origen IS NOT NULL
           AND (v.valor_destino IS NULL OR v.valor_origen COLLATE Latin1_General_BIN2 <> v.valor_destino COLLATE Latin1_General_BIN2))
       OR (po.prioridad = 1 AND v.valor_origen IS NULL AND v.valor_destino IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM migra.transformacion t
                           WHERE t.entidad = N'PERSONAS' AND t.origen = po.origen AND t.id_origen = po.id_origen AND t.columna = v.columna));

    -- DNI: normalizado (formato) o descartado por invalido (la persona queda sin DNI y no se une a nadie).
    INSERT migra.transformacion (entidad, origen, id_origen, columna, valor_origen, valor_destino, regla)
    SELECT N'PERSONAS', origen, id_origen, N'dni', dni_original, dni,
           CASE WHEN dni IS NULL THEN 'DNI_INVALIDO_SIN_UNIFICAR' ELSE 'DNI_NORMALIZADO' END
    FROM #po
    WHERE dni_original IS NOT NULL AND (dni IS NULL OR dni_original COLLATE Latin1_General_BIN2 <> dni COLLATE Latin1_General_BIN2);

    -- Jugadores: lo mismo, mas el periodo que el trigger abre con la fecha de hoy cuando el origen no tiene fecha de alta.
    INSERT migra.transformacion (entidad, origen, id_origen, columna, valor_origen, valor_destino, regla)
    SELECT N'JUGADORES', jo.origen, jo.id_origen, v.columna, v.valor_origen, v.valor_destino,
           CASE WHEN v.valor_origen IS NULL THEN 'COMPLETADO_DESDE_OTRO_REGISTRO' ELSE 'DESCARTADO_PRECEDENCIA_A' END
    FROM #jo jo
    JOIN #jf f ON f.grupo = jo.grupo
    CROSS APPLY (VALUES
        (N'FK_id_categoria', CONVERT(nvarchar(4000), jo.FK_id_categoria), CONVERT(nvarchar(4000), f.FK_id_categoria)),
        (N'club_origen', CONVERT(nvarchar(4000), jo.club_origen), CONVERT(nvarchar(4000), f.club_origen)),
        (N'ficha_medica_liga', CONVERT(nvarchar(4000), jo.ficha_medica_liga), CONVERT(nvarchar(4000), f.ficha_medica_liga)),
        (N'posicion_cancha', CONVERT(nvarchar(4000), jo.posicion_cancha), CONVERT(nvarchar(4000), f.posicion_cancha)),
        (N'peso', CONVERT(nvarchar(4000), jo.peso), CONVERT(nvarchar(4000), f.peso)),
        (N'altura', CONVERT(nvarchar(4000), jo.altura), CONVERT(nvarchar(4000), f.altura)),
        (N'pie_habil', CONVERT(nvarchar(4000), jo.pie_habil), CONVERT(nvarchar(4000), f.pie_habil)),
        (N'grupo_sanguineo', CONVERT(nvarchar(4000), jo.grupo_sanguineo), CONVERT(nvarchar(4000), f.grupo_sanguineo)),
        (N'fecha_alta', CONVERT(nvarchar(4000), jo.fecha_alta, 23), CONVERT(nvarchar(4000), f.fecha_alta, 23))
    ) AS v (columna, valor_origen, valor_destino)
    WHERE (v.valor_origen IS NOT NULL
           AND (v.valor_destino IS NULL OR v.valor_origen COLLATE Latin1_General_BIN2 <> v.valor_destino COLLATE Latin1_General_BIN2))
       OR (jo.prioridad = 1 AND v.valor_origen IS NULL AND v.valor_destino IS NOT NULL);

    INSERT migra.transformacion (entidad, origen, id_origen, columna, valor_origen, valor_destino, regla)
    SELECT N'JUGADORES', jo.origen, jo.id_origen, N'JUGADORES_PERIODOS.fecha_alta', NULL, CONVERT(nvarchar(10), @hoy, 23),
           'PERIODO_ABIERTO_EN_FECHA_DE_MIGRACION'
    FROM #jf f JOIN #jo jo ON jo.grupo = f.grupo AND jo.prioridad = 1
    WHERE f.fecha_alta IS NULL;

    /* ================================ PERSONAS (carga) ================================ */
    SET IDENTITY_INSERT dbo.PERSONAS ON;
    INSERT dbo.PERSONAS (PK_id_persona, genero, fecha_de_nacimiento, dni, nombre, apellido, domicilio)
    SELECT id_fijo, genero, fecha_nacimiento, dni, nombre, apellido, domicilio FROM #pf WHERE id_fijo IS NOT NULL;
    SET IDENTITY_INSERT dbo.PERSONAS OFF;
    UPDATE #pf SET id_unificado = id_fijo WHERE id_fijo IS NOT NULL;

    DECLARE @nuevasPersonas TABLE (grupo varchar(40) COLLATE DATABASE_DEFAULT PRIMARY KEY, id int NOT NULL);
    MERGE dbo.PERSONAS AS d
    USING (SELECT * FROM #pf WHERE id_fijo IS NULL) AS s ON 1 = 0
    WHEN NOT MATCHED THEN
        INSERT (genero, fecha_de_nacimiento, dni, nombre, apellido, domicilio)
        VALUES (s.genero, s.fecha_nacimiento, s.dni, s.nombre, s.apellido, s.domicilio)
    OUTPUT s.grupo, inserted.PK_id_persona INTO @nuevasPersonas (grupo, id);
    UPDATE f SET id_unificado = n.id FROM #pf f JOIN @nuevasPersonas n ON n.grupo = f.grupo;

    INSERT migra.mapa (entidad, origen, id_origen, id_unificado, regla)
    SELECT N'PERSONAS', po.origen, po.id_origen, f.id_unificado,
           CASE WHEN po.prioridad > 1 THEN 'FUSIONADO' WHEN f.id_fijo IS NULL THEN 'ID_NUEVO' ELSE 'ID_CONSERVADO' END
    FROM #po po JOIN #pf f ON f.grupo = po.grupo;

    /* ================================ JUGADORES (carga) ================================ */
    -- La secuencia avanza por encima del mayor id de A antes de que B tome valores de ella. Nunca retrocede.
    SET @siguiente = (SELECT ISNULL(MAX(id_fijo), 0) + 1 FROM #jf);
    IF @siguiente > (SELECT CASE WHEN last_used_value IS NULL THEN CONVERT(bigint, start_value) ELSE CONVERT(bigint, last_used_value) + 1 END
                     FROM sys.sequences WHERE object_id = OBJECT_ID(N'dbo.SEQ_JUGADORES'))
    BEGIN
        SET @sql = N'ALTER SEQUENCE dbo.SEQ_JUGADORES RESTART WITH ' + CONVERT(nvarchar(20), @siguiente) + N';';
        EXEC sys.sp_executesql @sql;
    END;

    INSERT dbo.JUGADORES (PK_id_jugador, FK_id_persona, FK_id_categoria, club_origen, ficha_medica_liga, posicion_cancha,
                          peso, altura, pie_habil, grupo_sanguineo, fecha_alta, fecha_baja)
    SELECT j.id_fijo, p.id_unificado, j.FK_id_categoria, j.club_origen, j.ficha_medica_liga, j.posicion_cancha,
           j.peso, j.altura, j.pie_habil, j.grupo_sanguineo, j.fecha_alta, NULL
    FROM #jf j JOIN #pf p ON p.grupo = j.grupo
    WHERE j.id_fijo IS NOT NULL;
    UPDATE #jf SET id_unificado = id_fijo WHERE id_fijo IS NOT NULL;

    DECLARE @nuevosJugadores TABLE (grupo varchar(40) COLLATE DATABASE_DEFAULT PRIMARY KEY, id int NOT NULL);
    MERGE dbo.JUGADORES AS d
    USING (SELECT j.*, p.id_unificado AS id_persona FROM #jf j JOIN #pf p ON p.grupo = j.grupo WHERE j.id_fijo IS NULL) AS s ON 1 = 0
    WHEN NOT MATCHED THEN
        INSERT (FK_id_persona, FK_id_categoria, club_origen, ficha_medica_liga, posicion_cancha, peso, altura, pie_habil, grupo_sanguineo, fecha_alta, fecha_baja)
        VALUES (s.id_persona, s.FK_id_categoria, s.club_origen, s.ficha_medica_liga, s.posicion_cancha, s.peso, s.altura, s.pie_habil, s.grupo_sanguineo, s.fecha_alta, NULL)
    OUTPUT s.grupo, inserted.PK_id_jugador INTO @nuevosJugadores (grupo, id);
    UPDATE f SET id_unificado = n.id FROM #jf f JOIN @nuevosJugadores n ON n.grupo = f.grupo;

    INSERT migra.mapa (entidad, origen, id_origen, id_unificado, regla)
    SELECT N'JUGADORES', jo.origen, jo.id_origen, f.id_unificado,
           CASE WHEN jo.prioridad > 1 THEN 'FUSIONADO' WHEN f.id_fijo IS NULL THEN 'ID_NUEVO' ELSE 'ID_CONSERVADO' END
    FROM #jo jo JOIN #jf f ON f.grupo = jo.grupo;

    /* ================================ USUARIOS ================================ */
    -- A: contrasenia = password_hash (BCrypt o PBKDF2, los dos los valida PasswordHasher). La contrasena en texto plano NO se
    --    copia (igual que V20261011_06). acceso_portal y rol_portal son de A y se conservan.
    -- B: nace SIN acceso al Portal Administrativo (contrato v2): acceso_portal = 0, rol_portal = NULL, sin importar FK_id_rol.
    --    Un rol 1/2 de B lo ve el SuperAdmin como candidato a habilitar (UsuariosPortalDao); nadie se promueve solo.
    CREATE TABLE #uo (
        origen char(1) COLLATE DATABASE_DEFAULT NOT NULL, id_origen int NOT NULL, grupo varchar(40) COLLATE DATABASE_DEFAULT NULL,
        FK_id_rol int NULL, rol_origen int NULL, email varchar(255) COLLATE DATABASE_DEFAULT NULL,
        contrasenia varchar(255) COLLATE DATABASE_DEFAULT NULL, texto_plano_descartado bit NOT NULL, activo bit NULL,
        acceso_portal bit NOT NULL, rol_portal int NULL, activacion_pendiente bit NOT NULL, token_version int NOT NULL,
        PRIMARY KEY (origen, id_origen));

    INSERT #uo (origen, id_origen, grupo, FK_id_rol, rol_origen, email, contrasenia, texto_plano_descartado, activo,
                acceso_portal, rol_portal, activacion_pendiente, token_version)
    SELECT 'A', u.PK_id_usuario, po.grupo, CASE u.FK_id_rol WHEN 1 THEN 1 END, u.FK_id_rol, u.email COLLATE DATABASE_DEFAULT,
           CONVERT(varchar(255), u.password_hash) COLLATE DATABASE_DEFAULT,
           CASE WHEN u.password_hash IS NULL AND u.contrasenia IS NOT NULL THEN 1 ELSE 0 END, u.activo,
           u.acceso_portal, u.rol_portal, u.activacion_pendiente, u.token_version
    FROM [$(BaseA)].dbo.USUARIO u
    LEFT JOIN migra.persona_origen po ON po.origen = 'A' AND po.id_origen = u.FK_id_persona
    UNION ALL
    SELECT 'B', u.PK_id_usuario, po.grupo, u.FK_id_rol, u.FK_id_rol, u.email COLLATE DATABASE_DEFAULT,
           u.contrasenia COLLATE DATABASE_DEFAULT, 0, u.activo, 0, NULL, 0, 0
    FROM [$(BaseB)].dbo.USUARIOS u
    LEFT JOIN migra.persona_origen po ON po.origen = 'B' AND po.id_origen = u.FK_id_persona;

    INSERT migra.transformacion (entidad, origen, id_origen, columna, valor_origen, valor_destino, regla)
    SELECT N'USUARIOS', origen, id_origen, N'email', email, NULL, 'EMAIL_INVALIDO_DESCARTADO'
    FROM #uo WHERE email IS NOT NULL AND email NOT LIKE '%_@_%._%';
    INSERT migra.transformacion (entidad, origen, id_origen, columna, valor_origen, valor_destino, regla)
    SELECT N'USUARIOS', origen, id_origen, N'FK_id_rol', CONVERT(nvarchar(20), rol_origen), NULL, 'ROL_DE_A_SIN_EQUIVALENTE'
    FROM #uo WHERE origen = 'A' AND rol_origen IS NOT NULL AND FK_id_rol IS NULL;
    INSERT migra.transformacion (entidad, origen, id_origen, columna, valor_origen, valor_destino, regla)
    SELECT N'USUARIOS', origen, id_origen, N'contrasenia', N'<texto plano: no se copia>', NULL, 'CONTRASENIA_TEXTO_PLANO_DESCARTADA'
    FROM #uo WHERE texto_plano_descartado = 1;
    UPDATE #uo SET email = NULL WHERE email IS NOT NULL AND email NOT LIKE '%_@_%._%';

    IF EXISTS (SELECT email FROM #uo WHERE email IS NOT NULL GROUP BY email HAVING COUNT(*) > 1)
        THROW 53130, N'Dos usuarios de origen tienen el mismo correo: la base unificada exige correo unico. Resolverlo en el origen.', 1;

    SET IDENTITY_INSERT dbo.USUARIOS ON;
    INSERT dbo.USUARIOS (PK_id_usuario, FK_id_persona, FK_id_rol, email, contrasenia, activo, acceso_portal, rol_portal, activacion_pendiente, token_version)
    SELECT u.id_origen, p.id_unificado, u.FK_id_rol, u.email, u.contrasenia, u.activo, u.acceso_portal, u.rol_portal, u.activacion_pendiente, u.token_version
    FROM #uo u LEFT JOIN #pf p ON p.grupo = u.grupo
    WHERE u.origen = 'A';
    SET IDENTITY_INSERT dbo.USUARIOS OFF;

    INSERT migra.mapa (entidad, origen, id_origen, id_unificado, regla)
    SELECT N'USUARIOS', 'A', id_origen, id_origen, 'ID_CONSERVADO' FROM #uo WHERE origen = 'A';

    MERGE dbo.USUARIOS AS d
    USING (SELECT u.*, p.id_unificado AS id_persona FROM #uo u LEFT JOIN #pf p ON p.grupo = u.grupo WHERE u.origen = 'B') AS s ON 1 = 0
    WHEN NOT MATCHED THEN
        INSERT (FK_id_persona, FK_id_rol, email, contrasenia, activo, acceso_portal, rol_portal, activacion_pendiente, token_version)
        VALUES (s.id_persona, s.FK_id_rol, s.email, s.contrasenia, s.activo, s.acceso_portal, s.rol_portal, s.activacion_pendiente, s.token_version)
    OUTPUT N'USUARIOS', 'B', s.id_origen, inserted.PK_id_usuario, 'ID_NUEVO' INTO migra.mapa (entidad, origen, id_origen, id_unificado, regla);

    /* ================================ STAFF y STAFF_CATEGORIAS ================================ */
    SET IDENTITY_INSERT dbo.STAFF ON;
    INSERT dbo.STAFF (PK_id_staff, FK_id_usuario)
    SELECT s.PK_id_staff, mu.id_unificado
    FROM [$(BaseA)].dbo.STAFF s
    LEFT JOIN migra.mapa mu ON mu.entidad = N'USUARIOS' AND mu.origen = 'A' AND mu.id_origen = s.FK_id_usuario;
    SET IDENTITY_INSERT dbo.STAFF OFF;
    INSERT migra.mapa (entidad, origen, id_origen, id_unificado, regla)
    SELECT N'STAFF', 'A', PK_id_staff, PK_id_staff, 'ID_CONSERVADO' FROM [$(BaseA)].dbo.STAFF;

    MERGE dbo.STAFF AS d
    USING (SELECT s.PK_id_staff, mu.id_unificado AS id_usuario FROM [$(BaseB)].dbo.STAFF s
           LEFT JOIN migra.mapa mu ON mu.entidad = N'USUARIOS' AND mu.origen = 'B' AND mu.id_origen = s.FK_id_usuario) AS s ON 1 = 0
    WHEN NOT MATCHED THEN INSERT (FK_id_usuario) VALUES (s.id_usuario)
    OUTPUT N'STAFF', 'B', s.PK_id_staff, inserted.PK_id_staff, 'ID_NUEVO' INTO migra.mapa (entidad, origen, id_origen, id_unificado, regla);

    SET IDENTITY_INSERT dbo.STAFF_CATEGORIAS ON;
    INSERT dbo.STAFF_CATEGORIAS (PK_id_staff_categoria, FK_id_staff, FK_id_categoria)
    SELECT sc.PK_id_staff_categoria, ms.id_unificado, sc.FK_id_categoria
    FROM [$(BaseA)].dbo.STAFF_CATEGORIA sc
    LEFT JOIN migra.mapa ms ON ms.entidad = N'STAFF' AND ms.origen = 'A' AND ms.id_origen = sc.FK_id_staff;
    SET IDENTITY_INSERT dbo.STAFF_CATEGORIAS OFF;
    INSERT migra.mapa (entidad, origen, id_origen, id_unificado, regla)
    SELECT N'STAFF_CATEGORIAS', 'A', PK_id_staff_categoria, PK_id_staff_categoria, 'ID_CONSERVADO' FROM [$(BaseA)].dbo.STAFF_CATEGORIA;

    MERGE dbo.STAFF_CATEGORIAS AS d
    USING (SELECT sc.PK_id_staff_categoria, ms.id_unificado AS id_staff, sc.FK_id_categoria FROM [$(BaseB)].dbo.STAFF_CATEGORIAS sc
           LEFT JOIN migra.mapa ms ON ms.entidad = N'STAFF' AND ms.origen = 'B' AND ms.id_origen = sc.FK_id_staff) AS s ON 1 = 0
    WHEN NOT MATCHED THEN INSERT (FK_id_staff, FK_id_categoria) VALUES (s.id_staff, s.FK_id_categoria)
    OUTPUT N'STAFF_CATEGORIAS', 'B', s.PK_id_staff_categoria, inserted.PK_id_staff_categoria, 'ID_NUEVO'
        INTO migra.mapa (entidad, origen, id_origen, id_unificado, regla);

    /* ================================ RESPONSABLES ================================ */
    SET IDENTITY_INSERT dbo.RESPONSABLES ON;
    INSERT dbo.RESPONSABLES (PK_id_responsable, FK_id_persona, email, telefono)
    SELECT r.PK_id_responsable, mp.id_unificado, r.email, r.telefono
    FROM [$(BaseA)].dbo.RESPONSABLES r
    LEFT JOIN migra.mapa mp ON mp.entidad = N'PERSONAS' AND mp.origen = 'A' AND mp.id_origen = r.FK_id_persona;
    SET IDENTITY_INSERT dbo.RESPONSABLES OFF;
    INSERT migra.mapa (entidad, origen, id_origen, id_unificado, regla)
    SELECT N'RESPONSABLES', 'A', PK_id_responsable, PK_id_responsable, 'ID_CONSERVADO' FROM [$(BaseA)].dbo.RESPONSABLES;

    MERGE dbo.RESPONSABLES AS d
    USING (SELECT r.PK_id_responsable, mp.id_unificado AS id_persona, r.email, r.telefono FROM [$(BaseB)].dbo.RESPONSABLES r
           LEFT JOIN migra.mapa mp ON mp.entidad = N'PERSONAS' AND mp.origen = 'B' AND mp.id_origen = r.FK_id_persona) AS s ON 1 = 0
    WHEN NOT MATCHED THEN INSERT (FK_id_persona, email, telefono) VALUES (s.id_persona, s.email, s.telefono)
    OUTPUT N'RESPONSABLES', 'B', s.PK_id_responsable, inserted.PK_id_responsable, 'ID_NUEVO'
        INTO migra.mapa (entidad, origen, id_origen, id_unificado, regla);

    /* ================================ JUGADORES_RESPONSABLES ================================ */
    SET IDENTITY_INSERT dbo.JUGADORES_RESPONSABLES ON;
    INSERT dbo.JUGADORES_RESPONSABLES (PK_id_jugador_responsable, FK_id_responsable, FK_id_jugador, parentesco)
    SELECT jr.PK_id_jugador_responsable, mr.id_unificado, mj.id_unificado, NULL
    FROM [$(BaseA)].dbo.Jugadore_Responsable jr
    LEFT JOIN migra.mapa mr ON mr.entidad = N'RESPONSABLES' AND mr.origen = 'A' AND mr.id_origen = jr.FK_id_Responsable
    LEFT JOIN migra.mapa mj ON mj.entidad = N'JUGADORES' AND mj.origen = 'A' AND mj.id_origen = jr.FK_id_Jugador;
    SET IDENTITY_INSERT dbo.JUGADORES_RESPONSABLES OFF;
    INSERT migra.mapa (entidad, origen, id_origen, id_unificado, regla)
    SELECT N'JUGADORES_RESPONSABLES', 'A', PK_id_jugador_responsable, PK_id_jugador_responsable, 'ID_CONSERVADO' FROM [$(BaseA)].dbo.Jugadore_Responsable;

    MERGE dbo.JUGADORES_RESPONSABLES AS d
    USING (SELECT jr.PK_id_jugador_responsable, mr.id_unificado AS id_responsable, mj.id_unificado AS id_jugador, jr.parentesco
           FROM [$(BaseB)].dbo.JUGADORES_RESPONSABLES jr
           LEFT JOIN migra.mapa mr ON mr.entidad = N'RESPONSABLES' AND mr.origen = 'B' AND mr.id_origen = jr.FK_id_responsable
           LEFT JOIN migra.mapa mj ON mj.entidad = N'JUGADORES' AND mj.origen = 'B' AND mj.id_origen = jr.FK_id_jugador) AS s ON 1 = 0
    WHEN NOT MATCHED THEN INSERT (FK_id_responsable, FK_id_jugador, parentesco) VALUES (s.id_responsable, s.id_jugador, s.parentesco)
    OUTPUT N'JUGADORES_RESPONSABLES', 'B', s.PK_id_jugador_responsable, inserted.PK_id_jugador_responsable, 'ID_NUEVO'
        INTO migra.mapa (entidad, origen, id_origen, id_unificado, regla);

    /* ================================ FICHAS_MEDICAS ================================ */
    SET IDENTITY_INSERT dbo.FICHAS_MEDICAS ON;
    INSERT dbo.FICHAS_MEDICAS (PK_id_ficha_medica, FK_id_jugador, obra_social, numero_afiliado_os, grupo_sanguineo, factor, antecedentes_salud,
                               medicamentos_cronicos, condiciones_cronicas, apto_fisico_vencimiento, observaciones, fecha_actualizacion)
    SELECT f.PK_id_ficha_medica, mj.id_unificado, f.obra_social, f.numero_afiliado_OS, f.grupo_sanguineo, f.factor, f.antecedentes_salud,
           f.medicamentos_cronicos, f.condiciones_cronicas, f.apto_fisico_vencimiento, f.observaciones, f.fecha_actualizacion
    FROM [$(BaseA)].dbo.FICHAS_MEDICAS f
    LEFT JOIN migra.mapa mj ON mj.entidad = N'JUGADORES' AND mj.origen = 'A' AND mj.id_origen = f.FK_id_jugador;
    SET IDENTITY_INSERT dbo.FICHAS_MEDICAS OFF;
    INSERT migra.mapa (entidad, origen, id_origen, id_unificado, regla)
    SELECT N'FICHAS_MEDICAS', 'A', PK_id_ficha_medica, PK_id_ficha_medica, 'ID_CONSERVADO' FROM [$(BaseA)].dbo.FICHAS_MEDICAS;

    MERGE dbo.FICHAS_MEDICAS AS d
    USING (SELECT f.*, mj.id_unificado AS id_jugador FROM [$(BaseB)].dbo.FICHAS_MEDICAS f
           LEFT JOIN migra.mapa mj ON mj.entidad = N'JUGADORES' AND mj.origen = 'B' AND mj.id_origen = f.FK_id_jugador) AS s ON 1 = 0
    WHEN NOT MATCHED THEN
        INSERT (FK_id_jugador, obra_social, numero_afiliado_os, grupo_sanguineo, factor, antecedentes_salud, medicamentos_cronicos,
                condiciones_cronicas, apto_fisico_vencimiento, observaciones, fecha_actualizacion)
        VALUES (s.id_jugador, s.obra_social, s.numero_afiliado_os, s.grupo_sanguineo, s.factor, s.antecedentes_salud, s.medicamentos_cronicos,
                s.condiciones_cronicas, s.apto_fisico_vencimiento, s.observaciones, s.fecha_actualizacion)
    OUTPUT N'FICHAS_MEDICAS', 'B', s.PK_id_ficha_medica, inserted.PK_id_ficha_medica, 'ID_NUEVO'
        INTO migra.mapa (entidad, origen, id_origen, id_unificado, regla);

    /* ================================ PARTIDOS (solo B) ================================ */
    MERGE dbo.HISTORIAL_PARTIDOS AS d
    USING (SELECT * FROM [$(BaseB)].dbo.HISTORIAL_PARTIDOS) AS s ON 1 = 0
    WHEN NOT MATCHED THEN
        INSERT (FK_id_categoria, fecha_partido, rival, condicion_localia, resultado, observaciones)
        VALUES (s.FK_id_categoria, s.fecha_partido, s.rival, s.condicion_localia, s.resultado, s.observaciones)
    OUTPUT N'HISTORIAL_PARTIDOS', 'B', s.PK_id_partido, inserted.PK_id_partido, 'ID_NUEVO'
        INTO migra.mapa (entidad, origen, id_origen, id_unificado, regla);

    MERGE dbo.JUGADORES_PARTIDOS AS d
    USING (SELECT jp.*, mp.id_unificado AS id_partido, mj.id_unificado AS id_jugador FROM [$(BaseB)].dbo.JUGADORES_PARTIDOS jp
           LEFT JOIN migra.mapa mp ON mp.entidad = N'HISTORIAL_PARTIDOS' AND mp.origen = 'B' AND mp.id_origen = jp.FK_id_partido
           LEFT JOIN migra.mapa mj ON mj.entidad = N'JUGADORES' AND mj.origen = 'B' AND mj.id_origen = jp.FK_id_jugador) AS s ON 1 = 0
    WHEN NOT MATCHED THEN
        INSERT (FK_id_partido, FK_id_jugador, minutos_jugados, goles, tarjetas_amarillas, tarjeta_roja)
        VALUES (s.id_partido, s.id_jugador, s.minutos_jugados, s.goles, s.tarjetas_amarillas, s.tarjeta_roja)
    OUTPUT N'JUGADORES_PARTIDOS', 'B', s.PK_id_jugador_partido, inserted.PK_id_jugador_partido, 'ID_NUEVO'
        INTO migra.mapa (entidad, origen, id_origen, id_unificado, regla);

    /* ================================ ASISTENCIAS ================================ */
    -- FK_id_persona (nueva en la base unificada) se deriva del jugador ya traducido.
    SET IDENTITY_INSERT dbo.ASISTENCIAS ON;
    INSERT dbo.ASISTENCIAS (PK_id_asistencia, FK_id_jugador, FK_id_staff, fecha_hora, estado_acceso, FK_id_persona)
    SELECT a.PK_id_asistencia, mj.id_unificado, ms.id_unificado, a.fecha_hora, a.estado_acceso, j.FK_id_persona
    FROM [$(BaseA)].dbo.ASISTENCIAS a
    LEFT JOIN migra.mapa mj ON mj.entidad = N'JUGADORES' AND mj.origen = 'A' AND mj.id_origen = a.FK_id_jugador
    LEFT JOIN migra.mapa ms ON ms.entidad = N'STAFF' AND ms.origen = 'A' AND ms.id_origen = a.FK_id_staff
    LEFT JOIN dbo.JUGADORES j ON j.PK_id_jugador = mj.id_unificado;
    SET IDENTITY_INSERT dbo.ASISTENCIAS OFF;
    INSERT migra.mapa (entidad, origen, id_origen, id_unificado, regla)
    SELECT N'ASISTENCIAS', 'A', PK_id_asistencia, PK_id_asistencia, 'ID_CONSERVADO' FROM [$(BaseA)].dbo.ASISTENCIAS;

    MERGE dbo.ASISTENCIAS AS d
    USING (SELECT a.*, mj.id_unificado AS id_jugador, ms.id_unificado AS id_staff, j.FK_id_persona AS id_persona
           FROM [$(BaseB)].dbo.ASISTENCIAS a
           LEFT JOIN migra.mapa mj ON mj.entidad = N'JUGADORES' AND mj.origen = 'B' AND mj.id_origen = a.FK_id_jugador
           LEFT JOIN migra.mapa ms ON ms.entidad = N'STAFF' AND ms.origen = 'B' AND ms.id_origen = a.FK_id_staff
           LEFT JOIN dbo.JUGADORES j ON j.PK_id_jugador = mj.id_unificado) AS s ON 1 = 0
    WHEN NOT MATCHED THEN
        INSERT (FK_id_jugador, FK_id_staff, fecha_hora, estado_acceso, FK_id_persona)
        VALUES (s.id_jugador, s.id_staff, s.fecha_hora, s.estado_acceso, s.id_persona)
    OUTPUT N'ASISTENCIAS', 'B', s.PK_id_asistencia, inserted.PK_id_asistencia, 'ID_NUEVO'
        INTO migra.mapa (entidad, origen, id_origen, id_unificado, regla);

    /* ================================ BECAS / DESCUENTOS ================================ */
    -- TIPOS_DESCUENTO es un catalogo cerrado: se traduce por nombre (A tiene "Descuento" en el id 3; la base unificada, en el 2).
    INSERT migra.mapa (entidad, origen, id_origen, id_unificado, regla)
    SELECT N'TIPOS_DESCUENTO', 'A', o.PK_id_descuento, d.PK_id_descuento,
           CASE WHEN o.PK_id_descuento = d.PK_id_descuento THEN 'ID_CONSERVADO' ELSE 'TRADUCIDO_POR_NOMBRE' END
    FROM [$(BaseA)].dbo.TIPO_DESCUENTO o
    JOIN dbo.TIPOS_DESCUENTO d ON d.tipo_descuento = o.tipo_descuento COLLATE DATABASE_DEFAULT;

    SET IDENTITY_INSERT dbo.JUGADORES_DESCUENTOS ON;
    INSERT dbo.JUGADORES_DESCUENTOS (PK_id_jugador_descuento, FK_id_jugador, FK_id_descuento, estado_activo, tipo_valor, porcentaje, monto_fijo,
                                     fecha_inicio, fecha_fin, fecha_cancelacion)
    SELECT jd.PK_id_jugador_descuento, mj.id_unificado, mt.id_unificado, jd.estado_activo, jd.tipo_valor, jd.porcentaje, jd.monto_fijo,
           jd.fecha_inicio, jd.fecha_fin, jd.fecha_cancelacion
    FROM [$(BaseA)].dbo.JUGADORES_DESCUENTOS jd
    LEFT JOIN migra.mapa mj ON mj.entidad = N'JUGADORES' AND mj.origen = 'A' AND mj.id_origen = jd.FK_id_jugador
    LEFT JOIN migra.mapa mt ON mt.entidad = N'TIPOS_DESCUENTO' AND mt.origen = 'A' AND mt.id_origen = jd.FK_id_descuento;
    SET IDENTITY_INSERT dbo.JUGADORES_DESCUENTOS OFF;
    INSERT migra.mapa (entidad, origen, id_origen, id_unificado, regla)
    SELECT N'JUGADORES_DESCUENTOS', 'A', PK_id_jugador_descuento, PK_id_jugador_descuento, 'ID_CONSERVADO' FROM [$(BaseA)].dbo.JUGADORES_DESCUENTOS;

    /* ================================ PAGOS ================================ */
    SET @siguiente = (SELECT ISNULL(MAX(PK_id_pago), 0) + 1 FROM [$(BaseA)].dbo.PAGOS);
    IF @siguiente > (SELECT CASE WHEN last_used_value IS NULL THEN CONVERT(bigint, start_value) ELSE CONVERT(bigint, last_used_value) + 1 END
                     FROM sys.sequences WHERE object_id = OBJECT_ID(N'dbo.SEQ_PAGOS'))
    BEGIN
        SET @sql = N'ALTER SEQUENCE dbo.SEQ_PAGOS RESTART WITH ' + CONVERT(nvarchar(20), @siguiente) + N';';
        EXEC sys.sp_executesql @sql;
    END;

    INSERT dbo.PAGOS (PK_id_pago, FK_id_jugador, monto_base, FK_id_jugador_descuento, monto_final, fecha_pago, metodo_pago, fecha_vencimiento,
                      estado, concepto, fecha_hora_registro, FK_id_usuario_registro)
    SELECT pg.PK_id_pago, mj.id_unificado, pg.monto_base, md.id_unificado, pg.monto_final, pg.fecha_pago, pg.metodo_pago, pg.fecha_vencimiento,
           pg.estado, pg.concepto, pg.fecha_hora_registro, mu.id_unificado
    FROM [$(BaseA)].dbo.PAGOS pg
    LEFT JOIN migra.mapa mj ON mj.entidad = N'JUGADORES' AND mj.origen = 'A' AND mj.id_origen = pg.FK_id_jugador
    LEFT JOIN migra.mapa md ON md.entidad = N'JUGADORES_DESCUENTOS' AND md.origen = 'A' AND md.id_origen = pg.FK_id_jugador_descuento
    LEFT JOIN migra.mapa mu ON mu.entidad = N'USUARIOS' AND mu.origen = 'A' AND mu.id_origen = pg.FK_id_usuario_registro;
    INSERT migra.mapa (entidad, origen, id_origen, id_unificado, regla)
    SELECT N'PAGOS', 'A', PK_id_pago, PK_id_pago, 'ID_CONSERVADO' FROM [$(BaseA)].dbo.PAGOS;

    -- B no tiene concepto: sus pagos son cuotas (lo unico que B registra). Queda anotado por fila.
    INSERT migra.transformacion (entidad, origen, id_origen, columna, valor_origen, valor_destino, regla)
    SELECT N'PAGOS', 'B', PK_id_pago, N'concepto', NULL, N'Cuota', 'CONCEPTO_CUOTA_POR_DEFECTO' FROM [$(BaseB)].dbo.PAGOS;

    MERGE dbo.PAGOS AS d
    USING (SELECT pg.*, mj.id_unificado AS id_jugador FROM [$(BaseB)].dbo.PAGOS pg
           LEFT JOIN migra.mapa mj ON mj.entidad = N'JUGADORES' AND mj.origen = 'B' AND mj.id_origen = pg.FK_id_jugador) AS s ON 1 = 0
    WHEN NOT MATCHED THEN
        INSERT (FK_id_jugador, monto_base, FK_id_jugador_descuento, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado, concepto)
        VALUES (s.id_jugador, s.monto_base, NULL, s.monto_final, s.fecha_pago, s.metodo_pago, s.fecha_vencimiento, s.estado, 'Cuota')
    OUTPUT N'PAGOS', 'B', s.PK_id_pago, inserted.PK_id_pago, 'ID_NUEVO' INTO migra.mapa (entidad, origen, id_origen, id_unificado, regla);

    /* ================================ AUDITORIA de A ================================ */
    -- Se conserva el historial tal cual (id, fecha, operador, datos). Solo se traduce el nombre de la entidad al de la base
    -- unificada y, si la entidad se fusiono, su id. Cada traduccion queda anotada.
    CREATE TABLE #entidades (vieja sysname COLLATE DATABASE_DEFAULT PRIMARY KEY, nueva sysname COLLATE DATABASE_DEFAULT NOT NULL);
    INSERT #entidades (vieja, nueva) VALUES
        (N'PERSONA', N'PERSONAS'), (N'USUARIO', N'USUARIOS'), (N'TIPO_DESCUENTO', N'TIPOS_DESCUENTO'),
        (N'Jugadore_Responsable', N'JUGADORES_RESPONSABLES'), (N'STAFF_CATEGORIA', N'STAFF_CATEGORIAS');

    SELECT a.PK_id_evento, a.id_usuario, a.email_usuario, a.entidad COLLATE DATABASE_DEFAULT AS entidad_origen,
           ISNULL(e.nueva, a.entidad COLLATE DATABASE_DEFAULT) AS entidad,
           a.id_entidad COLLATE DATABASE_DEFAULT AS id_entidad_origen,
           ISNULL(CONVERT(nvarchar(128), m.id_unificado), a.id_entidad COLLATE DATABASE_DEFAULT) AS id_entidad,
           a.accion, a.fecha_utc, a.datos_antes, a.datos_despues, a.nombre_usuario, a.apellido_usuario, a.dni_usuario
    INTO #aud
    FROM [$(BaseA)].dbo.AUDITORIA_CAMBIOS a
    LEFT JOIN #entidades e ON e.vieja = a.entidad COLLATE DATABASE_DEFAULT
    LEFT JOIN migra.mapa m ON m.entidad = ISNULL(e.nueva, a.entidad COLLATE DATABASE_DEFAULT) AND m.origen = 'A'
                          AND m.id_origen = TRY_CONVERT(int, a.id_entidad);

    INSERT migra.transformacion (entidad, origen, id_origen, columna, valor_origen, valor_destino, regla)
    SELECT N'AUDITORIA_CAMBIOS', 'A', PK_id_evento, N'entidad', entidad_origen, entidad, 'NOMBRE_DE_TABLA_UNIFICADO'
    FROM #aud WHERE entidad_origen <> entidad
    UNION ALL
    SELECT N'AUDITORIA_CAMBIOS', 'A', PK_id_evento, N'id_entidad', id_entidad_origen, id_entidad, 'ID_DE_ENTIDAD_FUSIONADA'
    FROM #aud WHERE id_entidad_origen <> id_entidad;

    SET IDENTITY_INSERT dbo.AUDITORIA_CAMBIOS ON;
    INSERT dbo.AUDITORIA_CAMBIOS (PK_id_evento, id_usuario, email_usuario, entidad, id_entidad, accion, fecha_utc, datos_antes, datos_despues,
                                  nombre_usuario, apellido_usuario, dni_usuario)
    SELECT a.PK_id_evento, mu.id_unificado, a.email_usuario, a.entidad, a.id_entidad, a.accion, a.fecha_utc, a.datos_antes, a.datos_despues,
           a.nombre_usuario, a.apellido_usuario, a.dni_usuario
    FROM #aud a
    LEFT JOIN migra.mapa mu ON mu.entidad = N'USUARIOS' AND mu.origen = 'A' AND mu.id_origen = a.id_usuario;
    SET IDENTITY_INSERT dbo.AUDITORIA_CAMBIOS OFF;
    INSERT migra.mapa (entidad, origen, id_origen, id_unificado, regla)
    SELECT N'AUDITORIA_CAMBIOS', 'A', PK_id_evento, PK_id_evento, 'ID_CONSERVADO' FROM #aud;

    /* ================================ Cierre ================================ */
    INSERT migra.conteo (etapa, entidad, origen, cantidad)
    SELECT 'DESTINO', v.entidad, 'U', v.cantidad
    FROM (VALUES
        (N'PERSONAS', (SELECT COUNT(*) FROM dbo.PERSONAS)), (N'USUARIOS', (SELECT COUNT(*) FROM dbo.USUARIOS)),
        (N'JUGADORES', (SELECT COUNT(*) FROM dbo.JUGADORES)), (N'JUGADORES_PERIODOS', (SELECT COUNT(*) FROM dbo.JUGADORES_PERIODOS)),
        (N'STAFF', (SELECT COUNT(*) FROM dbo.STAFF)), (N'STAFF_CATEGORIAS', (SELECT COUNT(*) FROM dbo.STAFF_CATEGORIAS)),
        (N'RESPONSABLES', (SELECT COUNT(*) FROM dbo.RESPONSABLES)), (N'JUGADORES_RESPONSABLES', (SELECT COUNT(*) FROM dbo.JUGADORES_RESPONSABLES)),
        (N'FICHAS_MEDICAS', (SELECT COUNT(*) FROM dbo.FICHAS_MEDICAS)), (N'HISTORIAL_PARTIDOS', (SELECT COUNT(*) FROM dbo.HISTORIAL_PARTIDOS)),
        (N'JUGADORES_PARTIDOS', (SELECT COUNT(*) FROM dbo.JUGADORES_PARTIDOS)), (N'ASISTENCIAS', (SELECT COUNT(*) FROM dbo.ASISTENCIAS)),
        (N'JUGADORES_DESCUENTOS', (SELECT COUNT(*) FROM dbo.JUGADORES_DESCUENTOS)), (N'PAGOS', (SELECT COUNT(*) FROM dbo.PAGOS)),
        (N'AUDITORIA_CAMBIOS', (SELECT COUNT(*) FROM dbo.AUDITORIA_CAMBIOS))
    ) AS v (entidad, cantidad);

    SET @sql = (SELECT STRING_AGG(CONVERT(nvarchar(max), N'ENABLE TRIGGER dbo.' + QUOTENAME(nombre) + N' ON ' + tabla + N';'), N' ') FROM @triggers);
    EXEC sys.sp_executesql @sql;
    IF EXISTS (SELECT 1 FROM sys.triggers WHERE parent_class = 1 AND is_disabled = 1)
        THROW 53140, N'Quedo un trigger deshabilitado despues de la carga.', 1;

    UPDATE migra.ejecucion SET estado = 'OK', fin_utc = SYSUTCDATETIME(),
           detalle = CONCAT(N'Personas: ', (SELECT COUNT(*) FROM dbo.PERSONAS), N'. Jugadores: ', (SELECT COUNT(*) FROM dbo.JUGADORES),
                            N'. Pagos: ', (SELECT COUNT(*) FROM dbo.PAGOS), N'. Transformaciones: ', (SELECT COUNT(*) FROM migra.transformacion), N'.')
    WHERE PK_id_ejecucion = @ejecucion;

    COMMIT TRANSACTION;
    PRINT N'Importacion OK.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    UPDATE migra.ejecucion SET estado = 'FALLO', fin_utc = SYSUTCDATETIME(),
           detalle = LEFT(CONCAT(N'Error ', ERROR_NUMBER(), N' (linea ', ERROR_LINE(), N'): ', ERROR_MESSAGE()), 4000)
    WHERE PK_id_ejecucion = @ejecucion;
    THROW;
END CATCH;
GO
