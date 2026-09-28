-- HU-033 (Becados y Descuentos) — Simplifica el catálogo de motivos de beneficio a dos
-- categorías: "Becado" y "Descuento". Antes TIPO_DESCUENTO tenía tres filas específicas
-- (Beca Completa, Media Beca, Descuento por Hermanos); de ahora en más el motivo solo indica
-- la categoría — el porcentaje o monto fijo puntual lo define el tesorero al asignar el
-- beneficio (JUGADORES_DESCUENTOS.tipo_valor/porcentaje/monto_fijo), sin relación con esta tabla.
--
-- Beca Completa y Media Beca se funden en "Becado" (misma categoría, distinto valor);
-- Descuento por Hermanos pasa a "Descuento". Los beneficios ya asignados a la fila que
-- desaparece se reasignan al id que sobrevive antes de borrarla, para no romper la FK de
-- JUGADORES_DESCUENTOS. Idempotente: si ya se corrió, no encuentra los nombres viejos y no
-- hace nada.
USE ClubCamionerosPruebaCuatro;
GO

DECLARE @idBecaCompleta INT = (SELECT PK_id_descuento FROM TIPO_DESCUENTO WHERE tipo_descuento = 'Beca Completa');
DECLARE @idMediaBeca INT = (SELECT PK_id_descuento FROM TIPO_DESCUENTO WHERE tipo_descuento = 'Media Beca');
DECLARE @idDescuentoHermanos INT = (SELECT PK_id_descuento FROM TIPO_DESCUENTO WHERE tipo_descuento = 'Descuento por Hermanos');

IF @idBecaCompleta IS NOT NULL
BEGIN
    UPDATE TIPO_DESCUENTO SET tipo_descuento = 'Becado' WHERE PK_id_descuento = @idBecaCompleta;

    IF @idMediaBeca IS NOT NULL
    BEGIN
        UPDATE JUGADORES_DESCUENTOS SET FK_id_descuento = @idBecaCompleta WHERE FK_id_descuento = @idMediaBeca;
        DELETE FROM TIPO_DESCUENTO WHERE PK_id_descuento = @idMediaBeca;
    END
END

IF @idDescuentoHermanos IS NOT NULL
BEGIN
    UPDATE TIPO_DESCUENTO SET tipo_descuento = 'Descuento' WHERE PK_id_descuento = @idDescuentoHermanos;
END
GO
