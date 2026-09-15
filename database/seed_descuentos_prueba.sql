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
DELETE FROM JUGADORES_DESCUENTOS WHERE FK_id_jugador IN (1, 2, 3, 4, 5, 6);

-- Asignaciones de prueba.
--
--   1-4: bonificaciones vigentes, con etiqueta visible. La 4 es de monto fijo,
--        el caso que incorpora HU-011.
--     5: vigencia ya vencida -> sigue ocupando el lugar del jugador, pero no
--        se etiqueta ni la devuelve GET /discounts.
--     6: dada de baja -> no se etiqueta y libera al jugador para una nueva.
INSERT INTO JUGADORES_DESCUENTOS
    (FK_id_jugador, FK_id_descuento, estado_activo, tipo_valor, porcentaje, monto_fijo, fecha_inicio, fecha_fin)
VALUES
    (1, @becaCompleta, 1, '%', 100.00, NULL,     '2026-01-01', '2026-12-31'),
    (2, @mediaBeca,    1, '%',  50.00, NULL,     '2026-01-01', '2026-12-31'),
    (3, @hermanos,     1, '%',  30.00, NULL,     '2026-01-01', NULL),
    (4, @hermanos,     1, '$',   NULL, 15000.00, '2026-01-01', NULL),
    (5, @becaCompleta, 1, '%', 100.00, NULL,     '2025-01-01', '2025-12-31'),
    (6, @mediaBeca,    0, '%',  50.00, NULL,     '2026-01-01', '2026-12-31');

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
    CASE WHEN jd.estado_activo = 1
          AND (jd.fecha_inicio IS NULL OR CAST(GETDATE() AS DATE) >= jd.fecha_inicio)
          AND (jd.fecha_fin IS NULL OR CAST(GETDATE() AS DATE) <= jd.fecha_fin)
         THEN 'vigente' ELSE 'no vigente' END AS estado
FROM JUGADORES_DESCUENTOS jd
    INNER JOIN JUGADORES j ON j.PK_id_jugador = jd.FK_id_jugador
    INNER JOIN PERSONA p ON p.PK_id_persona = j.FK_id_persona
    INNER JOIN TIPO_DESCUENTO td ON td.PK_id_descuento = jd.FK_id_descuento
ORDER BY jd.FK_id_jugador;
GO
