-- Prueba de los constraints de V20260915_01. No deja datos: todo va y vuelve.
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

DECLARE @jugador INT = (SELECT MIN(PK_id_jugador) FROM JUGADORES);
DECLARE @otro    INT = (SELECT MIN(PK_id_jugador) FROM JUGADORES WHERE PK_id_jugador > @jugador);
DECLARE @beca    INT = (SELECT PK_id_descuento FROM TIPO_DESCUENTO WHERE tipo_descuento = 'Beca Completa');
DECLARE @media   INT = (SELECT PK_id_descuento FROM TIPO_DESCUENTO WHERE tipo_descuento = 'Media Beca');

PRINT 'Jugadores de prueba: ' + CAST(@jugador AS VARCHAR) + ' y ' + CAST(@otro AS VARCHAR);
PRINT '';

BEGIN TRANSACTION;

-- CASO 1: alta porcentual valida -> DEBE PASAR
BEGIN TRY
    INSERT INTO JUGADORES_DESCUENTOS (FK_id_jugador, FK_id_descuento, estado_activo, tipo_valor, porcentaje, monto_fijo, fecha_inicio, fecha_fin)
    VALUES (@jugador, @media, 1, '%', 50.00, NULL, '2026-09-15', NULL);
    PRINT 'CASO 1 (alta porcentual 50%)            -> OK  (insertado)';
END TRY
BEGIN CATCH
    PRINT 'CASO 1 (alta porcentual 50%)            -> FALLO INESPERADO: ' + ERROR_MESSAGE();
END CATCH

-- CASO 2: alta monto fijo para OTRO jugador -> DEBE PASAR
BEGIN TRY
    INSERT INTO JUGADORES_DESCUENTOS (FK_id_jugador, FK_id_descuento, estado_activo, tipo_valor, porcentaje, monto_fijo, fecha_inicio, fecha_fin)
    VALUES (@otro, @beca, 1, '$', NULL, 15000.00, '2026-09-15', NULL);
    PRINT 'CASO 2 (alta monto fijo $15000)         -> OK  (insertado)';
END TRY
BEGIN CATCH
    PRINT 'CASO 2 (alta monto fijo $15000)         -> FALLO INESPERADO: ' + ERROR_MESSAGE();
END CATCH

-- CASO 3: SEGUNDA activa para el MISMO jugador -> DEBE SER RECHAZADA
BEGIN TRY
    INSERT INTO JUGADORES_DESCUENTOS (FK_id_jugador, FK_id_descuento, estado_activo, tipo_valor, porcentaje, monto_fijo, fecha_inicio, fecha_fin)
    VALUES (@jugador, @beca, 1, '%', 100.00, NULL, '2026-09-15', NULL);
    PRINT 'CASO 3 (segunda activa mismo jugador)   -> FALLO: se permitio duplicado!';
END TRY
BEGIN CATCH
    PRINT 'CASO 3 (segunda activa mismo jugador)   -> BLOQUEADO correctamente';
END CATCH

-- CASO 4: porcentaje Y monto fijo a la vez -> DEBE SER RECHAZADA
BEGIN TRY
    INSERT INTO JUGADORES_DESCUENTOS (FK_id_jugador, FK_id_descuento, estado_activo, tipo_valor, porcentaje, monto_fijo, fecha_inicio, fecha_fin)
    VALUES (@otro + 1, @beca, 1, '%', 50.00, 15000.00, '2026-09-15', NULL);
    PRINT 'CASO 4 (porcentaje + monto juntos)      -> FALLO: se permitio mezcla!';
END TRY
BEGIN CATCH
    PRINT 'CASO 4 (porcentaje + monto juntos)      -> BLOQUEADO correctamente';
END CATCH

-- CASO 5: porcentaje 0 -> DEBE SER RECHAZADA
BEGIN TRY
    INSERT INTO JUGADORES_DESCUENTOS (FK_id_jugador, FK_id_descuento, estado_activo, tipo_valor, porcentaje, monto_fijo, fecha_inicio, fecha_fin)
    VALUES (@otro + 2, @beca, 1, '%', 0, NULL, '2026-09-15', NULL);
    PRINT 'CASO 5 (porcentaje 0)                   -> FALLO: se permitio 0!';
END TRY
BEGIN CATCH
    PRINT 'CASO 5 (porcentaje 0)                   -> BLOQUEADO correctamente';
END CATCH

-- CASO 6: porcentaje 101 -> DEBE SER RECHAZADA
BEGIN TRY
    INSERT INTO JUGADORES_DESCUENTOS (FK_id_jugador, FK_id_descuento, estado_activo, tipo_valor, porcentaje, monto_fijo, fecha_inicio, fecha_fin)
    VALUES (@otro + 3, @beca, 1, '%', 101, NULL, '2026-09-15', NULL);
    PRINT 'CASO 6 (porcentaje 101)                 -> FALLO: se permitio > 100!';
END TRY
BEGIN CATCH
    PRINT 'CASO 6 (porcentaje 101)                 -> BLOQUEADO correctamente';
END CATCH

-- CASO 7: monto fijo negativo -> DEBE SER RECHAZADA
BEGIN TRY
    INSERT INTO JUGADORES_DESCUENTOS (FK_id_jugador, FK_id_descuento, estado_activo, tipo_valor, porcentaje, monto_fijo, fecha_inicio, fecha_fin)
    VALUES (@otro + 4, @beca, 1, '$', NULL, -500, '2026-09-15', NULL);
    PRINT 'CASO 7 (monto fijo negativo)            -> FALLO: se permitio negativo!';
END TRY
BEGIN CATCH
    PRINT 'CASO 7 (monto fijo negativo)            -> BLOQUEADO correctamente';
END CATCH

-- CASO 8: tras CANCELAR la activa, se puede asignar otra -> DEBE PASAR
BEGIN TRY
    UPDATE JUGADORES_DESCUENTOS SET estado_activo = 0 WHERE FK_id_jugador = @jugador AND estado_activo = 1;
    INSERT INTO JUGADORES_DESCUENTOS (FK_id_jugador, FK_id_descuento, estado_activo, tipo_valor, porcentaje, monto_fijo, fecha_inicio, fecha_fin)
    VALUES (@jugador, @beca, 1, '%', 100.00, NULL, '2026-09-15', NULL);
    PRINT 'CASO 8 (reasignar tras cancelar)        -> OK  (permitido)';
END TRY
BEGIN CATCH
    PRINT 'CASO 8 (reasignar tras cancelar)        -> FALLO INESPERADO: ' + ERROR_MESSAGE();
END CATCH

-- CASO 9: tipo_valor invalido -> DEBE SER RECHAZADA
BEGIN TRY
    INSERT INTO JUGADORES_DESCUENTOS (FK_id_jugador, FK_id_descuento, estado_activo, tipo_valor, porcentaje, monto_fijo, fecha_inicio, fecha_fin)
    VALUES (@otro + 5, @beca, 1, 'EUR', NULL, 100, '2026-09-15', NULL);
    PRINT 'CASO 9 (tipo_valor invalido)            -> FALLO: se permitio!';
END TRY
BEGIN CATCH
    PRINT 'CASO 9 (tipo_valor invalido)            -> BLOQUEADO correctamente';
END CATCH

ROLLBACK TRANSACTION;

PRINT '';
PRINT 'Rollback hecho: la base queda como estaba.';
SELECT COUNT(*) AS filas_restantes FROM JUGADORES_DESCUENTOS;
