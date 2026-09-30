<div align="center">
  <img src="frontend-cacc/public/cacc-email-header.jpg" alt="Club Atlético Social y Deportivo Camioneros — CACC" width="900" />

  <h1>Portal Administrativo CACC</h1>
  <p><strong>Una herramienta para acompañar y organizar la vida administrativa del club.</strong></p>

  <p>
    <img src="https://img.shields.io/badge/Angular-22.1-DD0031?logo=angular&logoColor=white" alt="Angular 22" />
    <img src="https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 9" />
    <img src="https://img.shields.io/badge/SQL%20Server-datos-CC2927?logo=microsoftsqlserver&logoColor=white" alt="SQL Server" />
    <img src="https://img.shields.io/badge/Estado-en%20desarrollo-16834A" alt="En desarrollo" />
  </p>
</div>

---

## El proyecto

El Portal Administrativo CACC centraliza tareas de gestión deportiva y administrativa del **Club Atlético Social y Deportivo Camioneros**. Reúne en una sola aplicación la información de jugadores, cuotas, pagos, aranceles, descuentos y actividad administrativa.

La interfaz y sus mensajes están en español rioplatense, con la identidad visual verde del club.

## Qué permite hacer

| Área | Funcionalidad |
| --- | --- |
| **Jugadores** | Consultar el padrón, revisar perfiles y ver historiales de pagos. |
| **Inscripciones** | Administrar inscripciones y sus pagos. |
| **Cuotas y pagos** | Registrar cobros, consultar movimientos, pagos recientes y cuentas con saldo pendiente. |
| **Deudas y morosidad** | Filtrar deudas por categoría, revisar saldos y abrir el detalle de deuda de cada jugador. |
| **Becados y descuentos** | Gestionar descuentos, becas y sus estados. |
| **Aranceles** | Programar aranceles y consultar historial y resumen. |
| **Reportes** | Exportar información de deudores, pagos, becas y aranceles en PDF o CSV. |
| **Usuarios y permisos** | Habilitar cuentas, asignar roles y administrar accesos al portal. |
| **Auditoría** | Consultar cambios y operaciones administrativas. |
| **Acceso a cuentas** | Iniciar sesión, activar una cuenta y solicitar el restablecimiento de contraseña por correo. |

### Roles

- **Superadministrador:** acceso a la configuración y a las áreas de administración completa.
- **Administrador:** acceso acotado a las tareas operativas habilitadas para su rol.

Las vistas y operaciones protegidas usan autenticación JWT y autorización por rol.

## Tecnologías

| Parte | Tecnologías |
| --- | --- |
| **Frontend** | Angular 22, componentes standalone, TypeScript 6, Angular Router y Vitest. |
| **API** | ASP.NET Core sobre .NET 9, C# y Swagger en desarrollo. |
| **Lógica de negocio** | `ServiceLibrary`, con servicios para pagos y aranceles. |
| **Acceso a datos** | ADO.NET y Microsoft SQL Server; consultas SQL parametrizadas. |
| **Correo y reportes** | MailKit para correo, QuestPDF para PDF y exportación CSV. |
| **Pruebas del backend** | xUnit. |

## Estructura

```text
.
├── ApiGestion/          # API REST, autenticación, autorización y reportes
├── ApiGestion.Tests/    # Pruebas automatizadas del backend (xUnit)
├── DaoLibrary/          # Acceso a datos SQL con ADO.NET
├── EntityLibrary/       # Entidades del dominio
├── ServiceLibrary/      # Servicios de negocio
├── database/            # Scripts SQL incrementales
├── frontend-cacc/       # Aplicación Angular
└── SistemaCamionerosBackend.slnx
```

Frontend y backend se ejecutan y compilan por separado; el repositorio no usa un orquestador de monorepo.

## Requisitos

- .NET SDK **9.0.317** o posterior compatible con `global.json`.
- Node.js compatible con Angular 22 y npm **11.17.0**.
- SQL Server con la base de datos del club preparada.
- Para activar cuentas y restablecer contraseñas: un servidor SMTP configurado.

## Puesta en marcha

### 1. Configurar el backend

Revisá [`ApiGestion/appsettings.Development.json.example`](ApiGestion/appsettings.Development.json.example) como referencia. Configurá los valores de desarrollo mediante User Secrets o un archivo local no versionado.

