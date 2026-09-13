-- HU-010 - Inmutabilidad del arancel historico en cuotas adeudadas.
-- Base: ClubCamionerosPRUEBA (SQL Server)
--
-- Carga la tarifa vigente inicial para cada rama. Debe ejecutarse despues de
-- V20260913_01__crear_tarifas_por_rama.sql.
--
-- Sin esto, TARIFAS queda vacia y el proceso de emision de cuotas (fase
-- Backend de HU-010) no tendria ninguna tarifa vigente que resolver.

USE ClubCamionerosPRUEBA;
GO

-- Requerido para insertar en TARIFAS: tiene un indice filtrado
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;

IF NOT EXISTS (SELECT 1 FROM TARIFAS WHERE rama = 'M' AND fecha_fin IS NULL)
    INSERT INTO TARIFAS (rama, monto, fecha_inicio, fecha_fin)
    VALUES ('M', 85000.00, '2026-09-01', NULL);

IF NOT EXISTS (SELECT 1 FROM TARIFAS WHERE rama = 'F' AND fecha_fin IS NULL)
    INSERT INTO TARIFAS (rama, monto, fecha_inicio, fecha_fin)
    VALUES ('F', 58000.00, '2026-09-01', NULL);

COMMIT TRANSACTION;
GO
