-- HU-012 - Configuración y validación de fechas de vigencia para bonificaciones.
-- Base: ClubCamionerosPRUEBA (SQL Server)
--
-- Tres cambios sobre el modelo que dejó HU-011:
--
--   1. fecha_inicio y fecha_fin pasan a ser obligatorias. Hasta ahora una
--      bonificación podía otorgarse sin vencimiento y quedaba corriendo hasta
--      que alguien la diera de baja a mano.
--
--   2. La vigencia pasa a exigir fecha_fin ESTRICTAMENTE posterior a
--      fecha_inicio. El CHECK anterior admitía que fueran iguales, lo que
--      describe un beneficio de un solo día que el negocio no contempla.
--
--   3. CAMBIO DE REGLA DE NEGOCIO: se reemplaza "una bonificación abierta por
--      jugador" por "una bonificación APLICABLE por fecha".
--
-- Sobre el punto 3, que es el de fondo.
--
-- Hasta acá, el índice filtrado UX_JUGDESC_UNA_ACTIVA impedía que un jugador
-- tuviera dos filas con estado_activo = 1, sin mirar las fechas. Con la
-- caducidad automática que pide HU-012 eso deja de servir: una bonificación
-- vencida en 2025 seguía ocupando el lugar del jugador en 2026 y había que
-- cancelarla a mano antes de asignarle otra, que es justamente la intervención
-- manual que la historia quiere eliminar. Tampoco permitía dejar programada una
-- bonificación para el año que viene mientras corre la de este año.
--
-- No alcanza con corregir el filtro del índice: SQL Server exige que la
-- condición sea determinista y GETDATE() no lo es, así que un "índice de
-- vigentes" no se puede declarar.
--
-- Por eso el índice se elimina y la regla pasa a validarse en el DAO, buscando
-- solapamiento de rangos dentro de la misma transacción que inserta, con
-- UPDLOCK y HOLDLOCK sobre el rango consultado para que dos altas simultáneas
-- no se crucen.
--
-- Queda asentado el costo: la unicidad deja de estar garantizada por el motor y
-- pasa a depender de la capa de datos. Se eligió así porque la alternativa
-- obligaba a un paso manual en cada vencimiento. La decisión fue acordada antes
-- de implementar y está documentada en QA-HU-012.md.

USE ClubCamionerosPRUEBA;
GO

-- Necesarias para tocar la tabla mientras todavía existe el índice filtrado
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- 1. Cerrar las asignaciones que quedaron sin fecha de fin.
--    Se las lleva al 31 de diciembre del año en que empezaron: es el corte que
--    usa el club para el ciclo deportivo y evita perder el beneficio.
UPDATE JUGADORES_DESCUENTOS
SET fecha_fin = DATEFROMPARTS(YEAR(fecha_inicio), 12, 31)
WHERE fecha_fin IS NULL
  AND fecha_inicio IS NOT NULL;
GO

-- Una fila sin fecha de inicio no se puede reconstruir sola, y tampoco debería
-- existir. Si aparece alguna, la migración corta antes de tocar el esquema.
IF EXISTS (SELECT 1 FROM JUGADORES_DESCUENTOS WHERE fecha_inicio IS NULL OR fecha_fin IS NULL)
BEGIN
    RAISERROR('V20260915_02: hay bonificaciones sin fecha de inicio o de fin que no se pudieron completar. Revisarlas a mano antes de ejecutar.', 16, 1);
    SET NOEXEC ON;
END
GO

-- Igual que arriba: un rango de un solo día no puede quedar bajo el CHECK nuevo
IF EXISTS (SELECT 1 FROM JUGADORES_DESCUENTOS WHERE fecha_fin <= fecha_inicio)
BEGIN
    RAISERROR('V20260915_02: hay bonificaciones cuya fecha de fin no es posterior a la de inicio. Corregirlas antes de ejecutar.', 16, 1);
    SET NOEXEC ON;
END
GO

-- 2. El CHECK viejo referencia las columnas, así que se suelta antes del ALTER
IF OBJECT_ID('CK_JUGDESC_VIGENCIA', 'C') IS NOT NULL
    ALTER TABLE JUGADORES_DESCUENTOS DROP CONSTRAINT CK_JUGDESC_VIGENCIA;
GO

ALTER TABLE JUGADORES_DESCUENTOS ALTER COLUMN fecha_inicio DATE NOT NULL;
GO
ALTER TABLE JUGADORES_DESCUENTOS ALTER COLUMN fecha_fin DATE NOT NULL;
GO

-- 3. Vigencia estricta: el fin tiene que ser posterior al inicio, no igual
IF OBJECT_ID('CK_JUGDESC_VIGENCIA', 'C') IS NULL
    ALTER TABLE JUGADORES_DESCUENTOS
        ADD CONSTRAINT CK_JUGDESC_VIGENCIA CHECK (fecha_fin > fecha_inicio);
GO

-- 4. Cae el índice de unicidad por jugador. A partir de acá un jugador puede
--    acumular su historial, su bonificación vigente y una programada, siempre
--    que los rangos no se pisen. Esa validación vive en DiscountDao.
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_JUGDESC_UNA_ACTIVA')
    DROP INDEX UX_JUGDESC_UNA_ACTIVA ON JUGADORES_DESCUENTOS;
GO

-- 5. Índice de apoyo para la consulta que reemplaza al índice único: buscar si
--    un rango se superpone con otro del mismo jugador. Cubre las cuatro
--    columnas que entran en esa comparación, así que se resuelve sin ir a la
--    tabla.
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_JUGDESC_JUGADOR_ESTADO')
    DROP INDEX IX_JUGDESC_JUGADOR_ESTADO ON JUGADORES_DESCUENTOS;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_JUGDESC_JUGADOR_VIGENCIA')
    CREATE INDEX IX_JUGDESC_JUGADOR_VIGENCIA
        ON JUGADORES_DESCUENTOS (FK_id_jugador, estado_activo, fecha_inicio, fecha_fin);
GO

SET NOEXEC OFF;
GO
