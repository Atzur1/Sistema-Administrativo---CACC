/* =====================================================================================
   HU-053 - Paso 2: analisis de identidades por DNI. Solo LEE las bases de origen y solo escribe en el esquema migra.
   Variables sqlcmd: BaseA (Portal Administrativo, nombres PERSONA/USUARIO) y BaseB (Portal Deportivo, PERSONAS/USUARIOS).

   Regla acordada: el DNI identifica a la persona.
     - DNI normalizado: sin espacios, puntos ni guiones; valido si tiene 6 a 9 digitos. Se compara como TEXTO; dos DNI que
       solo difieren en ceros a la izquierda se marcan como conflicto (no se unen solos).
     - Mismo DNI en A y en B: misma persona. Quedan los datos de A; B completa solo lo que en A esta vacio.
     - Mismo DNI repetido dentro de un mismo sistema: se une solo si los registros son identicos en todos sus datos y a lo
       sumo uno tiene dependientes (jugador, usuario o responsable). Si no, es un conflicto.
     - Sin DNI valido: la persona se importa sola, sin unirse a nadie.
   Un conflicto solo se destraba con una decision humana en migra.resolucion_dni. Sin ella, este paso registra la
   ejecucion como BLOQUEADA y se detiene: el paso 3 no corre. Nunca se borra ni se elige un registro en silencio.

   Se puede repetir: recalcula persona_origen, jugador_origen, persona_plan y conflicto_dni desde cero.
   ===================================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF EXISTS (SELECT 1 FROM migra.ejecucion WHERE estado IN ('OK', 'EN_CURSO'))
    THROW 53000, N'Esta base ya tiene una importacion hecha (o en curso). Para repetir la prueba, recrear la base destino.', 1;
GO

TRUNCATE TABLE migra.persona_origen;
TRUNCATE TABLE migra.jugador_origen;
TRUNCATE TABLE migra.persona_plan;
TRUNCATE TABLE migra.conflicto_dni;

INSERT migra.persona_origen (origen, id_origen, dni_original, dni, grupo, nombre, apellido, genero, fecha_nacimiento, domicilio,
                             n_jugadores, n_usuarios, n_responsables)
SELECT 'A', p.PK_id_persona, p.Dni COLLATE DATABASE_DEFAULT, v.dni, ISNULL(v.dni, CONCAT('SIN_DNI:A:', p.PK_id_persona)),
       p.nombre COLLATE DATABASE_DEFAULT, p.apellido COLLATE DATABASE_DEFAULT, p.genero COLLATE DATABASE_DEFAULT,
       p.fecha_de_nacimiento, NULL,
       (SELECT COUNT(*) FROM [$(BaseA)].dbo.JUGADORES j WHERE j.FK_id_persona = p.PK_id_persona),
       (SELECT COUNT(*) FROM [$(BaseA)].dbo.USUARIO u WHERE u.FK_id_persona = p.PK_id_persona),
       (SELECT COUNT(*) FROM [$(BaseA)].dbo.RESPONSABLES r WHERE r.FK_id_persona = p.PK_id_persona)
FROM [$(BaseA)].dbo.PERSONA p
CROSS APPLY (SELECT REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(p.Dni COLLATE DATABASE_DEFAULT)), '.', ''), ' ', ''), '-', '') AS limpio) l
CROSS APPLY (SELECT CASE WHEN LEN(l.limpio) BETWEEN 6 AND 9 AND l.limpio NOT LIKE '%[^0-9]%' THEN l.limpio END AS dni) v;

INSERT migra.persona_origen (origen, id_origen, dni_original, dni, grupo, nombre, apellido, genero, fecha_nacimiento, domicilio,
                             n_jugadores, n_usuarios, n_responsables)
SELECT 'B', p.PK_id_persona, p.dni COLLATE DATABASE_DEFAULT, v.dni, ISNULL(v.dni, CONCAT('SIN_DNI:B:', p.PK_id_persona)),
       p.nombre COLLATE DATABASE_DEFAULT, p.apellido COLLATE DATABASE_DEFAULT, p.genero COLLATE DATABASE_DEFAULT,
       p.fecha_de_nacimiento, p.domicilio COLLATE DATABASE_DEFAULT,
       (SELECT COUNT(*) FROM [$(BaseB)].dbo.JUGADORES j WHERE j.FK_id_persona = p.PK_id_persona),
       (SELECT COUNT(*) FROM [$(BaseB)].dbo.USUARIOS u WHERE u.FK_id_persona = p.PK_id_persona),
       (SELECT COUNT(*) FROM [$(BaseB)].dbo.RESPONSABLES r WHERE r.FK_id_persona = p.PK_id_persona)
FROM [$(BaseB)].dbo.PERSONAS p
CROSS APPLY (SELECT REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(p.dni COLLATE DATABASE_DEFAULT)), '.', ''), ' ', ''), '-', '') AS limpio) l
CROSS APPLY (SELECT CASE WHEN LEN(l.limpio) BETWEEN 6 AND 9 AND l.limpio NOT LIKE '%[^0-9]%' THEN l.limpio END AS dni) v;

INSERT migra.jugador_origen (origen, id_origen, id_persona_origen, grupo, FK_id_categoria)
SELECT 'A', j.PK_id_jugador, j.FK_id_persona, po.grupo, j.FK_id_categoria
FROM [$(BaseA)].dbo.JUGADORES j
LEFT JOIN migra.persona_origen po ON po.origen = 'A' AND po.id_origen = j.FK_id_persona;

INSERT migra.jugador_origen (origen, id_origen, id_persona_origen, grupo, FK_id_categoria)
SELECT 'B', j.PK_id_jugador, j.FK_id_persona, po.grupo, j.FK_id_categoria
FROM [$(BaseB)].dbo.JUGADORES j
LEFT JOIN migra.persona_origen po ON po.origen = 'B' AND po.id_origen = j.FK_id_persona;

/* ------------------------------- Conflictos ------------------------------- */
-- resoluble = 0: ni una decision en resolucion_dni lo destraba; hay que corregir el dato en el sistema de origen.
-- COLLATE DATABASE_DEFAULT: sin el, la tabla temporal toma la collation de tempdb y no se puede comparar con migra.
CREATE TABLE #motivo (grupo varchar(40) COLLATE DATABASE_DEFAULT NOT NULL, motivo nvarchar(300) COLLATE DATABASE_DEFAULT NOT NULL, resoluble bit NOT NULL);

