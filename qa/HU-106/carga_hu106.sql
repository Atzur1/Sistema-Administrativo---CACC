-- HU-106, QA: volumen de club sobre la base descartable ClubCamionerosUnificadaV2_HU106_Prueba. Datos sintéticos (DNI 97xxxxxx).
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Aranceles: los de género y dos categorías con arancel propio (HU-048).
INSERT ARANCELES (genero, FK_id_categoria, monto, vigente_desde) VALUES ('Masculino', NULL, 85000, '2026-02-01'), ('Femenino', NULL, 60000, '2026-01-01');
INSERT ARANCELES (genero, FK_id_categoria, monto, vigente_desde)
SELECT NULL, PK_id_categoria, 95000, '2026-03-01' FROM (SELECT TOP (2) PK_id_categoria FROM CATEGORIAS ORDER BY PK_id_categoria) c;

-- 3.000 jugadores en las 13 categorías: 70 % varones, 30 % mujeres.
WITH n AS (SELECT TOP (3000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_objects a CROSS JOIN sys.all_objects b)
INSERT PERSONAS (nombre, apellido, dni, genero, fecha_de_nacimiento)
SELECT N'Carga', CONCAT(N'HU106 ', i), CONCAT('97', RIGHT(CONCAT('000000', i), 6)),
       CASE WHEN i % 10 < 7 THEN 'Masculino' ELSE 'Femenino' END, DATEADD(DAY, -(i % 3650), '2016-01-01')
FROM n;

DECLARE @categorias TABLE (orden INT PRIMARY KEY, id INT);
INSERT @categorias SELECT ROW_NUMBER() OVER (ORDER BY PK_id_categoria) - 1, PK_id_categoria FROM CATEGORIAS;
DECLARE @cantidad INT = (SELECT COUNT(*) FROM @categorias);

INSERT JUGADORES (FK_id_persona, FK_id_categoria, fecha_alta)
SELECT p.PK_id_persona, c.id, '2026-01-01'
FROM PERSONAS p
JOIN @categorias c ON c.orden = CONVERT(INT, RIGHT(p.dni, 6)) % @cantidad
WHERE p.dni LIKE '97______';

-- 5 % se fue en junio (mismo camino que una baja: UPDATE de fecha_baja, el trigger cierra el período).
UPDATE j SET fecha_baja = '2026-06-30'
FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona
WHERE p.dni LIKE '97______' AND CONVERT(INT, RIGHT(p.dni, 6)) % 20 = 0;

-- Cuotas de enero a octubre con la misma jerarquía y elegibilidad que el generador (SqlCuotaMensual): de categoría si
-- tiene, si no de género; solo los meses en que el jugador estuvo en el club. Hasta septiembre, 90 % pagadas; octubre
-- (mes en curso), 40 % pagadas y el resto pendiente.
WITH meses AS (SELECT DATEFROMPARTS(2026, m, 1) AS mes, m FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10)) v(m))
INSERT PAGOS (FK_id_jugador, monto_base, monto_final, fecha_vencimiento, estado, concepto, fecha_pago, metodo_pago)
SELECT j.PK_id_jugador, arancel.monto, arancel.monto, ms.mes,
       CASE WHEN (j.PK_id_jugador + ms.m) % 10 < CASE WHEN ms.m = 10 THEN 4 ELSE 9 END THEN 1 ELSE 0 END, 'Cuota',
       CASE WHEN (j.PK_id_jugador + ms.m) % 10 < CASE WHEN ms.m = 10 THEN 4 ELSE 9 END THEN DATEADD(DAY, 12, ms.mes) END,
       CASE WHEN (j.PK_id_jugador + ms.m) % 10 < CASE WHEN ms.m = 10 THEN 4 ELSE 9 END THEN 'Efectivo' END
FROM JUGADORES j
JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona
CROSS JOIN meses ms
CROSS APPLY (
    SELECT TOP (1) a.monto FROM ARANCELES a
    WHERE a.vigente_desde <= EOMONTH(ms.mes)
      AND (a.FK_id_categoria = j.FK_id_categoria OR (a.FK_id_categoria IS NULL AND a.genero = LTRIM(RTRIM(p.genero))))
    ORDER BY CASE WHEN a.FK_id_categoria IS NULL THEN 1 ELSE 0 END, a.vigente_desde DESC) AS arancel
WHERE p.dni LIKE '97______'
  AND (j.fecha_alta IS NULL OR j.fecha_alta <= EOMONTH(ms.mes))
  AND (j.fecha_baja IS NULL OR j.fecha_baja >= ms.mes);

-- Una categoría sin jugadores, para el caso "0 afectados".
INSERT CATEGORIAS (nombre_categoria) VALUES ('HU106 sin jugadores');

COMMIT;

SELECT CONCAT('jugadores=', (SELECT COUNT(*) FROM JUGADORES), ' activos=', (SELECT COUNT(*) FROM JUGADORES WHERE fecha_baja IS NULL),
              ' pagos=', (SELECT COUNT(*) FROM PAGOS), ' aranceles=', (SELECT COUNT(*) FROM ARANCELES),
              ' eventos=', (SELECT COUNT(*) FROM AUDITORIA_CAMBIOS));
