-- HU-106: un cambio de cuota mensual se confirma despues de ver su impacto, y la auditoria tiene que permitir
-- reconstruir que se aprobo. Hasta ahora el evento de ARANCELES guardaba solo el resultado (genero o categoria,
-- monto y vigencia): no se sabia si venia de un porcentaje, de un monto a sumar o de un monto escrito, cual era
-- el arancel anterior ni a cuantos jugadores alcanzaba.
--
-- Las columnas nuevas guardan lo que el SuperAdmin aprobo al confirmar; el trigger de auditoria las copia en
-- datos_despues del mismo evento (el que ya registra quien, cuando y desde que sistema). El evento existe solo si
-- la operacion se grabo: el trigger corre dentro de la misma transaccion.
--  - tipo_cambio / valor_cambio: Absoluto, Porcentaje (valor = %) o MontoFijo (valor = $ a sumar).
--  - monto_anterior: arancel vigente del destino al confirmar (null si no tenia).
--  - jugadores_alcanzados / diferencia_mensual: impacto en la cuota del primer mes que rige.
--  - impacto: desglose por categoria, genero y monto anterior (JSON).
-- Los aranceles cargados antes de HU-106 quedan con estas columnas en null.
ALTER TABLE dbo.ARANCELES ADD
    tipo_cambio VARCHAR(12) NULL CONSTRAINT CK_ARANCELES_TIPO_CAMBIO CHECK (tipo_cambio IN ('Absoluto', 'Porcentaje', 'MontoFijo')),
    valor_cambio DECIMAL(18, 2) NULL,
    monto_anterior DECIMAL(18, 2) NULL,
    jugadores_alcanzados INT NULL,
    diferencia_mensual DECIMAL(18, 2) NULL,
    impacto NVARCHAR(MAX) NULL CONSTRAINT CK_ARANCELES_IMPACTO_JSON CHECK (impacto IS NULL OR ISJSON(impacto) = 1);

EXEC sys.sp_executesql N'
CREATE OR ALTER TRIGGER dbo.TR_AUDIT_ARANCELES ON dbo.ARANCELES AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @actor INT = TRY_CONVERT(INT, SESSION_CONTEXT(N''idUsuario''));
    DECLARE @email NVARCHAR(254) = (SELECT email FROM dbo.USUARIOS WHERE PK_id_usuario = @actor);
    INSERT dbo.AUDITORIA_CAMBIOS(id_usuario, email_usuario, entidad, id_entidad, accion, datos_antes, datos_despues)
    SELECT @actor, @email, N''ARANCELES'', CONVERT(NVARCHAR(128), COALESCE(i.PK_id_arancel, d.PK_id_arancel)),
        CASE WHEN d.PK_id_arancel IS NULL THEN ''INSERT'' WHEN i.PK_id_arancel IS NULL THEN ''DELETE'' ELSE ''UPDATE'' END,
        CASE WHEN d.PK_id_arancel IS NULL THEN NULL ELSE (SELECT d.genero, d.FK_id_categoria AS idCategoria, d.monto, d.vigente_desde,
            d.tipo_cambio AS tipoCambio, d.valor_cambio AS valorCambio, d.monto_anterior AS montoAnterior,
            d.jugadores_alcanzados AS jugadoresAlcanzados, d.diferencia_mensual AS diferenciaMensual, JSON_QUERY(d.impacto) AS impacto
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END,
        CASE WHEN i.PK_id_arancel IS NULL THEN NULL ELSE (SELECT i.genero, i.FK_id_categoria AS idCategoria, i.monto, i.vigente_desde,
            i.tipo_cambio AS tipoCambio, i.valor_cambio AS valorCambio, i.monto_anterior AS montoAnterior,
            i.jugadores_alcanzados AS jugadoresAlcanzados, i.diferencia_mensual AS diferenciaMensual, JSON_QUERY(i.impacto) AS impacto
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) END
    FROM inserted i FULL OUTER JOIN deleted d ON d.PK_id_arancel = i.PK_id_arancel;
END;';
