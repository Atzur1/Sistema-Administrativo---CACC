using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;
using System.Security.Claims;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// HU-021: licencia Community de QuestPDF (gratuita para orgs de este tamaño).
QuestPDF.Settings.License = LicenseType.Community;

builder.Services.AddControllers();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("account-email", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 4, Window = TimeSpan.FromMinutes(15), QueueLimit = 0 }));
});

// 1. AGREGA ESTA POLÍTICA DE CORS (Permite conexiones desde Angular)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular",
        policy =>
        {
            var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                ?? (builder.Environment.IsDevelopment() ? new[] { "http://localhost:4200" } : Array.Empty<string>());
            policy.WithOrigins(origins)
                .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
                .WithHeaders("Authorization", "Content-Type")
                // HU-021: sin esto el navegador recibe el Content-Disposition
                // en la respuesta pero el JS del frontend no puede leerlo (CORS
                // solo expone unos pocos headers "seguros" por default), y la
                // descarga se queda sin el nombre de archivo que puso el backend.
                .WithExposedHeaders("Content-Disposition");
        });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddScoped<DaoLibrary.AuthDao>(provider =>
    new DaoLibrary.AuthDao(builder.Configuration.GetConnectionString("ConexionSQL") ?? ""));
builder.Services.AddScoped<DaoLibrary.AuditDao>(provider =>
    new DaoLibrary.AuditDao(builder.Configuration.GetConnectionString("ConexionSQL") ?? ""));
builder.Services.AddScoped<DaoLibrary.AccountAccessDao>(provider =>
    new DaoLibrary.AccountAccessDao(builder.Configuration.GetConnectionString("ConexionSQL") ?? ""));
builder.Services.AddSingleton<ApiGestion.Services.EmailLinkSender>();

// Cuotas y pagos: DAO + runner transaccional + servicio de negocio
builder.Services.AddScoped<DaoLibrary.IPagosDao>(provider =>
    new DaoLibrary.PagosDao(builder.Configuration.GetConnectionString("ConexionSQL") ?? ""));
builder.Services.AddScoped<DaoLibrary.ISqlTransactionRunner>(provider =>
    new DaoLibrary.SqlTransactionRunner(builder.Configuration.GetConnectionString("ConexionSQL") ?? ""));
builder.Services.AddScoped<ServiceLibrary.IPagosService, ServiceLibrary.PagosService>();

builder.Services.AddScoped<DaoLibrary.IJugadoresDao>(provider =>
    new DaoLibrary.JugadoresDao(builder.Configuration.GetConnectionString("ConexionSQL") ?? ""));

builder.Services.AddScoped<DaoLibrary.IEstadisticasDao>(provider =>
    new DaoLibrary.EstadisticasDao(builder.Configuration.GetConnectionString("ConexionSQL") ?? ""));

// Categorías: catálogo para el selector de Deudas y Morosidad (HU-020)
builder.Services.AddScoped<DaoLibrary.ICategoriasDao>(provider =>
    new DaoLibrary.CategoriasDao(builder.Configuration.GetConnectionString("ConexionSQL") ?? ""));

// Aranceles: DAO + servicio de negocio
builder.Services.AddScoped<DaoLibrary.IArancelesDao>(provider =>
    new DaoLibrary.ArancelesDao(builder.Configuration.GetConnectionString("ConexionSQL") ?? ""));
builder.Services.AddScoped<ServiceLibrary.IArancelesService, ServiceLibrary.ArancelesService>();

// Inscripción única de la rama masculina (HU-033)
builder.Services.AddScoped<DaoLibrary.EnrollmentFeeDAO>(provider =>
    new DaoLibrary.EnrollmentFeeDAO(builder.Configuration.GetConnectionString("ConexionSQL") ?? ""));
builder.Services.AddScoped<DaoLibrary.PlayerDAO>(provider =>
    new DaoLibrary.PlayerDAO(builder.Configuration.GetConnectionString("ConexionSQL") ?? ""));
builder.Services.AddScoped<DaoLibrary.EnrollmentDAO>(provider =>
    new DaoLibrary.EnrollmentDAO(builder.Configuration.GetConnectionString("ConexionSQL") ?? ""));

// Becados y descuentos
builder.Services.AddScoped<DaoLibrary.DiscountDao>(provider =>
    new DaoLibrary.DiscountDao(builder.Configuration.GetConnectionString("ConexionSQL") ?? ""));

// 4. NUEVO: Configuración de autenticación JWT
var jwtKey = builder.Configuration["Jwt:Key"];
var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.StartsWith("REEMPLAZAR", StringComparison.OrdinalIgnoreCase) || Encoding.UTF8.GetByteCount(jwtKey) < 32 ||
    string.IsNullOrWhiteSpace(jwtIssuer) || string.IsNullOrWhiteSpace(jwtAudience))
    throw new InvalidOperationException("La configuración Jwt:Key (mínimo 32 bytes), Jwt:Issuer y Jwt:Audience es obligatoria.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var idClaim = context.Principal?.FindFirst("idUsuario")?.Value;
                var roleClaim = context.Principal?.FindFirst(ClaimTypes.Role)?.Value;
                var versionClaim = context.Principal?.FindFirst("tokenVersion")?.Value;
                if (!int.TryParse(idClaim, out var id) || !int.TryParse(roleClaim, out var role) || !int.TryParse(versionClaim, out var tokenVersion))
                {
                    context.Fail("Token inválido.");
                    return;
                }
                var authDao = context.HttpContext.RequestServices.GetRequiredService<DaoLibrary.AuthDao>();
                if (!authDao.UsuarioActivoConRol(id, role, tokenVersion)) context.Fail("Usuario inactivo o permisos revocados.");
                await Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// 2. ACTIVA EL CORS AQUÍ (Antes de UseAuthorization y MapControllers)
app.UseCors("AllowAngular");
app.UseRateLimiter();

// 4. NUEVO: tiene que ir ANTES de UseAuthorization
app.UseAuthentication();
app.UseMiddleware<ApiGestion.AuditActorMiddleware>();
app.UseAuthorization();

app.MapControllers();

app.Run();