INSERT #motivo (grupo, motivo, resoluble)
SELECT grupo, N'nombre o apellido distinto con el mismo DNI', 1
FROM migra.persona_origen
WHERE dni IS NOT NULL
GROUP BY grupo
HAVING COUNT(DISTINCT CONCAT(LTRIM(RTRIM(nombre)), N'|', LTRIM(RTRIM(apellido)))) > 1;

INSERT #motivo (grupo, motivo, resoluble)
SELECT grupo,
       CASE WHEN COUNT(DISTINCT CONCAT(nombre, N'|', apellido, N'|', ISNULL(genero, N'<NULL>'), N'|',
                                       ISNULL(CONVERT(char(10), fecha_nacimiento, 23), N'<NULL>'), N'|', ISNULL(domicilio, N'<NULL>'))) > 1
            THEN CONCAT(N'DNI repetido dentro de ', origen, N' con datos distintos')
            ELSE CONCAT(N'DNI repetido dentro de ', origen, N' y mas de un registro con dependientes') END, 1
FROM migra.persona_origen
WHERE dni IS NOT NULL
GROUP BY grupo, origen
HAVING COUNT(*) > 1
   AND (COUNT(DISTINCT CONCAT(nombre, N'|', apellido, N'|', ISNULL(genero, N'<NULL>'), N'|',
                              ISNULL(CONVERT(char(10), fecha_nacimiento, 23), N'<NULL>'), N'|', ISNULL(domicilio, N'<NULL>'))) > 1
        OR SUM(CASE WHEN n_jugadores + n_usuarios + n_responsables > 0 THEN 1 ELSE 0 END) > 1);

