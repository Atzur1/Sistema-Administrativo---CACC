-- Un INSERT que no indique el id lo toma de la secuencia (V20261011_04). Asi tambien funciona la carga
-- masiva de cuotas (INSERT ... SELECT) y cualquier otro sistema que escriba en estas tablas sin calcular
-- el id. Quien siga mandando el id explicito con MAX + 1 puede chocar con la secuencia: todos los que
-- escriban en PAGOS o JUGADORES tienen que dejar de calcularlo.
IF OBJECT_ID(N'dbo.DF_PAGOS_PK_id_pago', N'D') IS NULL
    ALTER TABLE dbo.PAGOS ADD CONSTRAINT DF_PAGOS_PK_id_pago DEFAULT (NEXT VALUE FOR dbo.SEQ_PAGOS) FOR PK_id_pago;

IF OBJECT_ID(N'dbo.DF_JUGADORES_PK_id_jugador', N'D') IS NULL
    ALTER TABLE dbo.JUGADORES ADD CONSTRAINT DF_JUGADORES_PK_id_jugador DEFAULT (NEXT VALUE FOR dbo.SEQ_JUGADORES) FOR PK_id_jugador;