Configuraciones necesarias:

| Clave | Uso |
| --- | --- |
| `ConnectionStrings:ConexionSQL` | Conexión al SQL Server y a la base del club. |
| `Jwt:Key` | Clave secreta de al menos 32 bytes; no uses la clave de ejemplo. |
| `Jwt:Issuer` y `Jwt:Audience` | Emisor y audiencia de los tokens. |
| `Cors:AllowedOrigins` | Orígenes desde los que se permitirá acceder a la API. En desarrollo: `http://localhost:4200`. |
| `Security:SuperAdminEmail` | Correo habilitado para solicitar el restablecimiento de la cuenta de SuperAdmin. |
| `Email:*` | Servidor SMTP, credenciales, remitente y URL pública del frontend para enlaces de acceso. |

Para guardar valores de desarrollo fuera del repositorio:

```bash
dotnet user-secrets init --project ApiGestion
dotnet user-secrets set "Jwt:Key" "REEMPLAZAR_POR_UNA_CLAVE_ALEATORIA_DE_32_BYTES_O_MAS" --project ApiGestion
dotnet user-secrets set "Jwt:Issuer" "SistemaCACC" --project ApiGestion
dotnet user-secrets set "Jwt:Audience" "SistemaCACC" --project ApiGestion
dotnet user-secrets set "ConnectionStrings:ConexionSQL" "Server=localhost;Database=TU_BASE;Trusted_Connection=True;TrustServerCertificate=True;" --project ApiGestion
```

Agregá del mismo modo las claves `Email:*`, `Security:SuperAdminEmail` y `Cors:AllowedOrigins` que necesite tu entorno. No subas contraseñas SMTP ni claves JWT a Git.

### 2. Preparar la base de datos

La carpeta [`database/`](database/) contiene scripts incrementales para una base existente; **no** incluye un instalador completo del esquema inicial ni un ejecutor automático de migraciones. Revisá cada script y aplicalo manualmente sobre la base correcta, en coordinación con quien administre SQL Server. No ejecutes scripts directamente sobre una base con datos reales sin respaldo.

El backend usa ADO.NET; no utiliza Entity Framework ni `dotnet ef`.

### 3. Iniciar la API

Desde la raíz del repositorio:

```bash
dotnet run --project ApiGestion
```

En desarrollo, Swagger está disponible en `/swagger`. La API usa los puertos configurados por `ApiGestion/Properties/launchSettings.json`.

### 4. Iniciar Angular

En otra terminal:

```bash
cd frontend-cacc
npm install
npm start
```

Abrí [http://localhost:4200](http://localhost:4200). El frontend usa rutas `/api`; revisá `frontend-cacc/proxy.conf.json` y la configuración local para que las solicitudes lleguen a la API.

## Compilar y ejecutar pruebas

Cada paquete se valida por separado.

```bash
# Backend: desde la raíz
dotnet build
dotnet test

# Frontend: desde frontend-cacc/
npm run build
npm test
```

## Scripts de base de datos

Los scripts versionados están en [`database/`](database/). Reflejan cambios de esquema y datos necesarios para las funcionalidades incorporadas. Como no hay un sistema que registre automáticamente cuáles ya se aplicaron, mantené un registro de ejecución por entorno y verificá el contenido antes de aplicarlos.

## Seguridad y despliegue

- Configurá una clave JWT propia y fuerte, y guardá secretos en variables de entorno o en un almacén seguro.
- Limitá `Cors:AllowedOrigins` a los dominios reales del frontend.
- Usá HTTPS para la API, el frontend y `Email:PublicBaseUrl` en producción.
- Configurá y verificá SMTP antes de habilitar correos de activación o recuperación.
- Aplicá el principio de mínimo privilegio a la cuenta SQL y respaldá la base antes de cambios.
- La configuración de desarrollo no debe reutilizarse como configuración de producción.

## Estado

El sistema está en desarrollo activo y puede incorporar cambios funcionales y visuales. Esta guía también irá evolucionando con el proyecto.

---

<div align="center">
  <strong>Club Atlético Social y Deportivo Camioneros</strong><br />
  <sub>Trabajo, identidad y compromiso con el club.</sub>
</div>
