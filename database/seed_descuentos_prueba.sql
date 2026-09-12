-- HU-014 - Datos de prueba de bonificaciones.
-- Base: ClubCamionerosPRUEBA (SQL Server)
--
-- Requiere que V20260912_01__recrear_descuentos_identity.sql ya se haya
-- ejecutado: este script solo carga datos, no crea ni modifica tablas.
--
-- Datos ficticios, pensados para cubrir los criterios de aceptación de la HU.

USE ClubCamionerosPRUEBA;
GO

BEGIN TRANSACTION;

-- 1. Tipos de bonificación que muestra la insignia
INSERT INTO TIPO_DESCUENTO (tipo_descuento, porcentaje, fecha_inicio, fecha_fin)
VALUES
    ('Becado 100%',    100, '2026-01-01', '2026-12-31'),
    ('Media Beca',      50, '2026-01-01', '2026-12-31'),
    ('Desc. Hermanos',  30, '2026-01-01', '2026-12-31'),
    ('Beca Vencida',   100, '2025-01-01', '2025-12-31');

-- 2. Asignaciones de prueba.
--    Los primeros cuatro jugadores quedan con bonificación vigente.
--    El quinto tiene una bonificación cuya fecha ya pasó y el sexto una dada de
--    baja: ninguno de los dos debe mostrar etiqueta (criterio de ocultamiento).
INSERT INTO JUGADORES_DESCUENTOS (FK_id_jugador, FK_id_descuento, estado_activo)
VALUES
    (1, 1, 1),
    (2, 2, 1),
    (3, 3, 1),
    (4, 1, 1),
    (5, 4, 1),
    (6, 2, 0);

COMMIT TRANSACTION;
GO

-- Verificación rápida de lo que va a ver la API
SELECT
    j.PK_id_jugador,
    p.apellido + ', ' + p.nombre AS jugador,
    td.tipo_descuento,
    td.fecha_inicio,
    td.fecha_fin,
    jd.estado_activo
FROM JUGADORES_DESCUENTOS jd
    INNER JOIN JUGADORES j ON j.PK_id_jugador = jd.FK_id_jugador
    INNER JOIN PERSONA p ON p.PK_id_persona = j.FK_id_persona
    INNER JOIN TIPO_DESCUENTO td ON td.PK_id_descuento = jd.FK_id_descuento
ORDER BY j.PK_id_jugador;
GO
