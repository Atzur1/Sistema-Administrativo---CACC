/* =====================================================================================
   HU-053 - Paso 4: validacion de la importacion. Solo lee (escribe el resultado en migra.validacion).
   Variables sqlcmd: BaseA, BaseB. Se puede repetir.

   Para no validar el script con el propio script, los ESPERADOS se recalculan desde las bases de origen (no desde
   migra.persona_plan) y cada FK traducida se compara contra el origen fila por fila. Termina con error si algo es FAIL.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

TRUNCATE TABLE migra.validacion;

IF NOT EXISTS (SELECT 1 FROM migra.ejecucion WHERE estado = 'OK')
    INSERT migra.validacion (grupo, verificacion, esperado, obtenido, resultado, detalle)
    VALUES ('EJECUCION', N'Hay una importacion terminada OK', 1, 0, 'FAIL', N'No hay importacion OK en esta base.');

/* ------------------------------- Claves de identidad desde el origen ------------------------------- */
CREATE TABLE #clave (origen char(1) COLLATE DATABASE_DEFAULT NOT NULL, id int NOT NULL, clave varchar(40) COLLATE DATABASE_DEFAULT NOT NULL, PRIMARY KEY (origen, id));
INSERT #clave (origen, id, clave)
SELECT 'A', p.PK_id_persona, ISNULL(v.dni, CONCAT('SIN_DNI:A:', p.PK_id_persona))
FROM [$(BaseA)].dbo.PERSONA p
CROSS APPLY (SELECT REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(p.Dni COLLATE DATABASE_DEFAULT)), '.', ''), ' ', ''), '-', '') AS limpio) l
CROSS APPLY (SELECT CASE WHEN LEN(l.limpio) BETWEEN 6 AND 9 AND l.limpio NOT LIKE '%[^0-9]%' THEN l.limpio END AS dni) v
UNION ALL
SELECT 'B', p.PK_id_persona, ISNULL(v.dni, CONCAT('SIN_DNI:B:', p.PK_id_persona))
FROM [$(BaseB)].dbo.PERSONAS p
CROSS APPLY (SELECT REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(p.dni COLLATE DATABASE_DEFAULT)), '.', ''), ' ', ''), '-', '') AS limpio) l
CROSS APPLY (SELECT CASE WHEN LEN(l.limpio) BETWEEN 6 AND 9 AND l.limpio NOT LIKE '%[^0-9]%' THEN l.limpio END AS dni) v;

/* ------------------------------- Conteos ------------------------------- */
CREATE TABLE #conteo (entidad sysname COLLATE DATABASE_DEFAULT NOT NULL, esperado bigint NOT NULL, obtenido bigint NOT NULL, detalle nvarchar(400) NULL);

