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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// 2. ACTIVA EL CORS AQUÍ (Antes de UseAuthorization y MapControllers)
app.UseCors("AllowAngular");

app.UseAuthorization();

app.MapControllers();

// 3. SEED: crea el usuario Admin si no existe (una sola vez al arrancar)
string cadenaConexion = builder.Configuration.GetConnectionString("ConexionSQL") ?? "";
/*/DaoLibrary.SeedAdmin.CrearAdminSiNoExiste(cadenaConexion); /*/ 

app.Run();


