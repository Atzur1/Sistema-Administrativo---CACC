-- HU-025: operador que registro el cobro de cada fila de PAGOS. La API lo toma del
-- claim idUsuario del JWT validado; nunca del cuerpo de la solicitud.
-- Nullable a proposito: los pagos anteriores y las cuotas generadas sin cobrar no
-- tienen operador, y no se inventa uno para completarlos.
-- Una sola sentencia (columna + clave foranea) para no referenciar la columna nueva
-- dentro del mismo lote en que se crea.
IF COL_LENGTH(N'dbo.PAGOS', N'FK_id_usuario_registro') IS NULL
    ALTER TABLE dbo.PAGOS ADD FK_id_usuario_registro INT NULL
        CONSTRAINT FK_PAGOS_USUARIO_REGISTRO FOREIGN KEY REFERENCES dbo.USUARIO (PK_id_usuario);