INSERT #motivo (grupo, motivo, resoluble)
SELECT grupo, N'mas de un usuario para la misma persona (USUARIOS admite uno por persona)', 0
FROM migra.persona_origen
GROUP BY grupo
HAVING SUM(n_usuarios) > 1;

INSERT #motivo (grupo, motivo, resoluble)
SELECT grupo, N'una misma persona de origen con mas de un jugador', 0
FROM migra.persona_origen
GROUP BY grupo
HAVING MAX(n_jugadores) > 1;

INSERT #motivo (grupo, motivo, resoluble)
SELECT grupo, N'responsable cargado en mas de un registro de la misma persona', 1
FROM migra.persona_origen
GROUP BY grupo
HAVING SUM(CASE WHEN n_responsables > 0 THEN 1 ELSE 0 END) > 1;

INSERT #motivo (grupo, motivo, resoluble)
SELECT grupo, N'los jugadores de esta persona estan en categorias distintas', 1
FROM migra.jugador_origen
WHERE grupo IS NOT NULL
GROUP BY grupo
HAVING COUNT(DISTINCT FK_id_categoria) > 1;

INSERT #motivo (grupo, motivo, resoluble)
SELECT DISTINCT p.grupo, CONCAT(N'otro DNI con el mismo valor numerico: ', q.dni), 0
FROM (SELECT DISTINCT grupo, dni FROM migra.persona_origen WHERE dni IS NOT NULL) p
JOIN (SELECT DISTINCT grupo, dni FROM migra.persona_origen WHERE dni IS NOT NULL) q
  ON CONVERT(bigint, q.dni) = CONVERT(bigint, p.dni) AND q.grupo <> p.grupo;