INSERT #conteo (entidad, esperado, obtenido, detalle)
SELECT N'PERSONAS', (SELECT COUNT(DISTINCT clave) FROM #clave), (SELECT COUNT(*) FROM dbo.PERSONAS),
       CONCAT(N'A ', (SELECT COUNT(*) FROM #clave WHERE origen = 'A'), N' + B ', (SELECT COUNT(*) FROM #clave WHERE origen = 'B'),
              N' - registros con DNI ya presente ', (SELECT COUNT(*) FROM #clave) - (SELECT COUNT(DISTINCT clave) FROM #clave))
UNION ALL
SELECT N'JUGADORES',
       (SELECT COUNT(DISTINCT c.clave) FROM (
            SELECT 'A' AS origen, FK_id_persona AS id FROM [$(BaseA)].dbo.JUGADORES
            UNION ALL SELECT 'B', FK_id_persona FROM [$(BaseB)].dbo.JUGADORES) j
        JOIN #clave c ON c.origen = j.origen AND c.id = j.id),
       (SELECT COUNT(*) FROM dbo.JUGADORES),
       N'una persona unificada = un jugador unificado'
UNION ALL SELECT N'JUGADORES_PERIODOS', (SELECT COUNT(*) FROM dbo.JUGADORES), (SELECT COUNT(*) FROM dbo.JUGADORES_PERIODOS), N'un periodo abierto por jugador'
UNION ALL SELECT N'USUARIOS', (SELECT COUNT(*) FROM [$(BaseA)].dbo.USUARIO) + (SELECT COUNT(*) FROM [$(BaseB)].dbo.USUARIOS), (SELECT COUNT(*) FROM dbo.USUARIOS), NULL
UNION ALL SELECT N'STAFF', (SELECT COUNT(*) FROM [$(BaseA)].dbo.STAFF) + (SELECT COUNT(*) FROM [$(BaseB)].dbo.STAFF), (SELECT COUNT(*) FROM dbo.STAFF), NULL
UNION ALL SELECT N'STAFF_CATEGORIAS', (SELECT COUNT(*) FROM [$(BaseA)].dbo.STAFF_CATEGORIA) + (SELECT COUNT(*) FROM [$(BaseB)].dbo.STAFF_CATEGORIAS), (SELECT COUNT(*) FROM dbo.STAFF_CATEGORIAS), NULL
UNION ALL SELECT N'RESPONSABLES', (SELECT COUNT(*) FROM [$(BaseA)].dbo.RESPONSABLES) + (SELECT COUNT(*) FROM [$(BaseB)].dbo.RESPONSABLES), (SELECT COUNT(*) FROM dbo.RESPONSABLES), NULL
UNION ALL SELECT N'JUGADORES_RESPONSABLES', (SELECT COUNT(*) FROM [$(BaseA)].dbo.Jugadore_Responsable) + (SELECT COUNT(*) FROM [$(BaseB)].dbo.JUGADORES_RESPONSABLES), (SELECT COUNT(*) FROM dbo.JUGADORES_RESPONSABLES), NULL
UNION ALL SELECT N'FICHAS_MEDICAS', (SELECT COUNT(*) FROM [$(BaseA)].dbo.FICHAS_MEDICAS) + (SELECT COUNT(*) FROM [$(BaseB)].dbo.FICHAS_MEDICAS), (SELECT COUNT(*) FROM dbo.FICHAS_MEDICAS), NULL
UNION ALL SELECT N'HISTORIAL_PARTIDOS', (SELECT COUNT(*) FROM [$(BaseB)].dbo.HISTORIAL_PARTIDOS), (SELECT COUNT(*) FROM dbo.HISTORIAL_PARTIDOS), NULL
UNION ALL SELECT N'JUGADORES_PARTIDOS', (SELECT COUNT(*) FROM [$(BaseB)].dbo.JUGADORES_PARTIDOS), (SELECT COUNT(*) FROM dbo.JUGADORES_PARTIDOS), NULL
UNION ALL SELECT N'ASISTENCIAS', (SELECT COUNT(*) FROM [$(BaseA)].dbo.ASISTENCIAS) + (SELECT COUNT(*) FROM [$(BaseB)].dbo.ASISTENCIAS), (SELECT COUNT(*) FROM dbo.ASISTENCIAS), NULL
UNION ALL SELECT N'JUGADORES_DESCUENTOS', (SELECT COUNT(*) FROM [$(BaseA)].dbo.JUGADORES_DESCUENTOS) + (SELECT COUNT(*) FROM [$(BaseB)].dbo.JUGADORES_DESCUENTOS), (SELECT COUNT(*) FROM dbo.JUGADORES_DESCUENTOS), NULL
UNION ALL SELECT N'PAGOS', (SELECT COUNT(*) FROM [$(BaseA)].dbo.PAGOS) + (SELECT COUNT(*) FROM [$(BaseB)].dbo.PAGOS), (SELECT COUNT(*) FROM dbo.PAGOS), NULL
UNION ALL SELECT N'PAGOS: suma de monto_final (centavos)',
       CONVERT(bigint, 100 * (ISNULL((SELECT SUM(monto_final) FROM [$(BaseA)].dbo.PAGOS), 0) + ISNULL((SELECT SUM(monto_final) FROM [$(BaseB)].dbo.PAGOS), 0))),
       CONVERT(bigint, 100 * ISNULL((SELECT SUM(monto_final) FROM dbo.PAGOS), 0)), NULL
UNION ALL SELECT N'PAGOS: suma de monto_base (centavos)',
       CONVERT(bigint, 100 * (ISNULL((SELECT SUM(monto_base) FROM [$(BaseA)].dbo.PAGOS), 0) + ISNULL((SELECT SUM(monto_base) FROM [$(BaseB)].dbo.PAGOS), 0))),
       CONVERT(bigint, 100 * ISNULL((SELECT SUM(monto_base) FROM dbo.PAGOS), 0)), NULL
UNION ALL SELECT N'AUDITORIA_CAMBIOS (la carga no genera eventos)', (SELECT COUNT(*) FROM [$(BaseA)].dbo.AUDITORIA_CAMBIOS), (SELECT COUNT(*) FROM dbo.AUDITORIA_CAMBIOS), NULL
UNION ALL SELECT N'CATEGORIAS (catalogo, sin altas)', 13, (SELECT COUNT(*) FROM dbo.CATEGORIAS), NULL
UNION ALL SELECT N'ROLES (catalogo, sin altas)', 7, (SELECT COUNT(*) FROM dbo.ROLES), NULL;

INSERT migra.validacion (grupo, verificacion, esperado, obtenido, resultado, detalle)
SELECT 'CONTEO', entidad, esperado, obtenido, CASE WHEN esperado = obtenido THEN 'PASS' ELSE 'FAIL' END, detalle FROM #conteo;

/* ------------------------------- Ningun registro de origen sin destino ------------------------------- */
INSERT migra.validacion (grupo, verificacion, esperado, obtenido, resultado)
SELECT 'CONSERVADO', CONCAT(c.entidad, N' de ', c.origen, N' con id unificado'), c.cantidad, ISNULL(m.n, 0),
       CASE WHEN c.cantidad = ISNULL(m.n, 0) THEN 'PASS' ELSE 'FAIL' END
FROM migra.conteo c
LEFT JOIN (SELECT entidad, origen, COUNT(*) AS n FROM migra.mapa GROUP BY entidad, origen) m ON m.entidad = c.entidad AND m.origen = c.origen
WHERE c.etapa = 'ORIGEN';

INSERT migra.validacion (grupo, verificacion, esperado, obtenido, resultado)
SELECT 'CONSERVADO', N'Ids de A conservados (A no fusionado mantiene su id)', 0,
       (SELECT COUNT(*) FROM migra.mapa WHERE origen = 'A' AND regla = 'ID_CONSERVADO' AND id_origen <> id_unificado),
       CASE WHEN EXISTS (SELECT 1 FROM migra.mapa WHERE origen = 'A' AND regla = 'ID_CONSERVADO' AND id_origen <> id_unificado) THEN 'FAIL' ELSE 'PASS' END;

/* ------------------------------- Remapeo: cada FK apunta a la identidad correcta ------------------------------- */
CREATE TABLE #fk (verificacion nvarchar(300) COLLATE DATABASE_DEFAULT NOT NULL, errores bigint NOT NULL);

-- La persona unificada tiene el DNI de cada registro de origen que se le asigno; y nunca reune DNI distintos.
INSERT #fk SELECT N'PERSONAS: el DNI de destino es el DNI normalizado del origen', COUNT(*)
FROM migra.mapa m JOIN #clave c ON c.origen = m.origen AND c.id = m.id_origen
JOIN dbo.PERSONAS p ON p.PK_id_persona = m.id_unificado
WHERE m.entidad = N'PERSONAS' AND c.clave NOT LIKE 'SIN_DNI:%' AND (p.dni IS NULL OR p.dni <> c.clave);
INSERT #fk SELECT N'PERSONAS: ninguna persona unificada junta DNI distintos', COUNT(*)
FROM (SELECT m.id_unificado FROM migra.mapa m JOIN #clave c ON c.origen = m.origen AND c.id = m.id_origen
      WHERE m.entidad = N'PERSONAS' GROUP BY m.id_unificado HAVING COUNT(DISTINCT c.clave) > 1) x;

INSERT #fk SELECT N'JUGADORES: persona del jugador = persona traducida del origen', COUNT(*)
FROM (SELECT 'A' AS origen, PK_id_jugador AS id, FK_id_persona AS persona FROM [$(BaseA)].dbo.JUGADORES
      UNION ALL SELECT 'B', PK_id_jugador, FK_id_persona FROM [$(BaseB)].dbo.JUGADORES) o
JOIN migra.mapa mj ON mj.entidad = N'JUGADORES' AND mj.origen = o.origen AND mj.id_origen = o.id
JOIN migra.mapa mp ON mp.entidad = N'PERSONAS' AND mp.origen = o.origen AND mp.id_origen = o.persona
JOIN dbo.JUGADORES d ON d.PK_id_jugador = mj.id_unificado
WHERE ISNULL(d.FK_id_persona, -1) <> mp.id_unificado;

INSERT #fk SELECT N'JUGADORES: categoria del jugador = categoria del origen (salvo descarte anotado)', COUNT(*)
FROM (SELECT 'A' AS origen, PK_id_jugador AS id, FK_id_categoria AS cat FROM [$(BaseA)].dbo.JUGADORES
      UNION ALL SELECT 'B', PK_id_jugador, FK_id_categoria FROM [$(BaseB)].dbo.JUGADORES) o
JOIN migra.mapa mj ON mj.entidad = N'JUGADORES' AND mj.origen = o.origen AND mj.id_origen = o.id
JOIN dbo.JUGADORES d ON d.PK_id_jugador = mj.id_unificado
WHERE d.FK_id_categoria <> o.cat
  AND NOT EXISTS (SELECT 1 FROM migra.transformacion t WHERE t.entidad = N'JUGADORES' AND t.origen = o.origen AND t.id_origen = o.id
                  AND t.columna = N'FK_id_categoria');

INSERT #fk SELECT N'USUARIOS: persona del usuario = persona traducida del origen', COUNT(*)
FROM (SELECT 'A' AS origen, PK_id_usuario AS id, FK_id_persona AS persona FROM [$(BaseA)].dbo.USUARIO
      UNION ALL SELECT 'B', PK_id_usuario, FK_id_persona FROM [$(BaseB)].dbo.USUARIOS) o
JOIN migra.mapa mu ON mu.entidad = N'USUARIOS' AND mu.origen = o.origen AND mu.id_origen = o.id
JOIN dbo.USUARIOS d ON d.PK_id_usuario = mu.id_unificado
LEFT JOIN migra.mapa mp ON mp.entidad = N'PERSONAS' AND mp.origen = o.origen AND mp.id_origen = o.persona
WHERE ISNULL(d.FK_id_persona, -1) <> ISNULL(mp.id_unificado, -1);

INSERT #fk SELECT N'STAFF: usuario del staff = usuario traducido del origen', COUNT(*)
FROM (SELECT 'A' AS origen, PK_id_staff AS id, FK_id_usuario AS usuario FROM [$(BaseA)].dbo.STAFF
      UNION ALL SELECT 'B', PK_id_staff, FK_id_usuario FROM [$(BaseB)].dbo.STAFF) o
JOIN migra.mapa ms ON ms.entidad = N'STAFF' AND ms.origen = o.origen AND ms.id_origen = o.id
JOIN dbo.STAFF d ON d.PK_id_staff = ms.id_unificado
LEFT JOIN migra.mapa mu ON mu.entidad = N'USUARIOS' AND mu.origen = o.origen AND mu.id_origen = o.usuario
WHERE ISNULL(d.FK_id_usuario, -1) <> ISNULL(mu.id_unificado, -1);

INSERT #fk SELECT N'STAFF_CATEGORIAS: staff y categoria traducidos', COUNT(*)
FROM (SELECT 'A' AS origen, PK_id_staff_categoria AS id, FK_id_staff AS staff, FK_id_categoria AS cat FROM [$(BaseA)].dbo.STAFF_CATEGORIA
      UNION ALL SELECT 'B', PK_id_staff_categoria, FK_id_staff, FK_id_categoria FROM [$(BaseB)].dbo.STAFF_CATEGORIAS) o
JOIN migra.mapa m ON m.entidad = N'STAFF_CATEGORIAS' AND m.origen = o.origen AND m.id_origen = o.id
JOIN dbo.STAFF_CATEGORIAS d ON d.PK_id_staff_categoria = m.id_unificado
LEFT JOIN migra.mapa ms ON ms.entidad = N'STAFF' AND ms.origen = o.origen AND ms.id_origen = o.staff
WHERE ISNULL(d.FK_id_staff, -1) <> ISNULL(ms.id_unificado, -1) OR ISNULL(d.FK_id_categoria, -1) <> ISNULL(o.cat, -1);

INSERT #fk SELECT N'RESPONSABLES: persona traducida, email y telefono iguales', COUNT(*)
FROM (SELECT 'A' AS origen, PK_id_responsable AS id, FK_id_persona AS persona, email COLLATE DATABASE_DEFAULT AS email, telefono COLLATE DATABASE_DEFAULT AS telefono FROM [$(BaseA)].dbo.RESPONSABLES
      UNION ALL SELECT 'B', PK_id_responsable, FK_id_persona, email COLLATE DATABASE_DEFAULT, telefono COLLATE DATABASE_DEFAULT FROM [$(BaseB)].dbo.RESPONSABLES) o
JOIN migra.mapa m ON m.entidad = N'RESPONSABLES' AND m.origen = o.origen AND m.id_origen = o.id
JOIN dbo.RESPONSABLES d ON d.PK_id_responsable = m.id_unificado
LEFT JOIN migra.mapa mp ON mp.entidad = N'PERSONAS' AND mp.origen = o.origen AND mp.id_origen = o.persona
WHERE ISNULL(d.FK_id_persona, -1) <> ISNULL(mp.id_unificado, -1)
   OR ISNULL(d.email, N'') <> ISNULL(o.email, N'') OR ISNULL(d.telefono, N'') <> ISNULL(o.telefono, N'');

INSERT #fk SELECT N'JUGADORES_RESPONSABLES: jugador y responsable traducidos', COUNT(*)
FROM (SELECT 'A' AS origen, PK_id_jugador_responsable AS id, FK_id_Jugador AS jugador, FK_id_Responsable AS resp FROM [$(BaseA)].dbo.Jugadore_Responsable
      UNION ALL SELECT 'B', PK_id_jugador_responsable, FK_id_jugador, FK_id_responsable FROM [$(BaseB)].dbo.JUGADORES_RESPONSABLES) o
JOIN migra.mapa m ON m.entidad = N'JUGADORES_RESPONSABLES' AND m.origen = o.origen AND m.id_origen = o.id
JOIN dbo.JUGADORES_RESPONSABLES d ON d.PK_id_jugador_responsable = m.id_unificado
LEFT JOIN migra.mapa mj ON mj.entidad = N'JUGADORES' AND mj.origen = o.origen AND mj.id_origen = o.jugador
LEFT JOIN migra.mapa mr ON mr.entidad = N'RESPONSABLES' AND mr.origen = o.origen AND mr.id_origen = o.resp
WHERE ISNULL(d.FK_id_jugador, -1) <> ISNULL(mj.id_unificado, -1) OR ISNULL(d.FK_id_responsable, -1) <> ISNULL(mr.id_unificado, -1);

INSERT #fk SELECT N'FICHAS_MEDICAS: jugador traducido y datos medicos iguales', COUNT(*)
FROM (SELECT 'A' AS origen, PK_id_ficha_medica AS id, FK_id_jugador AS jugador, obra_social COLLATE DATABASE_DEFAULT AS os, apto_fisico_vencimiento AS apto FROM [$(BaseA)].dbo.FICHAS_MEDICAS
      UNION ALL SELECT 'B', PK_id_ficha_medica, FK_id_jugador, obra_social COLLATE DATABASE_DEFAULT, apto_fisico_vencimiento FROM [$(BaseB)].dbo.FICHAS_MEDICAS) o
JOIN migra.mapa m ON m.entidad = N'FICHAS_MEDICAS' AND m.origen = o.origen AND m.id_origen = o.id
JOIN dbo.FICHAS_MEDICAS d ON d.PK_id_ficha_medica = m.id_unificado
LEFT JOIN migra.mapa mj ON mj.entidad = N'JUGADORES' AND mj.origen = o.origen AND mj.id_origen = o.jugador
WHERE ISNULL(d.FK_id_jugador, -1) <> ISNULL(mj.id_unificado, -1) OR ISNULL(d.obra_social, N'') <> ISNULL(o.os, N'')
   OR ISNULL(d.apto_fisico_vencimiento, '19000101') <> ISNULL(o.apto, '19000101');

INSERT #fk SELECT N'JUGADORES_PARTIDOS: partido y jugador traducidos', COUNT(*)
FROM [$(BaseB)].dbo.JUGADORES_PARTIDOS o
JOIN migra.mapa m ON m.entidad = N'JUGADORES_PARTIDOS' AND m.origen = 'B' AND m.id_origen = o.PK_id_jugador_partido
JOIN dbo.JUGADORES_PARTIDOS d ON d.PK_id_jugador_partido = m.id_unificado
LEFT JOIN migra.mapa mp ON mp.entidad = N'HISTORIAL_PARTIDOS' AND mp.origen = 'B' AND mp.id_origen = o.FK_id_partido
LEFT JOIN migra.mapa mj ON mj.entidad = N'JUGADORES' AND mj.origen = 'B' AND mj.id_origen = o.FK_id_jugador
WHERE ISNULL(d.FK_id_partido, -1) <> ISNULL(mp.id_unificado, -1) OR ISNULL(d.FK_id_jugador, -1) <> ISNULL(mj.id_unificado, -1)
   OR ISNULL(d.goles, -1) <> ISNULL(o.goles, -1);

INSERT #fk SELECT N'HISTORIAL_PARTIDOS: categoria, fecha y rival iguales', COUNT(*)
FROM [$(BaseB)].dbo.HISTORIAL_PARTIDOS o
JOIN migra.mapa m ON m.entidad = N'HISTORIAL_PARTIDOS' AND m.origen = 'B' AND m.id_origen = o.PK_id_partido
JOIN dbo.HISTORIAL_PARTIDOS d ON d.PK_id_partido = m.id_unificado
WHERE ISNULL(d.FK_id_categoria, -1) <> ISNULL(o.FK_id_categoria, -1) OR ISNULL(d.fecha_partido, '19000101') <> ISNULL(o.fecha_partido, '19000101')
   OR ISNULL(d.rival, N'') <> ISNULL(o.rival COLLATE DATABASE_DEFAULT, N'');

INSERT #fk SELECT N'JUGADORES_DESCUENTOS: jugador traducido, tipo por nombre, valor y vigencia iguales', COUNT(*)
FROM [$(BaseA)].dbo.JUGADORES_DESCUENTOS o
JOIN [$(BaseA)].dbo.TIPO_DESCUENTO t ON t.PK_id_descuento = o.FK_id_descuento
JOIN migra.mapa m ON m.entidad = N'JUGADORES_DESCUENTOS' AND m.origen = 'A' AND m.id_origen = o.PK_id_jugador_descuento
JOIN dbo.JUGADORES_DESCUENTOS d ON d.PK_id_jugador_descuento = m.id_unificado
JOIN dbo.TIPOS_DESCUENTO dt ON dt.PK_id_descuento = d.FK_id_descuento
JOIN migra.mapa mj ON mj.entidad = N'JUGADORES' AND mj.origen = 'A' AND mj.id_origen = o.FK_id_jugador
WHERE d.FK_id_jugador <> mj.id_unificado OR dt.tipo_descuento <> t.tipo_descuento COLLATE DATABASE_DEFAULT
   OR d.estado_activo <> o.estado_activo OR ISNULL(d.porcentaje, -1) <> ISNULL(o.porcentaje, -1) OR ISNULL(d.monto_fijo, -1) <> ISNULL(o.monto_fijo, -1)
   OR d.fecha_inicio <> o.fecha_inicio OR d.fecha_fin <> o.fecha_fin OR ISNULL(d.fecha_cancelacion, '19000101') <> ISNULL(o.fecha_cancelacion, '19000101');

INSERT #fk SELECT N'PAGOS: jugador traducido; monto, fechas, estado, concepto y medio iguales', COUNT(*)
FROM (SELECT 'A' AS origen, PK_id_pago AS id, FK_id_jugador AS jugador, monto_base, monto_final, fecha_pago, fecha_vencimiento, estado,
             concepto COLLATE DATABASE_DEFAULT AS concepto, metodo_pago COLLATE DATABASE_DEFAULT AS metodo_pago FROM [$(BaseA)].dbo.PAGOS
      UNION ALL SELECT 'B', PK_id_pago, FK_id_jugador, monto_base, monto_final, fecha_pago, fecha_vencimiento, estado, 'Cuota',
             metodo_pago COLLATE DATABASE_DEFAULT FROM [$(BaseB)].dbo.PAGOS) o
JOIN migra.mapa m ON m.entidad = N'PAGOS' AND m.origen = o.origen AND m.id_origen = o.id
JOIN dbo.PAGOS d ON d.PK_id_pago = m.id_unificado
LEFT JOIN migra.mapa mj ON mj.entidad = N'JUGADORES' AND mj.origen = o.origen AND mj.id_origen = o.jugador
WHERE ISNULL(d.FK_id_jugador, -1) <> ISNULL(mj.id_unificado, -1)
   OR ISNULL(d.monto_base, -1) <> ISNULL(o.monto_base, -1) OR ISNULL(d.monto_final, -1) <> ISNULL(o.monto_final, -1)
   OR ISNULL(d.fecha_pago, '19000101') <> ISNULL(o.fecha_pago, '19000101') OR ISNULL(d.fecha_vencimiento, '19000101') <> ISNULL(o.fecha_vencimiento, '19000101')
   OR ISNULL(d.estado, 0) <> ISNULL(o.estado, 0) OR d.concepto <> o.concepto OR ISNULL(d.metodo_pago, N'') <> ISNULL(o.metodo_pago, N'');

INSERT migra.validacion (grupo, verificacion, esperado, obtenido, resultado)
SELECT 'REMAPEO', verificacion, 0, errores, CASE WHEN errores = 0 THEN 'PASS' ELSE 'FAIL' END FROM #fk;

/* ------------------------------- Huerfanos ------------------------------- */
-- "nuevos" = en destino y no en origen. Lo que ya venia roto se informa aparte (INFO), sin atribuirlo a la migracion.
CREATE TABLE #huerfano (verificacion nvarchar(300) COLLATE DATABASE_DEFAULT NOT NULL, destino bigint NOT NULL, origen bigint NOT NULL);
INSERT #huerfano VALUES
 (N'jugadores sin persona',
  (SELECT COUNT(*) FROM dbo.JUGADORES j WHERE NOT EXISTS (SELECT 1 FROM dbo.PERSONAS p WHERE p.PK_id_persona = j.FK_id_persona)),
  (SELECT COUNT(*) FROM [$(BaseA)].dbo.JUGADORES WHERE FK_id_persona IS NULL) + (SELECT COUNT(*) FROM [$(BaseB)].dbo.JUGADORES WHERE FK_id_persona IS NULL)),
 (N'usuarios sin persona',
  (SELECT COUNT(*) FROM dbo.USUARIOS WHERE FK_id_persona IS NULL),
  (SELECT COUNT(*) FROM [$(BaseA)].dbo.USUARIO WHERE FK_id_persona IS NULL) + (SELECT COUNT(*) FROM [$(BaseB)].dbo.USUARIOS WHERE FK_id_persona IS NULL)),
 (N'staff sin usuario',
  (SELECT COUNT(*) FROM dbo.STAFF WHERE FK_id_usuario IS NULL),
  (SELECT COUNT(*) FROM [$(BaseA)].dbo.STAFF WHERE FK_id_usuario IS NULL) + (SELECT COUNT(*) FROM [$(BaseB)].dbo.STAFF WHERE FK_id_usuario IS NULL)),
 (N'staff_categorias sin staff o sin categoria',
  (SELECT COUNT(*) FROM dbo.STAFF_CATEGORIAS WHERE FK_id_staff IS NULL OR FK_id_categoria IS NULL),
  (SELECT COUNT(*) FROM [$(BaseA)].dbo.STAFF_CATEGORIA WHERE FK_id_staff IS NULL OR FK_id_categoria IS NULL)
  + (SELECT COUNT(*) FROM [$(BaseB)].dbo.STAFF_CATEGORIAS WHERE FK_id_staff IS NULL OR FK_id_categoria IS NULL)),
 (N'pagos sin jugador',
  (SELECT COUNT(*) FROM dbo.PAGOS WHERE FK_id_jugador IS NULL),
  (SELECT COUNT(*) FROM [$(BaseA)].dbo.PAGOS WHERE FK_id_jugador IS NULL) + (SELECT COUNT(*) FROM [$(BaseB)].dbo.PAGOS WHERE FK_id_jugador IS NULL)),
 (N'asistencias sin jugador ni staff',
  (SELECT COUNT(*) FROM dbo.ASISTENCIAS WHERE FK_id_jugador IS NULL AND FK_id_staff IS NULL),
  (SELECT COUNT(*) FROM [$(BaseA)].dbo.ASISTENCIAS WHERE FK_id_jugador IS NULL AND FK_id_staff IS NULL)
  + (SELECT COUNT(*) FROM [$(BaseB)].dbo.ASISTENCIAS WHERE FK_id_jugador IS NULL AND FK_id_staff IS NULL)),
 (N'jugador-responsable con un extremo vacio',
  (SELECT COUNT(*) FROM dbo.JUGADORES_RESPONSABLES WHERE FK_id_jugador IS NULL OR FK_id_responsable IS NULL),
  (SELECT COUNT(*) FROM [$(BaseA)].dbo.Jugadore_Responsable WHERE FK_id_Jugador IS NULL OR FK_id_Responsable IS NULL)
  + (SELECT COUNT(*) FROM [$(BaseB)].dbo.JUGADORES_RESPONSABLES WHERE FK_id_jugador IS NULL OR FK_id_responsable IS NULL)),
 (N'fichas medicas sin jugador',
  (SELECT COUNT(*) FROM dbo.FICHAS_MEDICAS WHERE FK_id_jugador IS NULL),
  (SELECT COUNT(*) FROM [$(BaseA)].dbo.FICHAS_MEDICAS WHERE FK_id_jugador IS NULL) + (SELECT COUNT(*) FROM [$(BaseB)].dbo.FICHAS_MEDICAS WHERE FK_id_jugador IS NULL)),
 (N'jugadores_partidos con un extremo vacio',
  (SELECT COUNT(*) FROM dbo.JUGADORES_PARTIDOS WHERE FK_id_jugador IS NULL OR FK_id_partido IS NULL),
  (SELECT COUNT(*) FROM [$(BaseB)].dbo.JUGADORES_PARTIDOS WHERE FK_id_jugador IS NULL OR FK_id_partido IS NULL)),
 (N'responsables sin persona',
  (SELECT COUNT(*) FROM dbo.RESPONSABLES WHERE FK_id_persona IS NULL),
  (SELECT COUNT(*) FROM [$(BaseA)].dbo.RESPONSABLES WHERE FK_id_persona IS NULL) + (SELECT COUNT(*) FROM [$(BaseB)].dbo.RESPONSABLES WHERE FK_id_persona IS NULL)),
 (N'responsables sin ningun jugador vinculado',
  (SELECT COUNT(*) FROM dbo.RESPONSABLES r WHERE NOT EXISTS (SELECT 1 FROM dbo.JUGADORES_RESPONSABLES jr WHERE jr.FK_id_responsable = r.PK_id_responsable)),
  (SELECT COUNT(*) FROM [$(BaseA)].dbo.RESPONSABLES r WHERE NOT EXISTS (SELECT 1 FROM [$(BaseA)].dbo.Jugadore_Responsable jr WHERE jr.FK_id_Responsable = r.PK_id_responsable))
  + (SELECT COUNT(*) FROM [$(BaseB)].dbo.RESPONSABLES r WHERE NOT EXISTS (SELECT 1 FROM [$(BaseB)].dbo.JUGADORES_RESPONSABLES jr WHERE jr.FK_id_responsable = r.PK_id_responsable)));

INSERT migra.validacion (grupo, verificacion, esperado, obtenido, resultado, detalle)
SELECT 'HUERFANOS', CONCAT(N'Nuevos huerfanos: ', verificacion), 0, destino - origen,
       CASE WHEN destino - origen <= 0 THEN 'PASS' ELSE 'FAIL' END,
       CONCAT(N'destino ', destino, N', ya existian en origen ', origen)
FROM #huerfano;
INSERT migra.validacion (grupo, verificacion, esperado, obtenido, resultado, detalle)
SELECT 'PREEXISTENTE', CONCAT(N'Inconsistencia que ya traia el origen: ', verificacion), NULL, origen, 'INFO', N'No la produjo la migracion.'
FROM #huerfano WHERE origen > 0;

/* ------------------------------- Integridad y duplicados ------------------------------- */
CREATE TABLE #dbcc (tabla nvarchar(300) NULL, restriccion nvarchar(300) NULL, condicion nvarchar(4000) NULL);
INSERT #dbcc EXEC (N'DBCC CHECKCONSTRAINTS WITH ALL_CONSTRAINTS, NO_INFOMSGS');

INSERT migra.validacion (grupo, verificacion, esperado, obtenido, resultado, detalle)
VALUES
 ('INTEGRIDAD', N'Filas que violan una FK o CHECK (DBCC CHECKCONSTRAINTS)', 0, (SELECT COUNT(*) FROM #dbcc),
  CASE WHEN EXISTS (SELECT 1 FROM #dbcc) THEN 'FAIL' ELSE 'PASS' END, (SELECT STRING_AGG(CONVERT(nvarchar(max), restriccion), N', ') FROM #dbcc)),
 ('INTEGRIDAD', N'FK deshabilitadas o no confiables', 0, (SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled = 1 OR is_not_trusted = 1),
  CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys WHERE is_disabled = 1 OR is_not_trusted = 1) THEN 'FAIL' ELSE 'PASS' END, NULL),
 ('INTEGRIDAD', N'CHECK deshabilitados o no confiables', 0, (SELECT COUNT(*) FROM sys.check_constraints WHERE is_disabled = 1 OR is_not_trusted = 1),
  CASE WHEN EXISTS (SELECT 1 FROM sys.check_constraints WHERE is_disabled = 1 OR is_not_trusted = 1) THEN 'FAIL' ELSE 'PASS' END, NULL),
 ('DUPLICADOS', N'Personas con el mismo DNI', 0, (SELECT COUNT(*) FROM (SELECT dni FROM dbo.PERSONAS WHERE dni IS NOT NULL GROUP BY dni HAVING COUNT(*) > 1) x),
  CASE WHEN EXISTS (SELECT dni FROM dbo.PERSONAS WHERE dni IS NOT NULL GROUP BY dni HAVING COUNT(*) > 1) THEN 'FAIL' ELSE 'PASS' END, NULL),
 ('DUPLICADOS', N'Personas con mas de un jugador', 0, (SELECT COUNT(*) FROM (SELECT FK_id_persona FROM dbo.JUGADORES GROUP BY FK_id_persona HAVING COUNT(*) > 1) x),
  CASE WHEN EXISTS (SELECT FK_id_persona FROM dbo.JUGADORES GROUP BY FK_id_persona HAVING COUNT(*) > 1) THEN 'FAIL' ELSE 'PASS' END, NULL),
 ('DUPLICADOS', N'Personas con mas de un usuario', 0, (SELECT COUNT(*) FROM (SELECT FK_id_persona FROM dbo.USUARIOS WHERE FK_id_persona IS NOT NULL GROUP BY FK_id_persona HAVING COUNT(*) > 1) x),
  CASE WHEN EXISTS (SELECT FK_id_persona FROM dbo.USUARIOS WHERE FK_id_persona IS NOT NULL GROUP BY FK_id_persona HAVING COUNT(*) > 1) THEN 'FAIL' ELSE 'PASS' END, NULL),
 ('REGLAS', N'Jugadores cuya persona no tiene genero Masculino/Femenino', 0,
  (SELECT COUNT(*) FROM dbo.JUGADORES j JOIN dbo.PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.genero IS NULL OR p.genero NOT IN ('Masculino', 'Femenino')),
  CASE WHEN EXISTS (SELECT 1 FROM dbo.JUGADORES j JOIN dbo.PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.genero IS NULL OR p.genero NOT IN ('Masculino', 'Femenino')) THEN 'FAIL' ELSE 'PASS' END, NULL),
 ('REGLAS', N'Usuarios de B con acceso al Portal Administrativo (deben nacer sin acceso)', 0,
  (SELECT COUNT(*) FROM migra.mapa m JOIN dbo.USUARIOS u ON u.PK_id_usuario = m.id_unificado WHERE m.entidad = N'USUARIOS' AND m.origen = 'B' AND (u.acceso_portal = 1 OR u.rol_portal IS NOT NULL)),
  CASE WHEN EXISTS (SELECT 1 FROM migra.mapa m JOIN dbo.USUARIOS u ON u.PK_id_usuario = m.id_unificado WHERE m.entidad = N'USUARIOS' AND m.origen = 'B' AND (u.acceso_portal = 1 OR u.rol_portal IS NOT NULL)) THEN 'FAIL' ELSE 'PASS' END, NULL),
 ('REGLAS', N'Usuarios con acceso al Portal Administrativo = los que ya lo tenian en A',
  (SELECT COUNT(*) FROM [$(BaseA)].dbo.USUARIO WHERE acceso_portal = 1), (SELECT COUNT(*) FROM dbo.USUARIOS WHERE acceso_portal = 1),
  CASE WHEN (SELECT COUNT(*) FROM [$(BaseA)].dbo.USUARIO WHERE acceso_portal = 1) = (SELECT COUNT(*) FROM dbo.USUARIOS WHERE acceso_portal = 1) THEN 'PASS' ELSE 'FAIL' END, NULL);

/* ------------------------------- Triggers, secuencias e identidades ------------------------------- */
-- Los 14 triggers que define esquema/ClubCamioneros_unificada_crear_base.sql, todos habilitados.
DECLARE @triggersEsperados TABLE (nombre sysname PRIMARY KEY);
INSERT @triggersEsperados (nombre) VALUES
    (N'TR_AUDIT_ARANCELES'), (N'TR_AUDIT_ARANCELES_INSCRIPCION'), (N'TR_AUDITORIA_IDENTIDAD_ACTOR'), (N'TR_AUDIT_CATEGORIAS'),
    (N'TR_AUDIT_JUGADORES'), (N'TR_JUGADORES_PERIODOS'), (N'TR_JUGADORES_GENERO_OBLIGATORIO'), (N'TR_PERSONAS_GENERO_DE_JUGADOR'),
    (N'TR_AUDIT_JUGADORES_DESCUENTOS'), (N'TR_AUDIT_PAGOS'), (N'TR_AUDIT_PERSONAS'), (N'TR_AUDIT_TIPOS_DESCUENTO'),
    (N'TR_AUDIT_TOKEN_ACCESO_CUENTA'), (N'TR_AUDIT_USUARIOS');
DECLARE @triggersActivos int = (SELECT COUNT(*) FROM sys.triggers t JOIN @triggersEsperados e ON e.nombre = t.name WHERE t.is_disabled = 0);
INSERT migra.validacion (grupo, verificacion, esperado, obtenido, resultado, detalle)
VALUES ('TRIGGERS', N'Triggers del esquema unificado presentes y habilitados', (SELECT COUNT(*) FROM @triggersEsperados), @triggersActivos,
        CASE WHEN @triggersActivos = (SELECT COUNT(*) FROM @triggersEsperados) AND NOT EXISTS (SELECT 1 FROM sys.triggers WHERE is_disabled = 1)
             THEN 'PASS' ELSE 'FAIL' END,
        (SELECT STRING_AGG(CONVERT(nvarchar(max), CONCAT(name, CASE WHEN is_disabled = 1 THEN N' (DESHABILITADO)' END)), N', ') FROM sys.triggers WHERE parent_class = 1));

INSERT migra.validacion (grupo, verificacion, esperado, obtenido, resultado, detalle)
SELECT 'IDS', CONCAT(N'Proximo valor de ', s.name, N' mayor que el mayor id presente'), t.maximo + 1, x.proximo,
       CASE WHEN x.proximo > t.maximo THEN 'PASS' ELSE 'FAIL' END, NULL
FROM sys.sequences s
CROSS APPLY (SELECT CASE WHEN s.last_used_value IS NULL THEN CONVERT(bigint, s.start_value) ELSE CONVERT(bigint, s.last_used_value) + CONVERT(bigint, s.increment) END AS proximo) x
CROSS APPLY (SELECT CASE s.name WHEN N'SEQ_PAGOS' THEN (SELECT ISNULL(MAX(PK_id_pago), 0) FROM dbo.PAGOS)
                                WHEN N'SEQ_JUGADORES' THEN (SELECT ISNULL(MAX(PK_id_jugador), 0) FROM dbo.JUGADORES) END AS maximo) t
WHERE s.name IN (N'SEQ_PAGOS', N'SEQ_JUGADORES');

INSERT migra.validacion (grupo, verificacion, esperado, obtenido, resultado, detalle)
SELECT 'IDS', CONCAT(N'IDENTITY de ', t.name, N' por encima del mayor id presente'), m.maximo, CONVERT(bigint, IDENT_CURRENT(N'dbo.' + t.name)),
       CASE WHEN CONVERT(bigint, IDENT_CURRENT(N'dbo.' + t.name)) >= m.maximo THEN 'PASS' ELSE 'FAIL' END, NULL
FROM sys.tables t
JOIN sys.identity_columns ic ON ic.object_id = t.object_id
CROSS APPLY (SELECT CASE t.name
    WHEN N'PERSONAS' THEN (SELECT ISNULL(MAX(PK_id_persona), 0) FROM dbo.PERSONAS)
    WHEN N'USUARIOS' THEN (SELECT ISNULL(MAX(PK_id_usuario), 0) FROM dbo.USUARIOS)
    WHEN N'STAFF' THEN (SELECT ISNULL(MAX(PK_id_staff), 0) FROM dbo.STAFF)
    WHEN N'STAFF_CATEGORIAS' THEN (SELECT ISNULL(MAX(PK_id_staff_categoria), 0) FROM dbo.STAFF_CATEGORIAS)
    WHEN N'RESPONSABLES' THEN (SELECT ISNULL(MAX(PK_id_responsable), 0) FROM dbo.RESPONSABLES)
    WHEN N'JUGADORES_RESPONSABLES' THEN (SELECT ISNULL(MAX(PK_id_jugador_responsable), 0) FROM dbo.JUGADORES_RESPONSABLES)
    WHEN N'CATEGORIAS' THEN (SELECT ISNULL(MAX(PK_id_categoria), 0) FROM dbo.CATEGORIAS)
    WHEN N'JUGADORES_DESCUENTOS' THEN (SELECT ISNULL(MAX(PK_id_jugador_descuento), 0) FROM dbo.JUGADORES_DESCUENTOS)
    WHEN N'AUDITORIA_CAMBIOS' THEN (SELECT ISNULL(MAX(PK_id_evento), 0) FROM dbo.AUDITORIA_CAMBIOS)
    WHEN N'FICHAS_MEDICAS' THEN (SELECT ISNULL(MAX(PK_id_ficha_medica), 0) FROM dbo.FICHAS_MEDICAS)
    WHEN N'ASISTENCIAS' THEN (SELECT ISNULL(MAX(PK_id_asistencia), 0) FROM dbo.ASISTENCIAS) END AS maximo) m
WHERE t.schema_id = SCHEMA_ID(N'dbo') AND m.maximo IS NOT NULL AND m.maximo > 0;

/* ------------------------------- Tablas de origen no contempladas ------------------------------- */
INSERT migra.validacion (grupo, verificacion, esperado, obtenido, resultado, detalle)
SELECT 'COBERTURA', CONCAT(N'Tabla de origen con datos sin traduccion: ', x.base, N'.', x.tabla), 0, x.filas, 'FAIL', NULL
FROM (
    SELECT N'A' AS base, t.name COLLATE DATABASE_DEFAULT AS tabla, SUM(p.rows) AS filas
    FROM [$(BaseA)].sys.tables t JOIN [$(BaseA)].sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
    GROUP BY t.name
    UNION ALL
    SELECT N'B', t.name COLLATE DATABASE_DEFAULT, SUM(p.rows)
    FROM [$(BaseB)].sys.tables t JOIN [$(BaseB)].sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
    GROUP BY t.name) x
WHERE x.filas > 0
  AND NOT (x.base = N'A' AND x.tabla IN (N'PERSONA', N'USUARIO', N'ROLES', N'JUGADORES', N'RESPONSABLES', N'Jugadore_Responsable', N'STAFF',
          N'STAFF_CATEGORIA', N'FICHAS_MEDICAS', N'CATEGORIAS', N'ASISTENCIAS', N'TIPO_DESCUENTO', N'JUGADORES_DESCUENTOS', N'PAGOS',
          N'AUDITORIA_CAMBIOS', N'__CaccMigraciones'))
  AND NOT (x.base = N'B' AND x.tabla IN (N'PERSONAS', N'USUARIOS', N'ROLES', N'JUGADORES', N'RESPONSABLES', N'JUGADORES_RESPONSABLES', N'STAFF',
          N'STAFF_CATEGORIAS', N'FICHAS_MEDICAS', N'CATEGORIAS', N'ASISTENCIAS', N'TIPOS_DESCUENTO', N'JUGADORES_DESCUENTOS', N'PAGOS',
          N'HISTORIAL_PARTIDOS', N'JUGADORES_PARTIDOS'));

INSERT migra.validacion (grupo, verificacion, esperado, obtenido, resultado, detalle)
VALUES ('COBERTURA', N'Tablas de origen revisadas sin datos pendientes de traduccion', NULL, NULL, 'INFO',
        N'__CaccMigraciones de A no se copia: la base unificada tiene su propio historial de migraciones.');

/* ------------------------------- Resumen de decisiones aplicadas ------------------------------- */
INSERT migra.validacion (grupo, verificacion, esperado, obtenido, resultado, detalle)
SELECT 'DECISIONES', CONCAT(N'Transformacion ', regla, N' (', entidad, N'.', columna, N')'), NULL, COUNT(*), 'INFO', NULL
FROM migra.transformacion GROUP BY regla, entidad, columna;
INSERT migra.validacion (grupo, verificacion, esperado, obtenido, resultado, detalle)
SELECT 'DECISIONES', N'Grupos de DNI resueltos por decision humana', NULL, COUNT(DISTINCT grupo), 'INFO',
       CASE WHEN EXISTS (SELECT 1 FROM migra.resolucion_dni WHERE es_prueba = 1)
            THEN N'ATENCION: hay resoluciones marcadas es_prueba = 1 (no son decisiones del club).' END
FROM migra.resolucion_dni;

SELECT grupo, verificacion, esperado, obtenido, resultado, detalle FROM migra.validacion ORDER BY PK_id;

/* ------------------------------- Muestras ------------------------------- */
PRINT N'--- Muestra: persona solo de A, persona solo de B, misma persona en A y B, conflicto resuelto ---';
;WITH candidato AS (
    SELECT CASE WHEN pp.regla = 'UNICA' AND m.origen = 'A'
                 AND EXISTS (SELECT 1 FROM dbo.JUGADORES j JOIN dbo.PAGOS pg ON pg.FK_id_jugador = j.PK_id_jugador WHERE j.FK_id_persona = m.id_unificado)
                THEN N'solo A (con pagos)'
                WHEN pp.regla = 'UNICA' AND m.origen = 'B'
                 AND EXISTS (SELECT 1 FROM dbo.USUARIOS u JOIN dbo.STAFF s ON s.FK_id_usuario = u.PK_id_usuario WHERE u.FK_id_persona = m.id_unificado)
                THEN N'solo B (usuario y staff)'
                WHEN pp.regla = 'MISMO_DNI_ENTRE_SISTEMAS' AND m.origen = 'B' AND pp.es_canonica = 0
                THEN N'misma persona en A y B'
                WHEN pp.regla = 'RESOLUCION_HUMANA' THEN N'conflicto resuelto' END AS caso,
           m.id_unificado, m.origen, m.id_origen
    FROM migra.mapa m
    JOIN migra.persona_plan pp ON pp.origen = m.origen AND pp.id_origen = m.id_origen
    WHERE m.entidad = N'PERSONAS'),
muestra AS (
    SELECT caso, id_unificado
    FROM (SELECT caso, id_unificado, ROW_NUMBER() OVER (PARTITION BY caso ORDER BY origen, id_origen) AS n FROM candidato WHERE caso IS NOT NULL) x
    WHERE n = 1)
SELECT mu.caso, p.PK_id_persona, p.dni, p.apellido, p.nombre, p.genero, p.fecha_de_nacimiento,
       (SELECT STRING_AGG(CONCAT(m.origen, N':', m.id_origen, N' ', m.regla), N', ') FROM migra.mapa m
        WHERE m.entidad = N'PERSONAS' AND m.id_unificado = p.PK_id_persona) AS registros_de_origen,
       j.PK_id_jugador, c.nombre_categoria,
       (SELECT COUNT(*) FROM dbo.PAGOS pg WHERE pg.FK_id_jugador = j.PK_id_jugador) AS pagos,
       (SELECT COUNT(*) FROM dbo.FICHAS_MEDICAS f WHERE f.FK_id_jugador = j.PK_id_jugador) AS fichas,
       u.PK_id_usuario, r.nombre_rol, u.acceso_portal, s.PK_id_staff
FROM muestra mu
JOIN dbo.PERSONAS p ON p.PK_id_persona = mu.id_unificado
LEFT JOIN dbo.JUGADORES j ON j.FK_id_persona = p.PK_id_persona
LEFT JOIN dbo.CATEGORIAS c ON c.PK_id_categoria = j.FK_id_categoria
LEFT JOIN dbo.USUARIOS u ON u.FK_id_persona = p.PK_id_persona
LEFT JOIN dbo.ROLES r ON r.PK_id_rol = u.FK_id_rol
LEFT JOIN dbo.STAFF s ON s.FK_id_usuario = u.PK_id_usuario;

IF EXISTS (SELECT 1 FROM migra.validacion WHERE resultado = 'FAIL')
BEGIN
    DECLARE @fallas nvarchar(2000) = (SELECT STRING_AGG(CONVERT(nvarchar(max), verificacion), N'; ') FROM migra.validacion WHERE resultado = 'FAIL');
    DECLARE @mensaje nvarchar(2048) = LEFT(N'Validacion con fallas: ' + @fallas, 2048);
    THROW 53200, @mensaje, 1;
END;
PRINT N'Validacion: todas las verificaciones PASS.';
GO
