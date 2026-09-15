-- HU-014 - Etiquetado e indicador visual de jugador con bonificación activa.
-- Base: ClubCamionerosPRUEBA (SQL Server)
--
-- Migración de esquema. Corrige el bug reportado en Jira: las PK de
-- TIPO_DESCUENTO y JUGADORES_DESCUENTOS no eran IDENTITY, por lo que todo
-- INSERT fallaba con "Cannot insert the value NULL into column...".
--
-- Ambas tablas están vacías, así que se recrean. PAGOS no se toca: solo se
-- quita y se vuelve a poner su foreign key para poder soltar la tabla.
--
-- Los datos de prueba se cargan aparte, con seed_descuentos_prueba.sql,
-- que debe ejecutarse después de este script.

USE ClubCamionerosPRUEBA;
GO

BEGIN TRANSACTION;

-- 1. Soltar las foreign keys que apuntan a las tablas que se van a recrear
IF OBJECT_ID('FK_PAGOS_JUGDESC', 'F') IS NOT NULL
    ALTER TABLE PAGOS DROP CONSTRAINT FK_PAGOS_JUGDESC;

IF OBJECT_ID('FK_JUGDESC_JUGADOR', 'F') IS NOT NULL
    ALTER TABLE JUGADORES_DESCUENTOS DROP CONSTRAINT FK_JUGDESC_JUGADOR;

IF OBJECT_ID('FK_JUGDESC_TIPODESC', 'F') IS NOT NULL
    ALTER TABLE JUGADORES_DESCUENTOS DROP CONSTRAINT FK_JUGDESC_TIPODESC;

-- 2. Recrear TIPO_DESCUENTO con PK IDENTITY
IF OBJECT_ID('JUGADORES_DESCUENTOS', 'U') IS NOT NULL
    DROP TABLE JUGADORES_DESCUENTOS;

IF OBJECT_ID('TIPO_DESCUENTO', 'U') IS NOT NULL
    DROP TABLE TIPO_DESCUENTO;

CREATE TABLE TIPO_DESCUENTO
(
    PK_id_descuento INT IDENTITY(1,1) NOT NULL,
    tipo_descuento  VARCHAR(100) NULL,
    porcentaje      INT NULL,
    fecha_inicio    DATE NULL,
    fecha_fin       DATE NULL,
    CONSTRAINT PK_TIPO_DESCUENTO PRIMARY KEY (PK_id_descuento)
);

-- 3. Recrear JUGADORES_DESCUENTOS con PK IDENTITY
CREATE TABLE JUGADORES_DESCUENTOS
(
    PK_id_jugador_descuento INT IDENTITY(1,1) NOT NULL,
    FK_id_jugador           INT NULL,
    FK_id_descuento         INT NULL,
    estado_activo           BIT NULL,
    CONSTRAINT PK_JUGADORES_DESCUENTOS PRIMARY KEY (PK_id_jugador_descuento),
    CONSTRAINT FK_JUGDESC_JUGADOR FOREIGN KEY (FK_id_jugador)
        REFERENCES JUGADORES (PK_id_jugador),
    CONSTRAINT FK_JUGDESC_TIPODESC FOREIGN KEY (FK_id_descuento)
        REFERENCES TIPO_DESCUENTO (PK_id_descuento)
);

-- 4. Devolver a PAGOS su foreign key
ALTER TABLE PAGOS ADD CONSTRAINT FK_PAGOS_JUGDESC
    FOREIGN KEY (FK_id_jugador_descuento)
    REFERENCES JUGADORES_DESCUENTOS (PK_id_jugador_descuento);

-- 5. Índice de apoyo: la grilla filtra siempre por jugador y estado.
--    Evita el scan completo al calcular la etiqueta de cada fila (RNF de rendimiento).
CREATE INDEX IX_JUGDESC_JUGADOR_ESTADO
    ON JUGADORES_DESCUENTOS (FK_id_jugador, estado_activo);

COMMIT TRANSACTION;
GO
