-- HU-014 / HU-011 - Datos de prueba de bonificaciones.
-- Base: ClubCamionerosPRUEBA (SQL Server)
--
-- Requiere que ya se hayan ejecutado, en este orden:
--   V20260912_01__recrear_descuentos_identity.sql
--   V20260915_01__descuentos_catalogo_y_monto_fijo.sql
--
-- Desde HU-011 el modelo cambió: TIPO_DESCUENTO es un catálogo cerrado de
-- motivos y el valor y la vigencia de cada beneficio viven en la asignación.
-- Por eso este seed ya no inserta tipos: usa los tres que crea la migración.
--
-- Datos ficticios, pensados para cubrir los criterios de aceptación de ambas
-- historias.

USE ClubCamionerosPRUEBA;
GO

-- El índice filtrado UX_JUGDESC_UNA_ACTIVA exige estas opciones para cualquier
-- INSERT, UPDATE o DELETE sobre JUGADORES_DESCUENTOS. sqlcmd las trae apagadas.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

BEGIN TRANSACTION;

DECLARE @becaCompleta INT = (SELECT PK_id_descuento FROM TIPO_DESCUENTO WHERE tipo_descuento = 'Beca Completa');
DECLARE @mediaBeca    INT = (SELECT PK_id_descuento FROM TIPO_DESCUENTO WHERE tipo_descuento = 'Media Beca');
DECLARE @hermanos     INT = (SELECT PK_id_descuento FROM TIPO_DESCUENTO WHERE tipo_descuento = 'Descuento por Hermanos');

-- Se limpian solo los jugadores del seed, para no pisar asignaciones reales
DELETE FROM JUGADORES_DESCUENTOS WHERE FK_id_jugador IN (1, 2, 3, 4, 5, 6, 7, 8);

-- Asignaciones de prueba. Desde HU-012 toda bonificación lleva un período
-- obligatorio y su estado se deduce de ese rango contra la fecha del servidor,
-- así que el seed cubre los tres casos que el sistema puede devolver.
--
--   1-4: Activa. Vigencia que contiene al día de hoy, etiqueta visible. La 4 es
--        de monto fijo, el caso que incorporó HU-011.
--     5: Expirada. Su período terminó en 2025: no se etiqueta, no la devuelve
--        GET /discounts y ya no bloquea al jugador para una nueva.
--     6: Cancelada. Dada de baja a mano, no se etiqueta.
--     7: Programada. Empieza el año que viene, todavía no se aplica.
--   8-9: dos períodos consecutivos del mismo jugador, que no se pisan. Es lo
--        que HU-012 habilitó al reemplazar la unicidad por la no superposición.
INSERT INTO JUGADORES_DESCUENTOS
    (FK_id_jugador, FK_id_descuento, estado_activo, tipo_valor, porcentaje, monto_fijo, fecha_inicio, fecha_fin)
VALUES
    (1, @becaCompleta, 1, '%', 100.00, NULL,     '2026-01-01', '2026-12-31'),
    (2, @mediaBeca,    1, '%',  50.00, NULL,     '2026-01-01', '2026-12-31'),
    (3, @hermanos,     1, '%',  30.00, NULL,     '2026-01-01', '2026-12-31'),
    (4, @hermanos,     1, '$',   NULL, 15000.00, '2026-01-01', '2026-12-31'),
    (5, @becaCompleta, 1, '%', 100.00, NULL,     '2025-01-01', '2025-12-31'),
    (6, @mediaBeca,    0, '%',  50.00, NULL,     '2026-01-01', '2026-12-31'),
    (7, @mediaBeca,    1, '%',  50.00, NULL,     '2027-01-01', '2027-12-31'),
    (8, @mediaBeca,    1, '%',  50.00, NULL,     '2026-01-01', '2026-06-30'),
    (8, @becaCompleta, 1, '%', 100.00, NULL,     '2026-07-01', '2026-12-31');

COMMIT TRANSACTION;
GO

-- Verificación rápida de lo que va a ver la API
SELECT
    jd.FK_id_jugador,
    p.apellido + ', ' + p.nombre AS jugador,
    td.tipo_descuento AS motivo,
    jd.tipo_valor,
    jd.porcentaje,
    jd.monto_fijo,
    jd.fecha_inicio,
    jd.fecha_fin,
    jd.estado_activo,
    -- Mismo criterio que resuelve DiscountDao: la fecha la pone el servidor
    CASE
        WHEN jd.estado_activo = 0 THEN 'Cancelada'
        WHEN CAST(GETDATE() AS DATE) < jd.fecha_inicio THEN 'Programada'
        WHEN CAST(GETDATE() AS DATE) > jd.fecha_fin THEN 'Expirada'
        ELSE 'Activa'
    END AS estado
FROM JUGADORES_DESCUENTOS jd
    INNER JOIN JUGADORES j ON j.PK_id_jugador = jd.FK_id_jugador
    INNER JOIN PERSONA p ON p.PK_id_persona = j.FK_id_persona
    INNER JOIN TIPO_DESCUENTO td ON td.PK_id_descuento = jd.FK_id_descuento
ORDER BY jd.FK_id_jugador;
GO
