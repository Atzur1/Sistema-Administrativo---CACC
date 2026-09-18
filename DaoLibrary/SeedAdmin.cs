using Microsoft.Data.SqlClient;

namespace DaoLibrary
{
    public static class SeedAdmin
    {
        public static void CrearAdminSiNoExiste(string cadenaConexion)
        {
            using (SqlConnection conexion = new SqlConnection(cadenaConexion))
            {
                conexion.Open();

                // 1. Verificar si ya existe el usuario admin
                string checkQuery = "SELECT COUNT(*) FROM USUARIO WHERE email = @email";
                using (SqlCommand checkCmd = new SqlCommand(checkQuery, conexion))
                {
                    checkCmd.Parameters.AddWithValue("@email", "admin@cacc.com");
                    int existe = (int)checkCmd.ExecuteScalar();

                    if (existe > 0)
                    {
                        return; // Ya existe, no hacemos nada
                    }
                }

                // 2. Crear el rol Administrador si no existe
                string rolQuery = @"
                    IF NOT EXISTS (SELECT 1 FROM ROLES WHERE PK_id_rol = 1)
                    INSERT INTO ROLES (PK_id_rol, nombre_rol, Permisos)
                    VALUES (1, 'Administrador General', 'ALL')";
                using (SqlCommand rolCmd = new SqlCommand(rolQuery, conexion))
                {
                    rolCmd.ExecuteNonQuery();
                }

                // 3. Crear la persona asociada al admin
                string personaQuery = @"
                    INSERT INTO PERSONA (genero, fecha_de_nacimiento, Dni, nombre, apellido)
                    OUTPUT INSERTED.PK_id_persona
                    VALUES ('Masculino', '1990-01-01', '00000000', 'Admin', 'CACC')";
                int idPersona;
                using (SqlCommand personaCmd = new SqlCommand(personaQuery, conexion))
                {
                    idPersona = (int)personaCmd.ExecuteScalar();
                }

                // 4. Crear el usuario admin
                string usuarioQuery = @"
                    INSERT INTO USUARIO (PK_id_usuario, FK_id_persona, FK_id_rol, email, contrasenia, activo)
                    VALUES (1, @idPersona, 1, @email, @contrasenia, 1)";
                using (SqlCommand usuarioCmd = new SqlCommand(usuarioQuery, conexion))
                {
                    usuarioCmd.Parameters.AddWithValue("@idPersona", idPersona);
                    usuarioCmd.Parameters.AddWithValue("@email", "admin@cacc.com");
                    usuarioCmd.Parameters.AddWithValue("@contrasenia", "admin123");
                    usuarioCmd.ExecuteNonQuery();
                }
            }
        }
    }
}