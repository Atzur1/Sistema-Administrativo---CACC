-- Escenario de regresión de HU-014 sobre el modelo nuevo de HU-011.
-- Reproduce los tres casos que QA-HU-014 dejó documentados:
--   jugador 101 -> bonificación vigente          => GET debe dar 200
--   jugador 102 -> bonificación vencida          => GET debe dar 404
--   jugador 103 -> bonificación dada de baja     => GET debe dar 404
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

DECLARE @beca  INT = (SELECT PK_id_descuento FROM TIPO_DESCUENTO WHERE tipo_descuento = 'Beca Completa');
DECLARE @media INT = (SELECT PK_id_descuento FROM TIPO_DESCUENTO WHERE tipo_descuento = 'Media Beca');

DELETE FROM JUGADORES_DESCUENTOS WHERE FK_id_jugador IN (101, 102, 103);

-- Vigente: empezó ayer y no vence
INSERT INTO JUGADORES_DESCUENTOS
    (FK_id_jugador, FK_id_descuento, estado_activo, tipo_valor, porcentaje, monto_fijo, fecha_inicio, fecha_fin)
VALUES (101, @beca, 1, '%', 100.00, NULL, DATEADD(DAY, -1, CAST(GETDATE() AS DATE)), NULL);

-- Vencida: su ventana terminó el año pasado, pero sigue abierta (estado_activo = 1)
INSERT INTO JUGADORES_DESCUENTOS
    (FK_id_jugador, FK_id_descuento, estado_activo, tipo_valor, porcentaje, monto_fijo, fecha_inicio, fecha_fin)
VALUES (102, @media, 1, '%', 50.00, NULL, '2025-01-01', '2025-12-31');

-- Dada de baja
INSERT INTO JUGADORES_DESCUENTOS
    (FK_id_jugador, FK_id_descuento, estado_activo, tipo_valor, porcentaje, monto_fijo, fecha_inicio, fecha_fin)
VALUES (103, @media, 0, '%', 50.00, NULL, '2026-01-01', NULL);

SELECT FK_id_jugador, estado_activo, fecha_inicio, fecha_fin
FROM JUGADORES_DESCUENTOS WHERE FK_id_jugador IN (101, 102, 103)
ORDER BY FK_id_jugador;