INSERT migra.conflicto_dni (grupo, origen, id_persona_origen, nombre, apellido, fecha_nacimiento, dependientes, motivo)
SELECT p.grupo, p.origen, p.id_origen, p.nombre, p.apellido, p.fecha_nacimiento, p.n_jugadores + p.n_usuarios + p.n_responsables, m.motivos
FROM migra.persona_origen p
JOIN (SELECT grupo, STRING_AGG(motivo, N'; ') AS motivos
      FROM (SELECT DISTINCT grupo, motivo FROM #motivo) x GROUP BY grupo) m ON m.grupo = p.grupo;

/* ------------------------------- Resoluciones ------------------------------- */
DECLARE @obsoletas nvarchar(2000) = (
    SELECT STRING_AGG(CONVERT(nvarchar(max), CONCAT(r.grupo, N' (', r.origen, N':', r.id_persona_origen, N')')), N', ')
    FROM migra.resolucion_dni r
    WHERE NOT EXISTS (SELECT 1 FROM migra.conflicto_dni c
                      WHERE c.grupo = r.grupo AND c.origen = r.origen AND c.id_persona_origen = r.id_persona_origen));

DECLARE @pendientes nvarchar(2000) = (
    SELECT STRING_AGG(CONVERT(nvarchar(max), g.grupo), N', ')
    FROM (SELECT c.grupo,
                 COUNT(*) AS filas,
                 COUNT(r.accion) AS resueltas,
                 SUM(CASE WHEN r.accion = 'CANONICA' THEN 1 ELSE 0 END) AS canonicas
          FROM migra.conflicto_dni c
          LEFT JOIN migra.resolucion_dni r
            ON r.grupo = c.grupo AND r.origen = c.origen AND r.id_persona_origen = c.id_persona_origen
          GROUP BY c.grupo) g
    WHERE g.resueltas < g.filas OR g.canonicas <> 1
       OR EXISTS (SELECT 1 FROM #motivo m WHERE m.grupo = g.grupo AND m.resoluble = 0));

PRINT N'--- Conflictos de identidad (migra.conflicto_dni) ---';
SELECT c.grupo, c.origen, c.id_persona_origen, c.apellido, c.nombre, c.fecha_nacimiento, c.dependientes, c.motivo,
       r.accion AS resolucion, r.es_prueba
FROM migra.conflicto_dni c
LEFT JOIN migra.resolucion_dni r ON r.grupo = c.grupo AND r.origen = c.origen AND r.id_persona_origen = c.id_persona_origen
ORDER BY c.grupo, c.origen, c.id_persona_origen;

IF @obsoletas IS NOT NULL OR @pendientes IS NOT NULL
BEGIN
    DECLARE @detalle nvarchar(4000) = LEFT(CONCAT(
        CASE WHEN @pendientes IS NOT NULL THEN N'Conflictos de DNI sin resolver: ' + @pendientes + N'. ' END,
        CASE WHEN @obsoletas IS NOT NULL THEN N'Resoluciones que ya no corresponden a un conflicto (los datos cambiaron): ' + @obsoletas + N'.' END), 4000);
    INSERT migra.ejecucion (base_a, base_b, estado, fin_utc, detalle)
    VALUES (N'$(BaseA)', N'$(BaseB)', 'BLOQUEADA', SYSUTCDATETIME(), @detalle);
    DECLARE @mensaje nvarchar(2048) = LEFT(N'Importacion BLOQUEADA. ' + @detalle
        + N' Revisar migra.conflicto_dni y cargar la decision en migra.resolucion_dni (ver database/unificacion/README.md).', 2048);
    THROW 53001, @mensaje, 1;
END;

/* ------------------------------- Plan por persona ------------------------------- */
-- Canonica de un grupo sin conflicto: la de A (sus datos tienen precedencia y conserva su id), y dentro del mismo
-- sistema la que tiene dependientes y luego el id mas bajo. En un grupo en conflicto, la que eligio la persona.
INSERT migra.persona_plan (origen, id_origen, grupo, es_canonica, regla)
SELECT p.origen, p.id_origen, p.grupo,
       CASE WHEN c.grupo IS NOT NULL THEN CASE WHEN r.accion = 'CANONICA' THEN 1 ELSE 0 END
            WHEN ROW_NUMBER() OVER (PARTITION BY p.grupo
                                    ORDER BY CASE p.origen WHEN 'A' THEN 0 ELSE 1 END,
                                             CASE WHEN p.n_jugadores + p.n_usuarios + p.n_responsables > 0 THEN 0 ELSE 1 END,
                                             p.id_origen) = 1 THEN 1
            ELSE 0 END,
       CASE WHEN c.grupo IS NOT NULL THEN 'RESOLUCION_HUMANA'
            WHEN g.filas = 1 THEN 'UNICA'
            WHEN g.origenes > 1 THEN 'MISMO_DNI_ENTRE_SISTEMAS'
            ELSE 'DUPLICADO_IDENTICO' END
FROM migra.persona_origen p
JOIN (SELECT grupo, COUNT(*) AS filas, COUNT(DISTINCT origen) AS origenes FROM migra.persona_origen GROUP BY grupo) g ON g.grupo = p.grupo
LEFT JOIN (SELECT DISTINCT grupo FROM migra.conflicto_dni) c ON c.grupo = p.grupo
LEFT JOIN migra.resolucion_dni r ON r.grupo = p.grupo AND r.origen = p.origen AND r.id_persona_origen = p.id_origen;

PRINT N'--- Plan de unificacion de personas ---';
SELECT regla, origen, COUNT(*) AS personas_origen, SUM(CAST(es_canonica AS int)) AS canonicas
FROM migra.persona_plan GROUP BY regla, origen ORDER BY regla, origen;

SELECT (SELECT COUNT(*) FROM migra.persona_origen WHERE origen = 'A') AS personas_A,
       (SELECT COUNT(*) FROM migra.persona_origen WHERE origen = 'B') AS personas_B,
       (SELECT COUNT(DISTINCT grupo) FROM migra.persona_origen) AS personas_unificadas_esperadas,
       (SELECT COUNT(*) FROM migra.persona_origen WHERE dni IS NULL) AS sin_dni_valido,
       (SELECT COUNT(*) FROM (SELECT grupo FROM migra.persona_origen GROUP BY grupo HAVING COUNT(DISTINCT origen) > 1) x) AS dni_compartidos_A_B,
       (SELECT COUNT(DISTINCT grupo) FROM migra.conflicto_dni) AS grupos_en_conflicto;
GO
