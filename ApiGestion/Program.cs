using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;
using System.Security.Claims;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// HU-021: licencia Community de QuestPDF (gratuita para orgs de este tamaño).
QuestPDF.Settings.License = LicenseType.Community;

// Una sola lectura de la cadena de conexión para todos los DAO.
var conexionSql = builder.Configuration.GetConnectionString("ConexionSQL") ?? "";

// Detrás de un proxy (Railway, un balanceador, IIS con ARR) la IP que ve la API es la del proxy: sin esto
// el límite de intentos de login por IP se compartía entre TODOS los usuarios, y la redirección a HTTPS
// no sabía que el pedido original ya era HTTPS. Opt-in (ForwardedHeaders:Enabled=true) porque confiar en
// X-Forwarded-For sin un proxy delante permitiría falsear la IP y saltear el límite.
var usarCabecerasDeProxy = builder.Configuration.GetValue<bool>("ForwardedHeaders:Enabled");
if (usarCabecerasDeProxy)
{
    builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
            | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
        // Solo el salto del proxy de la plataforma, que no tiene una IP fija conocida.
        options.ForwardLimit = 1;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

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
                ?? (builder.Environment.IsDevelopment()
                    // 4300: Portal Deportivo en desarrollo (al cerrar sesión allá, invalida el token acá).
                    ? new[] { "http://localhost:4200", "http://127.0.0.1:4200", "http://localhost:4300", "http://127.0.0.1:4300" }
                    : Array.Empty<string>());
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
builder.Services.AddSwaggerGen(opciones =>
{
    // Contrato de integración con el Portal Deportivo (HU-054): solo las rutas versionadas /api/integracion/v1/...
    opciones.SwaggerDoc("integracion-v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "API de integración con el Portal Deportivo",
        Version = "v1",
        Description = "Estado de habilitación de los jugadores para el QR de ingreso y los listados del Portal Deportivo. " +
            "Autenticación: el token (JWT) del usuario, el mismo en los dos portales. La respuesta nunca incluye montos."
    });
    opciones.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "Portal Administrativo", Version = "v1" });
    opciones.DocInclusionPredicate((documento, api) =>
    {
        bool esIntegracion = api.RelativePath?.StartsWith("api/integracion/", StringComparison.OrdinalIgnoreCase) == true;
        return documento == "integracion-v1" ? esIntegracion : !esIntegracion;
    });
    opciones.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Token del usuario (el que entrega el login del Portal Administrativo)."
    });
    opciones.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddScoped<DaoLibrary.AuthDao>(provider =>
    new DaoLibrary.AuthDao(conexionSql));
builder.Services.AddScoped<DaoLibrary.IAuditDao>(provider =>
    new DaoLibrary.AuditDao(conexionSql));
builder.Services.AddScoped<DaoLibrary.AccountAccessDao>(provider =>
    new DaoLibrary.AccountAccessDao(conexionSql));
builder.Services.AddScoped<DaoLibrary.IUsuariosPortalDao>(provider =>
    new DaoLibrary.UsuariosPortalDao(conexionSql));
// Solo lo usa el arranque: avisa si el usuario de la base puede borrar la auditoría.
builder.Services.AddSingleton(new DaoLibrary.DatabasePermissionsDao(conexionSql));
builder.Services.AddSingleton<ApiGestion.Services.EmailLinkSender>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<DaoLibrary.IIntentosLoginStore>(_ => new DaoLibrary.IntentosLoginDao(conexionSql));
builder.Services.AddSingleton<ApiGestion.Services.LoginThrottle>();

// Cuotas y pagos: DAO + transacción de pagos (también DAO) + servicio de negocio
builder.Services.AddScoped<DaoLibrary.IPagosDao>(provider =>
    new DaoLibrary.PagosDao(conexionSql));
builder.Services.AddScoped<DaoLibrary.IPaymentTransactionDao>(provider =>
    new DaoLibrary.PaymentTransactionDao(conexionSql));
builder.Services.AddScoped<ServiceLibrary.IPagosService, ServiceLibrary.PagosService>();

