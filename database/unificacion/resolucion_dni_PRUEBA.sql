/* =====================================================================================
   HU-053 - Resoluciones de conflictos de DNI SOLO PARA PROBAR EL MECANISMO.
   NO SON DECISIONES DEL CLUB. Quedan marcadas es_prueba = 1 y el paso 4 lo informa.
   Para produccion, una persona del club revisa migra.conflicto_dni y carga su propia decision (es_prueba = 0).

   Criterio usado en la prueba (arbitrario, solo para ejercitar la fusion):
     - Duplicados internos de A: queda el registro con pagos; si ninguno tiene, el de id mas bajo.
     - 47265479 (ARRASCAETA, tres fechas de nacimiento distintas): queda el de A; los dos de B se fusionan en el.
   ===================================================================================== */
SET NOCOUNT ON;
GO

DELETE migra.resolucion_dni WHERE es_prueba = 1;

INSERT migra.resolucion_dni (grupo, origen, id_persona_origen, accion, decidido_por, motivo, es_prueba)
VALUES
    ('49404226', 'A', 94, 'CANONICA', N'PRUEBA HU-053 (no es decision del club)', N'Duplicado en A sin pagos: queda el id mas bajo.', 1),
    ('49404226', 'A', 95, 'FUSIONAR', N'PRUEBA HU-053 (no es decision del club)', N'Duplicado en A sin pagos: queda el id mas bajo.', 1),
    ('56893324', 'A', 490, 'CANONICA', N'PRUEBA HU-053 (no es decision del club)', N'Queda el registro que tiene pagos.', 1),
    ('56893324', 'A', 491, 'FUSIONAR', N'PRUEBA HU-053 (no es decision del club)', N'Queda el registro que tiene pagos.', 1),
    ('56893332', 'A', 496, 'CANONICA', N'PRUEBA HU-053 (no es decision del club)', N'Queda el registro que tiene pagos.', 1),
    ('56893332', 'A', 497, 'FUSIONAR', N'PRUEBA HU-053 (no es decision del club)', N'Queda el registro que tiene pagos.', 1),
    ('47265479', 'A', 1, 'CANONICA', N'PRUEBA HU-053 (no es decision del club)', N'Fechas de nacimiento distintas: quedan los datos de A.', 1),
    ('47265479', 'B', 1, 'FUSIONAR', N'PRUEBA HU-053 (no es decision del club)', N'Fechas de nacimiento distintas: quedan los datos de A.', 1),
    ('47265479', 'B', 11, 'FUSIONAR', N'PRUEBA HU-053 (no es decision del club)', N'Fechas de nacimiento distintas: quedan los datos de A.', 1);

PRINT N'Resoluciones de PRUEBA cargadas (es_prueba = 1).';
GO
