-- HU-059: datos de negocio mínimos para las pruebas de punta a punta. Se cargan sobre una base RECIÉN creada por
-- reset_base.ps1 (esquema unificado + migraciones + cuentas), así que el estado inicial es siempre el mismo.
--
-- Son PRECONDICIONES, no los flujos que se prueban: el alta (HU-056) y la baja (HU-057) se ejecutan siempre por las APIs. Los
-- jugadores de abajo se insertan directo porque cada escenario necesita uno que ya exista ANTES de empezar (un jugador activo con
-- deuda para darlo de baja, una persona sin jugador para el caso "DNI existente").
--
-- DNI reservados del rango 9859xxxx. Ningún dato es de una persona real.
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Aranceles vigentes desde enero de 2026 (rama masculina): cuota mensual y la inscripción, que HU-056 exige en la rama masculina.
INSERT ARANCELES (genero, FK_id_categoria, monto, vigente_desde) VALUES ('Masculino', NULL, 1000, '2026-01-01');
INSERT ARANCELES_INSCRIPCION (genero, monto, vigente_desde) VALUES ('Masculino', 500, '2026-01-01');

-- Personas (todas de la rama masculina, nacidas en 2012).
INSERT PERSONAS (nombre, apellido, dni, genero, fecha_de_nacimiento) VALUES
    (N'Prueba', N'Baja Contrato',   '98590001', 'Masculino', '2012-01-01'),  -- F1: activo con deuda, se da de baja por el contrato HU-057
    (N'Prueba', N'Baja Deportivo',  '98590002', 'Masculino', '2012-01-01'),  -- F2: activo con deuda, se da de baja desde la API del Deportivo
    (N'Prueba', N'Control Activo',  '98590003', 'Masculino', '2012-01-01'),  -- F3: activo sin deuda, control de la generación de cuotas
    (N'Prueba', N'Persona Sin Jug', '98590004', 'Masculino', '2012-01-01'),  -- F4: persona existente SIN jugador (HU-056, DNI existente)
    (N'Prueba', N'Baja Temprana',   '98590006', 'Masculino', '2012-01-01');  -- F5: activo desde julio, se da de baja el 15/07 (meses cerrados posteriores)
-- 98590005 no existe: es la persona nueva del alta (HU-056).

-- Jugadores activos en la categoría 3 (de cobro y de pertenencia deportiva): F1-F3 desde el 01/08/2026, F5 desde el 01/07/2026.
INSERT JUGADORES (FK_id_persona, FK_id_categoria, fecha_alta)
SELECT PK_id_persona, 3, CASE dni WHEN '98590006' THEN '2026-07-01' ELSE '2026-08-01' END
FROM PERSONAS WHERE dni IN ('98590001', '98590002', '98590003', '98590006');
INSERT JUGADORES_CATEGORIAS (FK_id_jugador, FK_id_categoria)
SELECT j.PK_id_jugador, 3 FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni IN ('98590001', '98590002', '98590003', '98590006');

-- Deuda anterior de F5: julio de 2026 pendiente.
INSERT PAGOS (FK_id_jugador, monto_base, monto_final, fecha_vencimiento, estado, concepto)
SELECT j.PK_id_jugador, 1000, 1000, '2026-07-01', 0, 'Cuota'
FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni = '98590006';

-- Deuda anterior de F1 y F2: agosto y septiembre de 2026 pendientes (vencidas desde el 10 del mes siguiente).
INSERT PAGOS (FK_id_jugador, monto_base, monto_final, fecha_vencimiento, estado, concepto)
SELECT j.PK_id_jugador, 1000, 1000, m.mes, 0, 'Cuota'
FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona
CROSS JOIN (VALUES (CAST('2026-08-01' AS date)), (CAST('2026-09-01' AS date))) m(mes)
WHERE p.dni IN ('98590001', '98590002');

COMMIT;
SELECT CONCAT('fixtures: personas=', (SELECT COUNT(*) FROM PERSONAS WHERE dni LIKE '9859____'),
              ' jugadores=', (SELECT COUNT(*) FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni LIKE '9859____'),
              ' cuotas=', (SELECT COUNT(*) FROM PAGOS));