builder.Services.AddScoped<DaoLibrary.IJugadoresDao>(provider =>
    new DaoLibrary.JugadoresDao(conexionSql));

builder.Services.AddScoped<DaoLibrary.IEstadisticasDao>(provider =>
    new DaoLibrary.EstadisticasDao(conexionSql));

// Estado de deuda de los jugadores para el Portal Deportivo (QR de ingreso y listados)
builder.Services.AddScoped<DaoLibrary.IEstadoDeudaDao>(provider =>
    new DaoLibrary.EstadoDeudaDao(conexionSql));

// Categorías: catálogo para el selector de Deudas y Morosidad (HU-020)
builder.Services.AddScoped<DaoLibrary.ICategoriasDao>(provider =>
    new DaoLibrary.CategoriasDao(conexionSql));

// Recaudación mensual por medio de pago (HU-018)
builder.Services.AddScoped<DaoLibrary.IRecaudacionDao>(provider =>
    new DaoLibrary.RecaudacionDao(conexionSql));

// Aranceles: DAO + transacción de aranceles (también DAO) + servicio de negocio
builder.Services.AddScoped<DaoLibrary.IArancelesDao>(provider =>
    new DaoLibrary.ArancelesDao(conexionSql));
builder.Services.AddScoped<DaoLibrary.ITariffTransactionDao>(provider =>
    new DaoLibrary.TariffTransactionDao(conexionSql));
builder.Services.AddScoped<ServiceLibrary.IArancelesService>(provider => new ServiceLibrary.ArancelesService(
    provider.GetRequiredService<DaoLibrary.IArancelesDao>(),
    provider.GetRequiredService<DaoLibrary.ITariffTransactionDao>(),
    provider.GetRequiredService<DaoLibrary.ICategoriasDao>()));

// Aviso de meses que quedaron con cuotas sin generar (Resumen General, solo SuperAdmin).
builder.Services.AddScoped<DaoLibrary.ICuotasFaltantesDao>(provider =>
    new DaoLibrary.CuotasFaltantesDao(conexionSql));
builder.Services.AddScoped<ServiceLibrary.ICuotasFaltantesService>(provider =>
    new ServiceLibrary.CuotasFaltantesService(provider.GetRequiredService<DaoLibrary.ICuotasFaltantesDao>()));

// Cuotas mensuales automáticas: repite el arancel vigente de cada jugador mes a mes.
// Se puede apagar con GeneracionCuotas:Habilitada=false.
if (builder.Configuration.GetValue<bool?>("GeneracionCuotas:Habilitada") ?? true)
    builder.Services.AddHostedService<ApiGestion.Services.GeneradorCuotasMensuales>();

// Inscripción única de la rama masculina (HU-033)
builder.Services.AddScoped<DaoLibrary.EnrollmentFeeDAO>(provider =>
    new DaoLibrary.EnrollmentFeeDAO(conexionSql));
builder.Services.AddScoped<DaoLibrary.PlayerDAO>(provider =>
    new DaoLibrary.PlayerDAO(conexionSql));
builder.Services.AddScoped<DaoLibrary.EnrollmentDAO>(provider =>
    new DaoLibrary.EnrollmentDAO(conexionSql));
builder.Services.AddScoped<ServiceLibrary.IInscripcionesService>(provider => new ServiceLibrary.InscripcionesService(
    provider.GetRequiredService<DaoLibrary.EnrollmentDAO>(),
    provider.GetRequiredService<DaoLibrary.EnrollmentFeeDAO>(),
    provider.GetRequiredService<DaoLibrary.IJugadoresDao>()));
builder.Services.AddScoped<ServiceLibrary.IJugadoresAltaService>(provider => new ServiceLibrary.JugadoresAltaService(
    provider.GetRequiredService<DaoLibrary.PlayerDAO>(),
    provider.GetRequiredService<DaoLibrary.ICategoriasDao>(),
    provider.GetRequiredService<DaoLibrary.EnrollmentFeeDAO>(),
    provider.GetRequiredService<DaoLibrary.IArancelesDao>()));

