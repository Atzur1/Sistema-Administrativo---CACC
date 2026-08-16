using Microsoft.Data.SqlClient;
using EntityLibrary;

namespace DaoLibrary
{
    public class AuthDao
    {
        private readonly string _cadenaConexion;

        // Cuando la API llame a este DAO, le pasará la dirección de la base de datos
        public AuthDao(string cadenaConexion)
        {
            _cadenaConexion = cadenaConexion;
        }

        public Usuario? ValidarLogin(string email, string contrasenia)
        {
            Usuario? usuarioEncontrado = null;

            // Abrimos la conexión a SQL Server
            using (SqlConnection conexion = new SqlConnection(_cadenaConexion))
            {
                conexion.Open();
                
                // Armamos la consulta de forma súper segura para evitar hackeos (Inyección SQL)
                string query = "SELECT PK_id_usuario, email, contrasenia, FK_id_rol FROM USUARIO WHERE email = @email AND contrasenia = @contrasenia AND activo = 1";
                
                using (SqlCommand comando = new SqlCommand(query, conexion))
                {
                    comando.Parameters.AddWithValue("@email", email);
                    comando.Parameters.AddWithValue("@contrasenia", contrasenia);

                    using (SqlDataReader reader = comando.ExecuteReader())
                    {
                        if (reader.Read()) // Si encontró una fila, las credenciales son correctas
                        {
                            usuarioEncontrado = new Usuario
                            {
                                IdUsuario = Convert.ToInt32(reader["PK_id_usuario"]),
                                Email = reader["email"].ToString() ?? "",
                                Contrasenia = reader["contrasenia"].ToString() ?? "",
                                IdRol = reader["FK_id_rol"] != DBNull.Value ? Convert.ToInt32(reader["FK_id_rol"]) : 0
                            };
                        }
                    }
                }
            }
            
            return usuarioEncontrado; // Devuelve el usuario si existe, o 'null' si falló el login
        }
    }
}