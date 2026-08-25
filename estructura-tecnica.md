# Estructura Técnica — Sistema Administrativo CACC

Documento técnico de cómo está armado el repositorio. Complementa a `README.md` (visión general) y a `project-sistema-administrativo-cacc.md` (producto y portales).

## Visión general

Dos paquetes independientes en un mismo repositorio, sin orquestación de build compartida:

- **Backend** — solución .NET 10 (`SistemaCamionerosBackend.slnx`) con 3 proyectos: `ApiGestion` → `DaoLibrary` → `EntityLibrary`.
- **Frontend** — SPA Angular 22 (`frontend-cacc/`) con componentes standalone, TypeScript 6 y Vitest.

Cada lado arranca, compila y testea por separado.

## Árbol de archivos

```
Proyecto - Sistema Administrativo - CACC/
├── AGENTS.md                              ← guía de convenciones para agentes
├── README.md                              ← descripción general del sistema
├── project-sistema-administrativo-cacc.md ← doc de producto: portales y módulos
├── SistemaCamionerosBackend.slnx
├── openspec/
│   └── sdd-init.json                      ← init SDD (sin trackear)
├── ApiGestion/
│   ├── ApiGestion.csproj
│   ├── Program.cs                         ← bootstrap, DI, CORS, Swagger
│   ├── appsettings.json                   ← connection string SQL Server
│   ├── Controllers/
│   │   ├── AuthController.cs
│   │   └── WeatherForecastController.cs   ← template residual, sin uso
│   ├── Models/
│   │   └── LoginRequest.cs                ← DTO {Usuario, Contrasena}
│   └── Properties/launchSettings.json
├── DaoLibrary/
│   ├── DaoLibrary.csproj
│   └── AuthDao.cs                         ← acceso a datos con SQL crudo
├── EntityLibrary/
│   ├── EntityLibrary.csproj
│   └── Usuario.cs                         ← POCO {IdUsuario, Email, Contrasenia, IdRol}
└── frontend-cacc/
    ├── package.json / angular.json / tsconfig.*
    ├── public/logo-cacc.ico
    └── src/
        ├── main.ts / styles.css / index.html
        └── app/
            ├── app.ts / app.html (huérfano) / app.css / app.config.ts
            ├── app.routes.ts
            ├── login/        (login.ts, login.html, login.css, login.spec.ts)
            ├── portales/     (portales.ts, portales.html, portales.css, portales.spec.ts)
            ├── dashboard/    (dashboard.ts, dashboard.html, dashboard.css, dashboard.spec.ts)
            └── services/     (auth.ts, auth.spec.ts)
```

## Backend

### ApiGestion (Web API)

- **Program.cs**: registra `AuthDao` como scoped con la connection string, habilita CORS abierto (`AllowAngular`: any origin/method/header), Swagger solo en Development, HTTPS redirect y `MapControllers`.
- **AuthController**: `POST api/auth/login`. Valida credenciales vía `AuthDao.ValidarLogin`; éxito → 200 `{mensaje, email, rol}`; credenciales inválidas → 401; excepción → 500.
- **LoginRequest**: DTO `{Usuario, Contrasena}` sin validación de null.
- `WeatherForecastController` y `WeatherForecast.cs` son scaffolding del template sin limpiar.

### DaoLibrary

- **AuthDao.cs**: `ValidarLogin(email, contrasenia): Usuario?`. SQL crudo parametrizado contra la tabla `USUARIO` usando `Microsoft.Data.SqlClient` (sin ORM, sin EF, sin stored procedures). **Compara la contraseña en texto plano** (gap de seguridad conocido).

### EntityLibrary

- **Usuario.cs**: POCO `{IdUsuario, Email, Contrasenia, IdRol}`. Sin dependencias.

### Tabla SQL utilizada

| Tabla | Columnas observadas |
|---|---|
| `USUARIO` | `PK_id_usuario`, `email`, `contrasenia`, `FK_id_rol`, `activo` |

Base: SQL Server local, base `ClubCamionerosPRUEBA`, autenticación Windows (`Trusted_Connection=True`). Sin credenciales SQL en el repo.

## Frontend (Angular 22)

Configuración: componentes standalone (sin NgModules), builder `@angular/build:application`, tests con Vitest vía `@angular/build:unit-test`, Prettier 3.8 como única herramienta de calidad (sin ESLint).

| Componente | Qué hace | Estado |
|---|---|---|
| `App` | Shell con `<router-outlet>` inline | OK |
| `Login` | Form reactivo con validación y toggle de contraseña. POST directo con `HttpClient` a `http://localhost:5118/api/auth/login`, redirige a `/portales` a los 1.5s | Implementado (HU-001) |
| `Portales` | Pantalla de elección de entorno: tarjetas Administrativo y Deportivo. Botones → `alert()` (navegación comentada). `cerrarSesion()` → `/` | UI real, acciones stub (HU-005) |
| `Dashboard` | Contenido placeholder | Stub, sin ruta |
| `AuthService` | `login(credentials)` → POST a la misma URL | Creado pero no consumido |

### Endpoints consumidos

| Método | Ruta |
|---|---|
| POST | `http://localhost:5118/api/auth/login` |

### Rutas Angular

| Path | Componente |
|---|---|
| `''` | Login |
| `'portales'` | Portales |
| `'**'` | redirect a `''` |

Sin guards, sin lazy loading, sin rutas de dashboard ni portales internos.

## Comandos

```bash
# Backend (desde la raíz)
dotnet run --project ApiGestion   # API en https://localhost:5118
dotnet build

# Frontend (desde frontend-cacc/)
npm install
npm start                         # ng serve → localhost:4200
npm run build
npm test                          # ng test (Vitest)
npx prettier --write .
```

## Estado por historia de usuario

- **HU-001 (Login)**: implementada end-to-end (UI + endpoint + BD). Deuda: contraseña en texto plano, sin token/sesión, sin guard de ruta.
- **HU-005 (Elección de entorno / validación de credenciales)**: UI completa de portales con 2 de 3 entornos del doc de producto (falta Portal de Socios/Comunidad). La validación de credenciales es solo el login genérico: **no filtra portales por rol** (el `rol` se devuelve pero nunca se usa) y el ingreso a cada portal es `alert()` stub.
- **Dashboard**: sin ruta, sin navegación, contenido placeholder.

## Deuda técnica y pendientes

1. Contraseñas en texto plano (comparación directa en SQL).
2. Sin JWT/token, sin sesión, sin guard: `/portales` es accesible sin login.
3. CORS totalmente abierto — ok para dev, debe restringirse antes de desplegar.
4. URL de API duplicada en `login.ts` y `services/auth.ts`; `AuthService` duplicado sin uso.
5. `auth.spec.ts` roto: importa `Auth` inexistente (el archivo exporta `AuthService`).
6. `WeatherForecastController` / `WeatherForecast.cs`: template residual sin limpiar.
7. `app.html` huérfano: `app.ts` usa template inline.
8. `.gitignore` raíz es de C/C++: no cubre `bin/`, `obj/` ni `node_modules/` (el frontend tiene el suyo propio).
9. Tests existentes son boilerplate ("should create").