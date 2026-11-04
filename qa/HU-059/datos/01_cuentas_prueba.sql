-- HU-059: cuentas FICTICIAS de prueba (clave de todas: la de pruebas del banco cacc-unificacion, ver database/InstructivoEquipo.md).
-- Copia de cacc-unificacion/pruebas/seed_cuentas_prueba.sql para que el entorno no dependa de otra carpeta. El hash es de una
-- clave de prueba publicada; estas cuentas solo existen en bases locales de prueba.
SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON;
DECLARE @h varchar(255) = '$2a$12$gW.EMuN8Qg3OKOM/4a9wZ.GzqA.UbaETE.dc2h.pLM4IqiMgPH1qe';
-- DNI 20111222 SuperAdmin del Administrativo
INSERT PERSONAS(nombre,apellido,dni) VALUES (N'Super',N'Admin Prueba','20111222');
INSERT USUARIOS(FK_id_persona,FK_id_rol,email,contrasenia,activo,acceso_portal,rol_portal) VALUES (SCOPE_IDENTITY(),1,'superadmin.prueba@ejemplo.com',@h,1,1,1);
-- DNI 30000003 Medico (solo Deportivo)
INSERT PERSONAS(nombre,apellido,dni) VALUES (N'Maria',N'Medica','30000003');
INSERT USUARIOS(FK_id_persona,FK_id_rol,email,contrasenia,activo) VALUES (SCOPE_IDENTITY(),3,'medica.prueba@ejemplo.com',@h,1);
-- DNI 30000004 Director Tecnico de la categoria 3 (solo Deportivo)
INSERT PERSONAS(nombre,apellido,dni) VALUES (N'Diego',N'Director','30000004');
INSERT USUARIOS(FK_id_persona,FK_id_rol,email,contrasenia,activo) VALUES (SCOPE_IDENTITY(),4,'dt.prueba@ejemplo.com',@h,1);
DECLARE @u INT = (SELECT PK_id_usuario FROM USUARIOS WHERE email='dt.prueba@ejemplo.com');
INSERT STAFF(FK_id_usuario) VALUES (@u); INSERT STAFF_CATEGORIAS(FK_id_staff,FK_id_categoria) VALUES (SCOPE_IDENTITY(),3);
-- DNI 30000002 Administrativo (rol 2 de ROLES) candidato a habilitar en el portal: sin acceso todavia, sin contrasena
INSERT PERSONAS(nombre,apellido,dni) VALUES (N'Ana',N'Administrativa','30000002');
INSERT USUARIOS(FK_id_persona,FK_id_rol,email,contrasenia,activo) VALUES (SCOPE_IDENTITY(),2,'ana.prueba@ejemplo.com',NULL,1);
SELECT 'usuarios='+CAST(COUNT(*) AS varchar) FROM USUARIOS;
-- DNI 30000005 Administrador (rol de portal 2) ya habilitado y con contrasena: lo necesitan las pruebas del proyecto del Administrativo (dotnet test)
INSERT PERSONAS(nombre,apellido,dni) VALUES (N'Bruno',N'Administrador','30000005');
INSERT USUARIOS(FK_id_persona,FK_id_rol,email,contrasenia,activo,acceso_portal,rol_portal,activacion_pendiente) VALUES (SCOPE_IDENTITY(),2,'bruno.prueba@ejemplo.com',@h,1,1,2,0);
