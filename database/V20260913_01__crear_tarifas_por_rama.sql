-- HU-010 - Inmutabilidad del arancel historico en cuotas adeudadas.
-- Base: ClubCamionerosPRUEBA (SQL Server)
--
-- Crea la infraestructura de tarifas vigentes por rama (Masculino/Femenino),
-- totalmente desacoplada entre si: cada rama tiene su propia linea de tiempo
-- de vigencias, sin relacion (FK) con CATEGORIAS ni entre ellas. Un aumento
-- en una rama no puede afectar, ni por JOIN ni por FK, a la otra.
--
-- CATEGORIAS solo gana una columna de clasificacion (rama) para poder
-- resolver, al emitir una cuota, a que rama pertenece un jugador segun su
-- categoria real (12 categorias por edad = Masculino, "Femenino" = Femenino).
-- Esa columna no participa del calculo del monto: solo sirve para elegir
-- cual de las dos lineas de TARIFAS aplica.
--
-- Esta migracion NO toca PAGOS ni VISTA_ESTADO_DEUDA (fuera de alcance de
-- HU-010). El monto ya persistido en PAGOS.monto_base/monto_final para
-- cuotas existentes permanece intacto.
--
-- Nota: cada paso va en su propio lote (GO) porque SQL Server resuelve los
-- nombres de columna al compilar el lote completo, no al ejecutar cada
-- sentencia; sin el GO, el ALTER TABLE ADD y su uso inmediato en el mismo
-- lote fallan con "Invalid column name".

USE ClubCamionerosPRUEBA;
GO

-- Requerido por el indice filtrado (WHERE fecha_fin IS NULL) mas abajo
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;
GO

-- 1. Agregar la columna de clasificacion (si no existe)
IF COL_LENGTH('CATEGORIAS', 'rama') IS NULL
    ALTER TABLE CATEGORIAS ADD rama CHAR(1) NULL;
GO

-- 2. Backfill por nombre, no por ID, para no depender de que los PK
--    coincidan entre entornos
UPDATE CATEGORIAS SET rama = 'F' WHERE nombre_categoria = 'Femenino';
UPDATE CATEGORIAS SET rama = 'M' WHERE rama IS NULL;
GO

-- 3. Cerrar la columna: NOT NULL + CHECK
IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints WHERE name = 'CK_CATEGORIAS_RAMA'
)
BEGIN
    ALTER TABLE CATEGORIAS ALTER COLUMN rama CHAR(1) NOT NULL;
    ALTER TABLE CATEGORIAS ADD CONSTRAINT CK_CATEGORIAS_RAMA CHECK (rama IN ('M', 'F'));
END
GO

-- 4. Tabla TARIFAS: una linea de vigencias independiente por rama.
--    Sin FK a CATEGORIAS a proposito: la tarifa masculina y la femenina no
--    comparten ninguna dependencia estructural entre si.
IF OBJECT_ID('TARIFAS', 'U') IS NULL
BEGIN
    CREATE TABLE TARIFAS
    (
        PK_id_tarifa INT IDENTITY(1,1) NOT NULL,
        rama         CHAR(1) NOT NULL,
        monto        DECIMAL(10,2) NOT NULL,
        fecha_inicio DATE NOT NULL,
        fecha_fin    DATE NULL, -- NULL = vigente / sin reemplazo programado
        CONSTRAINT PK_TARIFAS PRIMARY KEY (PK_id_tarifa),
        CONSTRAINT CK_TARIFAS_RAMA CHECK (rama IN ('M', 'F')),
        CONSTRAINT CK_TARIFAS_MONTO CHECK (monto > 0),
        CONSTRAINT CK_TARIFAS_RANGO_FECHAS CHECK (fecha_fin IS NULL OR fecha_fin >= fecha_inicio)
    );

    -- Como maximo una tarifa "vigente" (fecha_fin NULL) por rama: el motor
    -- impide dejar dos lineas abiertas simultaneas para el mismo genero.
    CREATE UNIQUE INDEX UX_TARIFAS_RAMA_VIGENTE
        ON TARIFAS (rama)
        WHERE fecha_fin IS NULL;

    -- La emision de cuotas siempre busca "la vigente a la fecha X para la
    -- rama Y": este es el acceso mas frecuente.
    CREATE INDEX IX_TARIFAS_RAMA_VIGENCIA
        ON TARIFAS (rama, fecha_inicio, fecha_fin);
END
GO

COMMIT TRANSACTION;
GO
