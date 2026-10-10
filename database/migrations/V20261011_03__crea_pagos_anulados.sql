-- Anulacion de pagos cargados por error. Hasta ahora no habia forma de corregirlos sin tocar la base a mano.
--
-- Anular un abono lo saca de PAGOS (asi deja de sumar en todos los totales, sin tener que filtrar una
-- marca en cada consulta) y devuelve su monto al saldo pendiente de la cuota o la inscripcion. Esta tabla
-- es el registro permanente y de solo agregado de cada anulacion: que se anulo, por que, quien y cuando.
-- TR_AUDIT_PAGOS ademas deja el borrado en AUDITORIA_CAMBIOS.
IF OBJECT_ID(N'dbo.PAGOS_ANULADOS', N'U') IS NULL
    CREATE TABLE dbo.PAGOS_ANULADOS (
        PK_id_anulacion INT IDENTITY(1,1) NOT NULL,
        id_pago INT NOT NULL,
        FK_id_jugador INT NOT NULL,
        concepto VARCHAR(20) NOT NULL,
        monto DECIMAL(18,2) NOT NULL,
        monto_base DECIMAL(18,2) NULL,
        metodo_pago VARCHAR(50) NULL,
        fecha_pago DATE NULL,
        fecha_vencimiento DATE NULL,
        fecha_hora_registro_pago DATETIME2(7) NULL,
        FK_id_usuario_registro_pago INT NULL,
        motivo NVARCHAR(500) NOT NULL,
        FK_id_usuario_anulacion INT NOT NULL,
        fecha_hora_anulacion DATETIME2(7) NOT NULL,
        CONSTRAINT PK_PAGOS_ANULADOS PRIMARY KEY CLUSTERED (PK_id_anulacion),
        CONSTRAINT FK_PAGOS_ANULADOS_JUGADOR FOREIGN KEY (FK_id_jugador) REFERENCES dbo.JUGADORES (PK_id_jugador),
        CONSTRAINT FK_PAGOS_ANULADOS_USUARIO FOREIGN KEY (FK_id_usuario_anulacion) REFERENCES dbo.USUARIO (PK_id_usuario),
        CONSTRAINT CK_PAGOS_ANULADOS_MOTIVO CHECK (LEN(LTRIM(RTRIM(motivo))) >= 5)
    );