// Becados y descuentos
builder.Services.AddScoped<DaoLibrary.DiscountDao>(provider =>
    new DaoLibrary.DiscountDao(conexionSql));
builder.Services.AddScoped<ServiceLibrary.IBeneficiosService>(provider => new ServiceLibrary.BeneficiosService(
    provider.GetRequiredService<DaoLibrary.DiscountDao>(),
    provider.GetRequiredService<DaoLibrary.IJugadoresDao>()));

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
                var roleClaim = context.Principal?.FindFirst("rol_portal")?.Value;
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

// Modo "solo migrar" para los despliegues de producción: aplica las migraciones con la cuenta ADMINISTRADORA de
// la base y termina, sin levantar la API. En producción la API trabaja con el usuario cacc_api, que no puede
// cambiar la estructura (ver database/produccion). Uso:
//   dotnet ApiGestion.dll --DatabaseMigrations:SoloMigrar=true --ConnectionStrings:ConexionMigraciones="<cadena con la cuenta administradora>"
if (app.Configuration.GetValue<bool>("DatabaseMigrations:SoloMigrar"))
{
    var conexionMigraciones = app.Configuration.GetConnectionString("ConexionMigraciones")
        ?? app.Configuration.GetConnectionString("ConexionSQL");
    if (string.IsNullOrWhiteSpace(conexionMigraciones))
        throw new InvalidOperationException("Falta ConnectionStrings:ConexionMigraciones (cuenta administradora) para aplicar las migraciones.");
    await ApiGestion.Database.EjecutorMigraciones.EjecutarAsync(conexionMigraciones, app.Logger, CancellationToken.None);
    app.Logger.LogInformation("Migraciones aplicadas. Ejecutá database/produccion/03_permisos_usuario_api.sql y 04_verificar_permisos.sql.");
    return;
}

// En desarrollo se aplican los cambios antes de aceptar peticiones.
// Fuera de Development se requiere habilitación explícita.
if (app.Configuration.GetValue<bool?>("DatabaseMigrations:Enabled") ?? app.Environment.IsDevelopment())
{
    var conexion = app.Configuration.GetConnectionString("ConexionSQL");
    if (string.IsNullOrWhiteSpace(conexion))
        throw new InvalidOperationException("Falta ConnectionStrings:ConexionSQL para ejecutar las migraciones.");
    await ApiGestion.Database.EjecutorMigraciones.EjecutarAsync(
        conexion, app.Logger, app.Lifetime.ApplicationStopping);
}

// Fuera de desarrollo, avisa si la API se conecta con más permisos de los que necesita: con sysadmin o db_owner,
// la auditoría se puede borrar sin dejar rastro (un DENY no los alcanza). No bloquea el arranque.
if (!app.Environment.IsDevelopment())
{
    try
    {
        var databasePermissionsDao = app.Services.GetRequiredService<DaoLibrary.DatabasePermissionsDao>();
        if (databasePermissionsDao.CanDeleteAudit())
            app.Logger.LogWarning(
                "La API está conectada a SQL Server con un usuario que puede borrar la auditoría (sysadmin, db_owner o permiso DELETE). " +
                "Usá el usuario cacc_api: ver database/produccion/README.md.");
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "No se pudieron verificar los permisos del usuario de base de datos.");
    }
}

if (usarCabecerasDeProxy)
    app.UseForwardedHeaders();

// Cualquier excepción que un controller no atrape responde un 500 con el mismo formato que el resto de la
// API ({ exito, mensaje }), sin detalles internos; el detalle queda en el log.
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var error = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    app.Logger.LogError(error, "Error no controlado en {Metodo} {Ruta}", context.Request.Method, context.Request.Path);
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new { exito = false, mensaje = "Error interno del servidor. Intentá de nuevo en unos minutos." });
}));

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(opciones =>
    {
        opciones.SwaggerEndpoint("/swagger/integracion-v1/swagger.json", "Integración con el Deportivo v1");
        opciones.SwaggerEndpoint("/swagger/v1/swagger.json", "Portal Administrativo");
    });
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

