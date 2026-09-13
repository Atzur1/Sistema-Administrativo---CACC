-- HU-015 - Selección obligatoria del método de pago (Efectivo o Transferencia) en el cobro.
-- Base: ClubCamionerosPRUEBA (SQL Server)
--
-- Migración de esquema. Motivos:
--   1. PAGOS.PK_id_pago no es IDENTITY: todo INSERT sin id explícito falla con
--      "Cannot insert the value NULL into column...". SQL Server no permite
--      convertir una columna existente en IDENTITY con ALTER, así que la tabla
--      se recrea (PAGOS_NUEVA) y se renombra a PAGOS.
--   2. Se agregan los datos de trazabilidad del cobro que pide la HU:
--      referencia bancaria opcional, usuario que registró el cobro y fecha/hora
--      del registro.
--   3. Se restringe metodo_pago a 'efectivo' | 'transferencia' (en minúsculas,
--      igual que los registros existentes) y se bloquea su modificación.
--
-- Las filas existentes se copian conservando su PK_id_pago: es preservación de
-- datos propia de la recreación de la tabla, no una carga de datos de prueba.
-- Los pagos históricos quedan con referencia_pago, FK_id_usuario_registro y
-- fecha_registro en NULL porque ese dato no se registraba.
--
-- VISTA_ESTADO_DEUDA es una tabla (no una vista) con la FK FK_DEUDA_PAGO hacia
-- PAGOS. Se suelta y se vuelve a crear apuntando a la tabla nueva.
--
-- Todo corre en un único lote y una única transacción: ante cualquier error
-- (XACT_ABORT) se revierte completo y la base queda como estaba.
-- Hacer un backup de la base antes de ejecutarlo.

USE ClubCamionerosPRUEBA;
GO

SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

-- 1. Soltar las foreign keys de PAGOS y la que apunta hacia PAGOS
ALTER TABLE VISTA_ESTADO_DEUDA DROP CONSTRAINT FK_DEUDA_PAGO;
ALTER TABLE PAGOS DROP CONSTRAINT FK_PAGOS_JUGADOR;
ALTER TABLE PAGOS DROP CONSTRAINT FK_PAGOS_JUGDESC;

-- 2. Crear la tabla nueva con PK IDENTITY y las columnas de HU-015
CREATE TABLE PAGOS_NUEVA
(
    PK_id_pago              INT IDENTITY(1,1) NOT NULL,
    FK_id_jugador           INT NULL,
    monto_base              DECIMAL(18,2) NULL,
    FK_id_jugador_descuento INT NULL,
    monto_final             DECIMAL(18,2) NULL,
    fecha_pago              DATE NULL,
    metodo_pago             VARCHAR(50) NOT NULL,
    fecha_vencimiento       DATE NULL,
    estado                  BIT NULL,
    referencia_pago         VARCHAR(50) NULL,
    FK_id_usuario_registro  INT NULL,
    fecha_registro          DATETIME2(0) NULL
        CONSTRAINT DF_PAGOS_fecha_registro DEFAULT SYSDATETIME(),
    CONSTRAINT PK_PAGOS PRIMARY KEY (PK_id_pago),
    CONSTRAINT FK_PAGOS_JUGADOR FOREIGN KEY (FK_id_jugador)
        REFERENCES JUGADORES (PK_id_jugador),
    CONSTRAINT FK_PAGOS_JUGDESC FOREIGN KEY (FK_id_jugador_descuento)
        REFERENCES JUGADORES_DESCUENTOS (PK_id_jugador_descuento),
    CONSTRAINT FK_PAGOS_USUARIO FOREIGN KEY (FK_id_usuario_registro)
        REFERENCES USUARIO (PK_id_usuario),
    -- La collation de la base (Modern_Spanish_CI_AS) no distingue mayúsculas:
    -- se fuerza una comparación sensible para aceptar solo minúsculas.
    CONSTRAINT CK_PAGOS_metodo_pago CHECK
        (metodo_pago COLLATE Latin1_General_CS_AS IN ('efectivo', 'transferencia')),
    -- La referencia bancaria solo corresponde a una transferencia
    CONSTRAINT CK_PAGOS_referencia_pago CHECK
        (referencia_pago IS NULL OR metodo_pago = 'transferencia')
);

-- 3. Copiar los pagos existentes conservando su id
SET IDENTITY_INSERT PAGOS_NUEVA ON;

INSERT INTO PAGOS_NUEVA
    (PK_id_pago, FK_id_jugador, monto_base, FK_id_jugador_descuento, monto_final,
     fecha_pago, metodo_pago, fecha_vencimiento, estado,
     referencia_pago, FK_id_usuario_registro, fecha_registro)
SELECT
    PK_id_pago, FK_id_jugador, monto_base, FK_id_jugador_descuento, monto_final,
    fecha_pago, metodo_pago, fecha_vencimiento, estado,
    NULL, NULL, NULL
FROM PAGOS WITH (TABLOCKX, HOLDLOCK);

SET IDENTITY_INSERT PAGOS_NUEVA OFF;

-- 4. Verificar que no se perdió ninguna fila antes de soltar la tabla vieja
IF (SELECT COUNT(*) FROM PAGOS_NUEVA) <> (SELECT COUNT(*) FROM PAGOS)
    THROW 50001, 'La cantidad de filas copiadas no coincide con PAGOS. Se revierte la migración.', 1;

-- 5. Devolver a VISTA_ESTADO_DEUDA su foreign key, ahora hacia la tabla nueva
ALTER TABLE VISTA_ESTADO_DEUDA ADD CONSTRAINT FK_DEUDA_PAGO
    FOREIGN KEY (FK_id_pago) REFERENCES PAGOS_NUEVA (PK_id_pago);

-- 6. Reemplazar la tabla vieja
DROP TABLE PAGOS;
EXEC sp_rename 'PAGOS_NUEVA', 'PAGOS';

-- 7. Trigger de inmutabilidad: un cobro registrado no puede cambiar su método de
--    pago ni sus datos de registro. CREATE TRIGGER debe ser la primera sentencia
--    de su lote, por eso se ejecuta con EXEC dentro de la misma transacción.
EXEC('
CREATE TRIGGER TR_PAGOS_bloquear_modificacion_cobro
ON PAGOS
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT i.PK_id_pago, i.metodo_pago COLLATE Latin1_General_CS_AS,
               i.referencia_pago, i.FK_id_usuario_registro, i.fecha_registro
        FROM inserted i
        EXCEPT
        SELECT d.PK_id_pago, d.metodo_pago COLLATE Latin1_General_CS_AS,
               d.referencia_pago, d.FK_id_usuario_registro, d.fecha_registro
        FROM deleted d
    )
        THROW 51000, ''El método de pago y los datos de registro de un cobro no se pueden modificar.'', 1;
END
');

COMMIT TRANSACTION;
GO

-- Verificación posterior (solo lectura)
SELECT COUNT(*) AS filas, MAX(PK_id_pago) AS max_id, IDENT_CURRENT('PAGOS') AS identity_actual
FROM PAGOS;
