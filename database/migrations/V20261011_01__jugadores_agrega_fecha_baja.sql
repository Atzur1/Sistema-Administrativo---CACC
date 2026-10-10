-- Baja de jugadores: hasta ahora no existia forma de registrar que un jugador dejo el club, y
-- GeneradorCuotasMensuales le seguia emitiendo cuota todos los meses (deuda que nunca se iba a cobrar).
--
-- fecha_baja NULL = jugador activo. Con fecha, se le emiten cuotas hasta el mes de la baja inclusive
-- (mismo criterio que el alta: estar en el club algun dia del mes alcanza para la cuota de ese mes).
-- Columna nueva y opcional: no cambia nada para el otro equipo ni para los registros existentes.
IF COL_LENGTH('dbo.JUGADORES', 'fecha_baja') IS NULL
    ALTER TABLE dbo.JUGADORES ADD fecha_baja DATE NULL;
