/*
  Aplicar manualmente sobre la base de datos luego de respaldarla.
  La API requiere password_hash antes de volver a aceptar inicios de sesión.
  contrasenia queda temporalmente NULL-able para permitir actualización gradual
  al iniciar sesión correctamente con la contraseña heredada.
*/
IF COL_LENGTH('dbo.USUARIO', 'password_hash') IS NULL
    ALTER TABLE dbo.USUARIO ADD password_hash NVARCHAR(512) NULL;
GO

IF COL_LENGTH('dbo.USUARIO', 'contrasenia') IS NOT NULL
    ALTER TABLE dbo.USUARIO ALTER COLUMN contrasenia NVARCHAR(MAX) NULL;
GO

IF EXISTS (
    SELECT 1 FROM dbo.PAGOS
    WHERE estado = 0 AND concepto = 'Cuota' AND fecha_vencimiento IS NOT NULL
    GROUP BY FK_id_jugador, fecha_vencimiento, concepto HAVING COUNT(*) > 1
)
    THROW 51000, 'Hay cuotas pendientes duplicadas; corregirlas antes de crear el índice de seguridad.', 1;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.PAGOS') AND name = 'UX_PAGOS_CuotaPendiente_Jugador_Periodo')
    CREATE UNIQUE INDEX UX_PAGOS_CuotaPendiente_Jugador_Periodo
    ON dbo.PAGOS (FK_id_jugador, fecha_vencimiento, concepto)
    WHERE estado = 0 AND concepto = 'Cuota' AND fecha_vencimiento IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.PAGOS') AND name = 'IX_PAGOS_Jugador_Concepto_Periodo_Estado')
    CREATE INDEX IX_PAGOS_Jugador_Concepto_Periodo_Estado
    ON dbo.PAGOS (FK_id_jugador, concepto, fecha_vencimiento, estado);
GO

IF EXISTS (
    SELECT 1 FROM dbo.PAGOS
    WHERE estado = 0 AND concepto = 'Inscripcion'
    GROUP BY FK_id_jugador HAVING COUNT(*) > 1
)
    THROW 51001, 'Hay inscripciones pendientes duplicadas; corregirlas antes de crear el índice de seguridad.', 1;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.PAGOS') AND name = 'UX_PAGOS_InscripcionPendiente_Jugador')
    CREATE UNIQUE INDEX UX_PAGOS_InscripcionPendiente_Jugador
    ON dbo.PAGOS (FK_id_jugador)
    WHERE estado = 0 AND concepto = 'Inscripcion';
GO
