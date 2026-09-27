USE ClubCamionerosPruebaCuatro;
GO

-- fecha_pago es DATE (solo día, sin hora): suficiente para la lógica de facturación,
-- pero no alcanza para un feed de actividad tipo "hace 5 minutos". Esta columna es
-- puramente informativa (cuándo se grabó la fila en el sistema), separada de fecha_pago
-- para no tocar ningún cálculo de deuda/período que ya depende de esa semántica.
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('PAGOS') AND name = 'fecha_hora_registro')
BEGIN
    ALTER TABLE PAGOS ADD fecha_hora_registro DATETIME2 NULL;
END
GO

-- Los pagos ya existentes nunca tuvieron esta hora registrada: se dejan en NULL a
-- propósito en vez de inventarles una (el feed cae a "Hoy"/"Ayer"/"Hace N días" para
-- estos, con la misma precisión que siempre tuvieron sus datos).
