-- Solo lectura. Ejecutar conectado a la instancia local correcta.
USE [ClubCamionerosPRUEBA];
GO
SET NOCOUNT ON;

SELECT @@SERVERNAME AS servidor, DB_NAME() AS base_actual;

SELECT s.name AS esquema, t.name AS tabla, c.name AS columna,
       ty.name AS tipo, c.max_length, c.precision, c.scale,
       c.is_nullable, c.is_identity, dc.definition AS valor_predeterminado
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
JOIN sys.columns c ON c.object_id = t.object_id
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
WHERE t.name IN ('TIPO_DESCUENTO', 'JUGADORES_DESCUENTOS', 'ARANCELES', 'PAGOS')
ORDER BY s.name, t.name, c.column_id;

SELECT t.name AS tabla, SUM(p.rows) AS cantidad_filas
FROM sys.tables t
JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
GROUP BY t.name
ORDER BY t.name;

SELECT fk.name AS relacion,
       OBJECT_SCHEMA_NAME(fk.parent_object_id) AS esquema_origen,
       OBJECT_NAME(fk.parent_object_id) AS tabla_origen,
       COL_NAME(fkc.parent_object_id, fkc.parent_column_id) AS columna_origen,
       OBJECT_NAME(fk.referenced_object_id) AS tabla_destino,
       COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) AS columna_destino,
       fk.is_disabled, fk.is_not_trusted
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
ORDER BY tabla_origen, relacion;

SELECT OBJECT_NAME(parent_object_id) AS tabla, name, definition,
       is_disabled, is_not_trusted
FROM sys.check_constraints
WHERE parent_object_id IN (OBJECT_ID('dbo.TIPO_DESCUENTO'),
                          OBJECT_ID('dbo.JUGADORES_DESCUENTOS'),
                          OBJECT_ID('dbo.ARANCELES'));

SELECT OBJECT_NAME(i.object_id) AS tabla, i.name AS indice,
       i.is_unique, i.is_primary_key, i.filter_definition,
       c.name AS columna, ic.key_ordinal, ic.is_included_column
FROM sys.indexes i
JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE i.object_id IN (OBJECT_ID('dbo.TIPO_DESCUENTO'),
                     OBJECT_ID('dbo.JUGADORES_DESCUENTOS'),
                     OBJECT_ID('dbo.ARANCELES'), OBJECT_ID('dbo.PAGOS'))
ORDER BY tabla, indice, ic.index_column_id;

-- No devuelve usuarios, contraseñas ni datos personales.
