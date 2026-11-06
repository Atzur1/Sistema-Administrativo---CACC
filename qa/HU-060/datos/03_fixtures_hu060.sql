-- HU-060: precondiciones de las pruebas "las altas del Deportivo impactan en el Administrativo". Se cargan DESPUÉS del reset de
-- HU-059 (qa/HU-059/reset_base.ps1: esquema unificado + migraciones + cuentas ficticias + fixtures 9859xxxx), así que el estado
-- inicial es siempre el mismo.
--
-- Son PRECONDICIONES: aranceles y las identidades que tienen que existir ANTES del alta (persona sin jugador, persona con
-- jugador). Las altas, el staff y las habilitaciones se ejecutan siempre por las APIs reales.
--
-- DNI reservados del rango 9860xxxx. Ningún dato es de una persona real.
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Jerarquía de aranceles (HU-048). Ya existen, del reset de HU-059: Masculino general 1000 e inscripción Masculino 500 (desde 2026-01-01).
--  * Categoría 4 (Pt AFA 2010): arancel propio 1500 desde 2026-01-01 y uno FUTURO de 1800 desde 2027-01-01. La cuota emitida en
--    2026 tiene que llevar 1500: el futuro no está vigente al emitir.
--  * Categoría 3 (Pt AFA 2009): sin arancel propio -> cae al de su género (Masculino 1000).
--  * Categoría 13 (Femenino): arancel propio 800 desde 2026-01-01.
--  * Categoría 12 (Cebollitas) + Femenino: sin arancel de categoría NI de género femenino -> sin arancel aplicable.
--  * Sin inscripción femenina: una jugadora no paga inscripción (HU-033).
-- Un arancel es de categoría O de género, nunca de los dos (CK_ARANCELES_GENERO_O_CATEGORIA): los de categoría van sin género.
INSERT ARANCELES (genero, FK_id_categoria, monto, vigente_desde) VALUES
    (NULL, 4, 1500, '2026-01-01'),
    (NULL, 4, 1800, '2027-01-01'),
    (NULL, 13, 800, '2026-01-01');

-- Identidades que ya existen antes del alta.
INSERT PERSONAS (nombre, apellido, dni, genero, fecha_de_nacimiento) VALUES
    (N'Previa', N'Sin Jugador', '98600002', 'Masculino', '2010-03-03'),   -- H2: PERSONA existente sin JUGADOR (DNI repetido)
    (N'Previo', N'Con Jugador', '98600003', 'Masculino', '2009-04-04');   -- H3: PERSONA + JUGADOR ya existentes (reenvío del alta)
INSERT JUGADORES (FK_id_persona, FK_id_categoria, fecha_alta)
SELECT PK_id_persona, 3, '2026-08-01' FROM PERSONAS WHERE dni = '98600003';
INSERT JUGADORES_CATEGORIAS (FK_id_jugador, FK_id_categoria)
SELECT j.PK_id_jugador, 3 FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni = '98600003';

-- DNI que NO existen y llegan por el alta: 98600001 (nuevo, masculino, cat 4), 98600004 (masculino, cat 3), 98600005 (femenina,
-- cat 13), 98600006 (femenina, cat 12, sin arancel), 98600007 (sin categoría), 98600008 (válido, después del sin arancel),
-- 98600009 (masculino, cat 3, alta con la fecha de negocio fija de la otra rama del calendario: CUO-2).
-- Perfiles no jugadores creados por el Deportivo: 98600010 (DT), 98600011 (Médico), 98600012 (Administrador).

COMMIT;
SELECT CONCAT('hu060: personas=', (SELECT COUNT(*) FROM PERSONAS WHERE dni LIKE '9860____'),
              ' jugadores=', (SELECT COUNT(*) FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.dni LIKE '9860____'),
              ' aranceles=', (SELECT COUNT(*) FROM ARANCELES));
