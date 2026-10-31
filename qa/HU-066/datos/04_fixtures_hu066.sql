-- HU-066: precondiciones fijas de las pruebas de concurrencia. Se cargan DESPUÉS del reset de HU-059
-- (qa/HU-059/reset_base.ps1: esquema unificado + migraciones + cuentas ficticias + fixtures 9859xxxx).
--
-- Son PRECONDICIONES, no los flujos que se prueban: las altas, las generaciones y los pagos se disparan siempre por las APIs.
-- Las precondiciones que cada iteración necesita frescas (jugadores sin la cuota del mes, cuotas pendientes para cobrar) las
-- inserta la suite antes de cada ráfaga (función Precondicion de concurrencia_hu066.ps1), con el mismo criterio.
--
-- DNI reservados del rango 9866xxxx. Ningún dato es de una persona real.
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Caso 3 (misma identidad): PERSONAS que ya existen SIN jugador. En cada iteración dos altas del Deportivo y una del portal
-- intentan darla de alta como jugador a la vez: tiene que quedar UN jugador.
INSERT PERSONAS (nombre, apellido, dni, genero, fecha_de_nacimiento)
SELECT N'Previa', CONCAT(N'Identidad ', n), CONCAT('9869', RIGHT(CONCAT('000', n), 4)), 'Masculino', '2011-05-05'
FROM (VALUES (1), (2), (3), (4), (5), (6), (7), (8), (9), (10), (11), (12), (13), (14), (15), (16), (17), (18), (19), (20)) v(n);

COMMIT;
SELECT CONCAT('hu066: personas sin jugador=', (SELECT COUNT(*) FROM PERSONAS p WHERE p.dni LIKE '9869____'
    AND NOT EXISTS (SELECT 1 FROM JUGADORES j WHERE j.FK_id_persona = p.PK_id_persona)));
