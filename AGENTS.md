# AGENTS.md — Sistema Administrativo CACC

## Architecture

Two independent packages in one repo, no shared build orchestration:

- **Backend** (`ApiGestion/`, `DaoLibrary/`, `EntityLibrary/`): .NET 10 Web API, 3-project C# solution (`SistemaCamionerosBackend.slnx`). Dependency chain: `ApiGestion → DaoLibrary → EntityLibrary`.
- **Frontend** (`frontend-cacc/`): Angular 22 standalone components, TypeScript 6, Vitest.

Each side starts, builds, and tests independently. There is no monorepo tool, no Nx, no shared build script.

## Backend Commands

```bash
# Run API (from repo root or ApiGestion/)
dotnet run --project ApiGestion

# Build solution
dotnet build

# Run tests (when test projects exist)
dotnet test
```

API starts on `https://localhost:5118` / `http://localhost:5118`. Swagger UI at `/swagger` in Development.

## Frontend Commands

All from `frontend-cacc/`:

```bash
npm install          # install deps (npm 11.17.0 pinned via packageManager)
npm start            # ng serve → localhost:4200
npm run build        # production build
npm test             # Vitest unit tests
npx prettier --write .   # format (printWidth 100, single quotes)
```

No ESLint or Angular lint is configured. Prettier is the only code quality tool.

## Dev Workflow

1. Start backend: `dotnet run --project ApiGestion`
2. Start frontend: `cd frontend-cacc && npm start`
3. Frontend hardcodes API URL `http://localhost:5118/api/auth/login`. If the backend port changes, update `frontend-cacc/src/app/services/auth.ts` AND `frontend-cacc/src/app/login/login.ts`.

## Database

- SQL Server (localhost), database `ClubCamionerosPRUEBA`.
- Connection string in `ApiGestion/appsettings.json` under `ConnectionStrings:ConexionSQL`.
- Uses Windows auth (`Trusted_Connection=True`). No SQL auth credentials in the repo.
- Data access via raw ADO.NET (`Microsoft.Data.SqlClient`) in `DaoLibrary/` — no ORM, no Entity Framework, no migrations tool.

## Project Structure

```
ApiGestion/          → ASP.NET Web API controllers, models, Program.cs entrypoint
  Controllers/       → REST endpoints (route: api/[controller])
  Models/            → Request/response DTOs
DaoLibrary/          → Data access objects (raw SQL via SqlConnection)
EntityLibrary/       → Plain C# entity classes (POCOs)
frontend-cacc/       → Angular 22 SPA
  src/app/
    login/           → Login page (standalone component)
    portales/        → Portal selection page (stub, uses alert())
    dashboard/       → Dashboard component (empty stub)
    services/        → Angular services (auth.ts)
```

## Conventions

- Angular components are **standalone** (no NgModules).
- Component files: `name.ts`, `name.html`, `name.css`, `name.spec.ts` — all in the same directory.
- Backend follows standard ASP.NET controller pattern: `api/[controller]` routes.
- SQL queries are inline strings with parameterized queries (no stored procedures observed).
- Comments and UI text are in **Spanish** (Argentina). Code identifiers are in Spanish.
- Prettier: 100 char width, single quotes, Angular HTML parser.

## Gotchas

- **No auth token flow yet**: Login returns email/rol on success but the frontend doesn't store or send a token. There's no JWT, no session middleware, no route guards.
- **CORS is wide open**: `AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()` in `Program.cs`. Fine for dev, must be locked down before any deployment.
- **Passwords stored/transmitted in plaintext**: `AuthDao.cs` compares `contrasenia` directly. No hashing. This is a known security gap to address.
- **Portal navigation is stubbed**: `portales.ts` calls `alert()` instead of routing to actual portal views.
- **Dashboard component exists but isn't routed**: `app.routes.ts` only has `''` → Login and `'portales'` → Portales. Dashboard is imported nowhere.
- **Duplicate API URL**: The backend URL `http://localhost:5118/api/auth/login` is hardcoded in two places (`services/auth.ts` and `login/login.ts`). The `AuthService` exists but `Login` component doesn't use it — it calls `HttpClient` directly.
- **Root `.gitignore` is for C/C++**: The top-level `.gitignore` covers `.o`, `.ko`, `.elf`, `.dll` etc. It doesn't cover .NET or Node artifacts — those are in `frontend-cacc/.gitignore`. The root gitignore could use a .NET/Node section.
