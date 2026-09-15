-- HU-009 - Datos de prueba para la generación masiva de cuotas mensuales.
-- Base: ClubCamionerosPRUEBA (SQL Server)
--
-- Requiere que V20260914_01__generacion_cuotas_mensuales.sql ya se haya
-- ejecutado: este script solo carga datos, no crea ni modifica tablas.
--
-- Sin esto, sp_GenerateMonthlyFees no genera ninguna cuota: TARIFAS queda
-- vacía después de la migración y ningún jugador tiene tarifa vigente.
--
-- Un arancel vigente por categoría (fecha_fin NULL), con el mismo valor que ya
-- se muestra a modo de ejemplo en la pantalla de Actualización de Aranceles
-- del frontend: $92.000 para las categorías masculinas y $58.000 para
-- Femenino.

USE ClubCamionerosPRUEBA;
GO

BEGIN TRANSACTION;

INSERT INTO TARIFAS (FK_id_categoria, monto, fecha_inicio, fecha_fin)
SELECT
    PK_id_categoria,
    CASE WHEN nombre_categoria = 'Femenino' THEN 58000 ELSE 92000 END,
    '2026-01-01',
    NULL
FROM CATEGORIAS;

COMMIT TRANSACTION;
GO

-- Verificación rápida de lo que va a usar el SP
SELECT t.PK_id_tarifa, c.nombre_categoria, t.monto, t.fecha_inicio, t.fecha_fin
FROM TARIFAS t
    INNER JOIN CATEGORIAS c ON c.PK_id_categoria = t.FK_id_categoria
ORDER BY c.PK_id_categoria;
GO
