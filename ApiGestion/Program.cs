using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// 1. AGREGA ESTA POLÍTICA DE CORS (Permite conexiones desde Angular)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular",
        policy =>
        {
            policy.AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader();
        });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddScoped<DaoLibrary.AuthDao>(provider =>
    new DaoLibrary.AuthDao(builder.Configuration.GetConnectionString("ConexionSQL") ?? ""));

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

// Aranceles: DAO + servicio de negocio
builder.Services.AddScoped<DaoLibrary.IArancelesDao>(provider =>
    new DaoLibrary.ArancelesDao(builder.Configuration.GetConnectionString("ConexionSQL") ?? ""));
builder.Services.AddScoped<ServiceLibrary.IArancelesService, ServiceLibrary.ArancelesService>();

// Becados y descuentos
builder.Services.AddScoped<DaoLibrary.DiscountDao>(provider =>
    new DaoLibrary.DiscountDao(builder.Configuration.GetConnectionString("ConexionSQL") ?? ""));

// 4. NUEVO: Configuración de autenticación JWT
var jwtKey = builder.Configuration["Jwt:Key"]!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// 2. ACTIVA EL CORS AQUÍ (Antes de UseAuthorization y MapControllers)
app.UseCors("AllowAngular");

// 4. NUEVO: tiene que ir ANTES de UseAuthorization
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// 3. SEED: crea el usuario Admin si no existe (una sola vez al arrancar)
string cadenaConexion = builder.Configuration.GetConnectionString("ConexionSQL") ?? "";
// DaoLibrary.SeedAdmin.CrearAdminSiNoExiste(cadenaConexion);

app.Run();

