-- HU-107, QA: cobros sintéticos sobre la base descartable ClubCamionerosUnificadaV2_HU107_Prueba (DNI 96xxxxxx).
--   Septiembre 2026: 126 cobros (más de 100). Varios por día con distintas horas, dos con la misma hora exacta,
--                    6 sin hora de registro (pagos anteriores a esa columna), montos que como texto se ordenan mal
--                    ($9.000 / $20.000 / $100.000), dos categorías y los dos medios de pago. Además 30 cuotas
--                    pendientes (estado 0) que no son cobros.
--   Octubre 2026:    7 cobros (mes chico), uno el 01/10 a las 00:01.
--   Agosto 2026:     sin cobros (mes vacío). El 31/07 23:59 queda en julio, no en agosto.
--   Julio 2026:      5.000 cobros, para medir tiempos con volumen.
-- Las horas ya están en hora del club, igual que las graba SqlReloj.Ahora.
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @catA INT = (SELECT PK_id_categoria FROM CATEGORIAS ORDER BY PK_id_categoria OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY);
DECLARE @catB INT = (SELECT PK_id_categoria FROM CATEGORIAS ORDER BY PK_id_categoria OFFSET 1 ROWS FETCH NEXT 1 ROWS ONLY);

-- 30 jugadores: 15 en cada categoría.
WITH n AS (SELECT TOP (30) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_objects)
INSERT PERSONAS (nombre, apellido, dni, genero, fecha_de_nacimiento)
SELECT N'Cobro', CONCAT(N'HU107 ', RIGHT(CONCAT('00', i), 2)), CONCAT('96', RIGHT(CONCAT('000000', i), 6)), 'Masculino', '2012-05-01'
FROM n;

INSERT JUGADORES (FK_id_persona, FK_id_categoria, fecha_alta)
SELECT p.PK_id_persona, CASE WHEN CONVERT(INT, RIGHT(p.dni, 6)) % 2 = 1 THEN @catA ELSE @catB END, '2026-01-01'
FROM PERSONAS p WHERE p.dni LIKE '96______';

DECLARE @jugadores TABLE (orden INT PRIMARY KEY, id INT);
INSERT @jugadores
SELECT CONVERT(INT, RIGHT(p.dni, 6)) - 1, j.PK_id_jugador
FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona
WHERE p.dni LIKE '96______';

-- Septiembre: 126 cobros.
WITH n AS (SELECT TOP (126) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_objects),
c AS (
    SELECT i,
           DATEADD(DAY, (i - 1) % 30, CAST('2026-09-01' AS DATE)) AS dia,
           CHOOSE(1 + i % 6, 9000, 20000, 85000, 100000, 60000, 95000) AS monto
    FROM n
)
INSERT PAGOS (FK_id_jugador, monto_base, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado, concepto, fecha_hora_registro)
SELECT jg.id, c.monto, c.monto, c.dia,
       CASE WHEN c.i % 3 = 0 THEN 'Transferencia' ELSE 'Efectivo' END,
       '2026-09-01', 1, 'Cuota',
       CASE WHEN c.i % 21 = 0 THEN NULL
            -- 1 y 61 caen el mismo día a la misma hora exacta: los desempata el id.
            WHEN c.i IN (1, 61) THEN DATEADD(MINUTE, 9 * 60 + 7, CAST(c.dia AS DATETIME2))
            ELSE DATEADD(SECOND, (c.i * 37) % 60, DATEADD(MINUTE, (8 + c.i % 13) * 60 + (c.i * 7) % 60, CAST(c.dia AS DATETIME2))) END
FROM c JOIN @jugadores jg ON jg.orden = (c.i - 1) % 30;

-- El 30/09 a las 23:59:30 todavía es septiembre.
UPDATE PAGOS SET fecha_hora_registro = '2026-09-30T23:59:30'
WHERE PK_id_pago = (SELECT MAX(p.PK_id_pago) FROM PAGOS p JOIN @jugadores jg ON jg.id = p.FK_id_jugador
                    WHERE p.fecha_pago = '2026-09-30' AND p.fecha_hora_registro IS NOT NULL);

-- Cuotas de septiembre pendientes: no son cobros, no cuentan.
INSERT PAGOS (FK_id_jugador, monto_base, monto_final, fecha_vencimiento, estado, concepto)
SELECT id, 85000, 85000, '2026-10-01', 0, 'Cuota' FROM @jugadores;

-- Octubre: 7 cobros, el primero el 01/10 a las 00:01.
INSERT PAGOS (FK_id_jugador, monto_base, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado, concepto, fecha_hora_registro)
SELECT jg.id, v.monto, v.monto, CAST(v.registro AS DATE), v.medio, '2026-10-01', 1, 'Cuota', v.registro
FROM (VALUES (0, 85000, 'Efectivo', CAST('2026-10-01T00:01:00' AS DATETIME2)),
             (1, 95000, 'Transferencia', '2026-10-03T10:15:00'),
             (2, 85000, 'Efectivo', '2026-10-03T17:40:00'),
             (3, 60000, 'Transferencia', '2026-10-12T09:05:00'),
             (4, 85000, 'Efectivo', '2026-10-20T19:30:00'),
             (5, 95000, 'Efectivo', '2026-10-28T11:00:00'),
             (6, 20000, 'Transferencia', '2026-10-30T16:20:00')) v(orden, monto, medio, registro)
JOIN @jugadores jg ON jg.orden = v.orden;

-- 31/07 23:59: borde del mes vacío (queda en julio).
INSERT PAGOS (FK_id_jugador, monto_base, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado, concepto, fecha_hora_registro)
SELECT id, 85000, 85000, '2026-07-31', 'Efectivo', '2026-07-01', 1, 'Cuota', '2026-07-31T23:59:00' FROM @jugadores WHERE orden = 0;

-- Julio: 5.000 cobros para medir tiempos.
WITH n AS (SELECT TOP (5000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_objects a CROSS JOIN sys.all_objects b)
INSERT PAGOS (FK_id_jugador, monto_base, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado, concepto, fecha_hora_registro)
SELECT jg.id, 1000 * (1 + i % 97), 1000 * (1 + i % 97), DATEADD(DAY, i % 31, CAST('2026-07-01' AS DATE)),
       CASE WHEN i % 2 = 0 THEN 'Transferencia' ELSE 'Efectivo' END, '2026-07-01', 1, 'Cuota',
       DATEADD(SECOND, i % 3600, DATEADD(HOUR, 8 + i % 12, CAST(DATEADD(DAY, i % 31, CAST('2026-07-01' AS DATE)) AS DATETIME2)))
FROM n JOIN @jugadores jg ON jg.orden = i % 30;

COMMIT;

SELECT CONCAT('cobros sep=', (SELECT COUNT(*) FROM PAGOS WHERE estado = 1 AND fecha_pago >= '2026-09-01' AND fecha_pago < '2026-10-01'),
              ' oct=', (SELECT COUNT(*) FROM PAGOS WHERE estado = 1 AND fecha_pago >= '2026-10-01' AND fecha_pago < '2026-11-01'),
              ' ago=', (SELECT COUNT(*) FROM PAGOS WHERE estado = 1 AND fecha_pago >= '2026-08-01' AND fecha_pago < '2026-09-01'),
              ' jul=', (SELECT COUNT(*) FROM PAGOS WHERE estado = 1 AND fecha_pago >= '2026-07-01' AND fecha_pago < '2026-08-01'));
