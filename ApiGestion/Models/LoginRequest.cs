namespace ApiGestion.Models
{
    public class LoginRequest
    {
        // El DNI de la cuenta o, mientras dura la transición, su correo. No se valida el formato acá: un valor
        // que no es ni lo uno ni lo otro simplemente no encuentra ninguna cuenta.
        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.MaxLength(254)]
        public string Usuario { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.MinLength(1)]
        [System.ComponentModel.DataAnnotations.MaxLength(256)]
        public string Contrasena { get; set; } = string.Empty;
    }
}
