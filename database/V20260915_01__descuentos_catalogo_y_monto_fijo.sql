-- HU-011 - Formulario y selección del motivo de bonificación / descuento.
-- Base: ClubCamionerosPRUEBA (SQL Server)
--
-- Migración de esquema. Reordena el modelo de bonificaciones que dejó HU-014.
--
-- Problema que corrige: hasta ahora el porcentaje y las fechas de vigencia
-- vivían en TIPO_DESCUENTO, de modo que esa tabla no era un catálogo de motivos
-- sino una fila por asignación. Con ese modelo, "Media Beca" se repetía como
-- texto libre en cada alta y no había forma de listar los motivos válidos.
--
-- Después de esta migración:
--   TIPO_DESCUENTO       -> catálogo cerrado de motivos (3 filas fijas)
--   JUGADORES_DESCUENTOS -> la asignación, con su valor y su vigencia
--
-- Ambas tablas están vacías en el entorno de trabajo, así que no hay traslado
-- de datos que hacer. Si alguna instalación tuviera filas cargadas, el bloque 1
-- corta la ejecución antes de tocar nada.

USE ClubCamionerosPRUEBA;
GO

-- El índice filtrado del bloque 6 exige estas dos opciones activas. sqlcmd las
-- trae apagadas por defecto, así que se fijan acá y el script queda válido
-- corra desde donde corra (SSMS, Azure Data Studio o línea de comandos).
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- 1. Guarda: la migración redistribuye columnas entre las dos tablas, así que
--    solo corre si no hay asignaciones cargadas. El catálogo en sí puede tener
--    filas (las escribe este mismo script), por eso no se lo mira acá: lo que no
--    puede haber es una asignación previa cuyo valor habría que trasladar.
IF EXISTS (SELECT 1 FROM JUGADORES_DESCUENTOS)
BEGIN
    RAISERROR('V20260915_01: hay bonificaciones asignadas. Revisar el traslado de datos antes de ejecutar.', 16, 1);
    SET NOEXEC ON;
END
GO

-- 2. TIPO_DESCUENTO pasa a ser catálogo: el valor y la vigencia dejan de vivir acá
--    Cada ALTER va en su propio batch: SQL Server no admite soltar una columna y
--    referenciar el nuevo esquema dentro del mismo lote.
IF COL_LENGTH('TIPO_DESCUENTO', 'porcentaje') IS NOT NULL
    ALTER TABLE TIPO_DESCUENTO DROP COLUMN porcentaje;

IF COL_LENGTH('TIPO_DESCUENTO', 'fecha_inicio') IS NOT NULL
    ALTER TABLE TIPO_DESCUENTO DROP COLUMN fecha_inicio;

IF COL_LENGTH('TIPO_DESCUENTO', 'fecha_fin') IS NOT NULL
    ALTER TABLE TIPO_DESCUENTO DROP COLUMN fecha_fin;
GO

-- El motivo es la identidad de la fila: no puede faltar ni repetirse
ALTER TABLE TIPO_DESCUENTO ALTER COLUMN tipo_descuento VARCHAR(100) NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_TIPODESC_NOMBRE')
    CREATE UNIQUE INDEX UX_TIPODESC_NOMBRE ON TIPO_DESCUENTO (tipo_descuento);
GO

-- 3. Los tres motivos que habilita la historia. Son los únicos válidos:
--    la API valida contra esta tabla, no contra una lista en código.
INSERT INTO TIPO_DESCUENTO (tipo_descuento)
SELECT motivo
FROM (VALUES ('Beca Completa'), ('Media Beca'), ('Descuento por Hermanos')) AS catalogo(motivo)
WHERE NOT EXISTS (SELECT 1 FROM TIPO_DESCUENTO td WHERE td.tipo_descuento = catalogo.motivo);
GO

-- 4. JUGADORES_DESCUENTOS recibe el valor y la vigencia de cada asignación.
--    porcentaje es DECIMAL(5,2) y no INT: media beca sobre una cuota partida
--    puede caer en 33,33 % y con INT se perdía el resto.
IF COL_LENGTH('JUGADORES_DESCUENTOS', 'tipo_valor') IS NULL
    ALTER TABLE JUGADORES_DESCUENTOS ADD tipo_valor VARCHAR(10) NULL;
GO

IF COL_LENGTH('JUGADORES_DESCUENTOS', 'porcentaje') IS NULL
    ALTER TABLE JUGADORES_DESCUENTOS ADD porcentaje DECIMAL(5,2) NULL;
GO

IF COL_LENGTH('JUGADORES_DESCUENTOS', 'monto_fijo') IS NULL
    ALTER TABLE JUGADORES_DESCUENTOS ADD monto_fijo DECIMAL(10,2) NULL;
GO

IF COL_LENGTH('JUGADORES_DESCUENTOS', 'fecha_inicio') IS NULL
    ALTER TABLE JUGADORES_DESCUENTOS ADD fecha_inicio DATE NULL;
GO

