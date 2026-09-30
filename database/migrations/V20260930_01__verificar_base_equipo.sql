-- Base comun del equipo: las migraciones siguientes suponen que cada base ya tiene
-- los scripts manuales historicos de database/ hasta V20260929_07 inclusive.
-- Solo lee el esquema. Si falta algo, detiene el inicio indicando que script aplicar
-- manualmente (una vez, con respaldo previo) y no queda registrada.
DECLARE @faltantes TABLE (script varchar(80) NOT NULL, objeto nvarchar(200) NOT NULL);

INSERT INTO @faltantes (script, objeto)
SELECT r.script, r.objeto
FROM (VALUES
    ('V20260927_01__inscripcion_jugadores', N'tabla ARANCELES_INSCRIPCION', CASE WHEN OBJECT_ID(N'dbo.ARANCELES_INSCRIPCION', N'U') IS NULL THEN 1 ELSE 0 END),
    ('V20260927_01__inscripcion_jugadores', N'columna PAGOS.concepto', CASE WHEN COL_LENGTH(N'dbo.PAGOS', N'concepto') IS NULL THEN 1 ELSE 0 END),
    ('V20260927_01__inscripcion_jugadores', N'columna JUGADORES.fecha_alta', CASE WHEN COL_LENGTH(N'dbo.JUGADORES', N'fecha_alta') IS NULL THEN 1 ELSE 0 END),
    ('V20260927_01__simplifica_catalogo_tipo_descuento', N'catalogo TIPO_DESCUENTO Becado/Descuento', CASE WHEN EXISTS (SELECT 1 FROM dbo.TIPO_DESCUENTO WHERE tipo_descuento IN (N'Beca Completa', N'Media Beca', N'Descuento por Hermanos')) THEN 1 ELSE 0 END),
    ('V20260928_01__agrega_fecha_cancelacion_descuentos', N'columna JUGADORES_DESCUENTOS.fecha_cancelacion', CASE WHEN COL_LENGTH(N'dbo.JUGADORES_DESCUENTOS', N'fecha_cancelacion') IS NULL THEN 1 ELSE 0 END),
    ('V20260929_01__agrega_fecha_hora_registro_pagos', N'columna PAGOS.fecha_hora_registro', CASE WHEN COL_LENGTH(N'dbo.PAGOS', N'fecha_hora_registro') IS NULL THEN 1 ELSE 0 END),
    ('V20260929_02__endurece_autenticacion_y_operaciones', N'columna USUARIO.password_hash', CASE WHEN COL_LENGTH(N'dbo.USUARIO', N'password_hash') IS NULL THEN 1 ELSE 0 END),
    ('V20260929_02__endurece_autenticacion_y_operaciones', N'indice UX_PAGOS_CuotaPendiente_Jugador_Periodo', CASE WHEN NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.PAGOS') AND name = N'UX_PAGOS_CuotaPendiente_Jugador_Periodo') THEN 1 ELSE 0 END),
    ('V20260929_03__auditoria_integral_administrativa', N'tabla AUDITORIA_CAMBIOS', CASE WHEN OBJECT_ID(N'dbo.AUDITORIA_CAMBIOS', N'U') IS NULL THEN 1 ELSE 0 END),
    ('V20260929_03__auditoria_integral_administrativa', N'trigger TR_AUDIT_PAGOS', CASE WHEN OBJECT_ID(N'dbo.TR_AUDIT_PAGOS', N'TR') IS NULL THEN 1 ELSE 0 END),
    ('V20260929_04__invitacion_y_recuperacion_cuentas', N'tabla TOKEN_ACCESO_CUENTA', CASE WHEN OBJECT_ID(N'dbo.TOKEN_ACCESO_CUENTA', N'U') IS NULL THEN 1 ELSE 0 END),
    ('V20260929_04__invitacion_y_recuperacion_cuentas', N'columnas USUARIO.activacion_pendiente/token_version', CASE WHEN COL_LENGTH(N'dbo.USUARIO', N'activacion_pendiente') IS NULL OR COL_LENGTH(N'dbo.USUARIO', N'token_version') IS NULL THEN 1 ELSE 0 END),
    ('V20260929_05__identidad_del_operador_en_auditoria', N'columnas USUARIO.nombre/apellido/dni', CASE WHEN COL_LENGTH(N'dbo.USUARIO', N'nombre') IS NULL OR COL_LENGTH(N'dbo.USUARIO', N'apellido') IS NULL OR COL_LENGTH(N'dbo.USUARIO', N'dni') IS NULL THEN 1 ELSE 0 END),
    ('V20260929_06__responsable_visible_en_operaciones', N'trigger TR_AUDITORIA_IDENTIDAD_ACTOR', CASE WHEN OBJECT_ID(N'dbo.TR_AUDITORIA_IDENTIDAD_ACTOR', N'TR') IS NULL THEN 1 ELSE 0 END),
    ('V20260929_07__acceso_administrador_rol_portal', N'columnas USUARIO.acceso_portal/rol_portal', CASE WHEN COL_LENGTH(N'dbo.USUARIO', N'acceso_portal') IS NULL OR COL_LENGTH(N'dbo.USUARIO', N'rol_portal') IS NULL THEN 1 ELSE 0 END)
) AS r (script, objeto, falta)
WHERE r.falta = 1;

IF EXISTS (SELECT 1 FROM @faltantes)
BEGIN
    DECLARE @detalle nvarchar(1800) = (
        SELECT STRING_AGG(CONVERT(nvarchar(max), porScript.script + N'.sql (' + porScript.objetos + N')'), N'; ')
            WITHIN GROUP (ORDER BY porScript.script)
        FROM (
            SELECT script, STRING_AGG(CONVERT(nvarchar(max), objeto), N', ') AS objetos
            FROM @faltantes
            GROUP BY script
        ) AS porScript);
    DECLARE @mensaje nvarchar(2048) = LEFT(
        N'La base no tiene la estructura comun del equipo. Respaldarla y aplicar manualmente, en orden, los scripts de database/: '
        + @detalle, 2048);
    THROW 51100, @mensaje, 1;
END;
