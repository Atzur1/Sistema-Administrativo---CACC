-- HU-056: precondiciones de las pruebas del cambio de categoría de cobro (PUT /api/integracion/v1/jugadores/{id}/categoria,
-- llamado por el Deportivo desde PUT /api/jugadores/{id}/categoria). Se cargan DESPUÉS del reset de HU-059
-- (qa/HU-059/reset_base.ps1), así que el estado inicial es siempre el mismo.
--
-- Son PRECONDICIONES: jugadores que ya existen ANTES del cambio, con historial financiero que el cambio no puede tocar.
-- El cambio de categoría se ejecuta siempre por las APIs reales.
--
-- DNI reservados del rango 9856xxxx. Ningún dato es de una persona real.
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

INSERT PERSONAS (nombre, apellido, dni, genero, fecha_de_nacimiento) VALUES
    (N'Cambio', N'Categoria Activo', '98560001', 'Masculino', '2010-02-02'),   -- C1: activo en cat. 3, con una cuota pagada y una pendiente
    (N'Cambio', N'Categoria Baja',   '98560002', 'Masculino', '2010-03-03');   -- C2: dado de baja en cat. 3 (no se le cambia la categoría)

INSERT JUGADORES (FK_id_persona, FK_id_categoria, fecha_alta)
SELECT PK_id_persona, 3, '2026-08-01' FROM PERSONAS WHERE dni IN ('98560001', '98560002');
INSERT JUGADORES_CATEGORIAS (FK_id_jugador, FK_id_categoria)
SELECT j.PK_id_jugador, 3 FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni IN ('98560001', '98560002');
UPDATE j SET fecha_baja = '2026-09-30'
FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni = '98560002';

-- Historial de C1: agosto pagado, septiembre pendiente (con el arancel de la cat. 3 de entonces).
INSERT PAGOS (FK_id_jugador, monto_base, monto_final, fecha_vencimiento, estado, concepto, fecha_pago)
SELECT j.PK_id_jugador, 1000, 1000, m.mes, m.estado, 'Cuota', m.pago
FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona
CROSS JOIN (VALUES (CAST('2026-08-01' AS date), 1, CAST('2026-08-05' AS date)), (CAST('2026-09-01' AS date), 0, NULL)) m(mes, estado, pago)
WHERE p.dni = '98560001';

COMMIT;
SELECT CONCAT('hu056: personas=', (SELECT COUNT(*) FROM PERSONAS WHERE dni LIKE '9856____'),
              ' jugadores=', (SELECT COUNT(*) FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni LIKE '9856____'),
              ' pagos=', (SELECT COUNT(*) FROM PAGOS g JOIN JUGADORES j ON j.PK_id_jugador = g.FK_id_jugador JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni LIKE '9856____'));