-- NULL significa vigencia sin vencimiento: una beca suele otorgarse sin fecha
-- de corte y darse de baja a mano cuando el club lo decide.
IF COL_LENGTH('JUGADORES_DESCUENTOS', 'fecha_fin') IS NULL
    ALTER TABLE JUGADORES_DESCUENTOS ADD fecha_fin DATE NULL;
GO

-- 5. Reglas del modelo, declaradas en la tabla y no solo en la API.
--    El índice de apoyo que dejó HU-014 se apoya en FK_id_jugador y bloquea el
--    ALTER COLUMN, así que se suelta acá y se vuelve a crear en el bloque 6.
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_JUGDESC_JUGADOR_ESTADO')
    DROP INDEX IX_JUGDESC_JUGADOR_ESTADO ON JUGADORES_DESCUENTOS;
GO

-- Una asignación sin jugador, sin motivo o sin estado no significa nada
UPDATE JUGADORES_DESCUENTOS SET estado_activo = 1 WHERE estado_activo IS NULL;
GO

ALTER TABLE JUGADORES_DESCUENTOS ALTER COLUMN FK_id_jugador INT NOT NULL;
GO
ALTER TABLE JUGADORES_DESCUENTOS ALTER COLUMN FK_id_descuento INT NOT NULL;
GO
ALTER TABLE JUGADORES_DESCUENTOS ALTER COLUMN estado_activo BIT NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_JUGDESC_ESTADO')
    ALTER TABLE JUGADORES_DESCUENTOS
        ADD CONSTRAINT DF_JUGDESC_ESTADO DEFAULT 1 FOR estado_activo;
GO

-- El tipo de valor es cerrado: lo que no es '%' es '$'
IF OBJECT_ID('CK_JUGDESC_TIPO_VALOR', 'C') IS NULL
    ALTER TABLE JUGADORES_DESCUENTOS
        ADD CONSTRAINT CK_JUGDESC_TIPO_VALOR CHECK (tipo_valor IN ('%', '$'));
GO

-- Porcentaje y monto fijo son mutuamente excluyentes. Esto es el corazón de la
-- historia: una bonificación es porcentual o de monto fijo, nunca las dos.
IF OBJECT_ID('CK_JUGDESC_VALOR_EXCLUYENTE', 'C') IS NULL
    ALTER TABLE JUGADORES_DESCUENTOS
        ADD CONSTRAINT CK_JUGDESC_VALOR_EXCLUYENTE CHECK
        (
            (tipo_valor = '%' AND porcentaje IS NOT NULL AND monto_fijo IS NULL)
            OR
            (tipo_valor = '$' AND monto_fijo IS NOT NULL AND porcentaje IS NULL)
        );
GO

-- Rangos admitidos: un porcentaje fuera de 0-100 o un monto negativo no son
-- una bonificación, son un error de carga.
IF OBJECT_ID('CK_JUGDESC_PORCENTAJE_RANGO', 'C') IS NULL
    ALTER TABLE JUGADORES_DESCUENTOS
        ADD CONSTRAINT CK_JUGDESC_PORCENTAJE_RANGO CHECK
        (porcentaje IS NULL OR (porcentaje > 0 AND porcentaje <= 100));
GO

IF OBJECT_ID('CK_JUGDESC_MONTO_POSITIVO', 'C') IS NULL
    ALTER TABLE JUGADORES_DESCUENTOS
        ADD CONSTRAINT CK_JUGDESC_MONTO_POSITIVO CHECK
        (monto_fijo IS NULL OR monto_fijo > 0);
GO

-- La vigencia no puede terminar antes de empezar
IF OBJECT_ID('CK_JUGDESC_VIGENCIA', 'C') IS NULL
    ALTER TABLE JUGADORES_DESCUENTOS
        ADD CONSTRAINT CK_JUGDESC_VIGENCIA CHECK
        (fecha_fin IS NULL OR fecha_inicio IS NULL OR fecha_fin >= fecha_inicio);
GO

-- 6. Regla de negocio central: un único beneficio activo por jugador.
--    El índice filtrado la sostiene en el motor, así que ni una llamada directa
--    a la API ni un INSERT manual pueden dejar dos activas para el mismo
--    jugador. La API igual valida antes, para devolver un error entendible en
--    lugar de una violación de índice.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_JUGDESC_UNA_ACTIVA')
    CREATE UNIQUE INDEX UX_JUGDESC_UNA_ACTIVA
        ON JUGADORES_DESCUENTOS (FK_id_jugador)
        WHERE estado_activo = 1;
GO

-- 7. Se repone el índice de apoyo de HU-014, que el bloque 5 tuvo que soltar.
--    Sigue sirviendo a las consultas que miran también las bajas, algo que el
--    índice filtrado de arriba no cubre.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_JUGDESC_JUGADOR_ESTADO')
    CREATE INDEX IX_JUGDESC_JUGADOR_ESTADO
        ON JUGADORES_DESCUENTOS (FK_id_jugador, estado_activo);
GO

SET NOEXEC OFF;
GO
