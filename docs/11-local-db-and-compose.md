# 11. Local Development & Docker Compose Deployment

Playmox ships with a production-ready `docker-compose.yml` that orchestrates **SQL Server 2025 Express**, the **ASP.NET Core 8.0 API**, and the **Blazor WebAssembly Frontend**.

---

## 1. Container Architecture (`docker-compose.yml`)

| Service / Container | Base Image / Dockerfile | Default Host Port | Description |
| :--- | :--- | :--- | :--- |
| **`playmox-db`** | `mcr.microsoft.com/mssql/server:2025-latest` | `1433` (`DB_HOST_PORT`) | SQL Server 2025 Express (up to 50 GB per DB) with persistent volume `playmox-mssql-data`. |
| **`playmox-api`** | `./API/Dockerfile` (`.NET 8.0`) | `8080` (`API_HOST_PORT`) | ASP.NET Core 8 API, background provisioning workers, and automatic EF Core migration runner. |
| **`playmox-frontend`** | `./Frontend/PlaymoxWeb/Dockerfile` (`nginx:alpine`) | `8081` (`FRONTEND_HOST_PORT`) | Nginx server hosting the compiled Blazor WebAssembly SPA and injecting `API_BASE_URL` at container startup. |

---

## 2. Environment Configuration (`.env`)

Copy `.env.example` to `.env` in the repository root before starting the stack:

```bash
cp .env.example .env
```

### Complete `.env` Reference

| Variable | Example / Default | Description |
| :--- | :--- | :--- |
| `MSSQL_PID` | `Express` | SQL Server edition (`Express`, `Developer`, etc.). |
| `DB_NAME` | `Playmox` | Database name created and migrated automatically by EF Core. |
| `DB_SA_USER` | `sa` | SQL Server administrator login. |
| `DB_SA_PASSWORD` | `ChangeMe_UseStrongPassword123!` | Strong SQL Server password (at least 8 characters, mixed case, digits, symbols). |
| `DB_HOST_PORT` | `1433` | Host port mapped to container port `1433`. |
| `API_HOST_PORT` | `8080` | Host port mapped to the Playmox API container (`8080`). |
| `FRONTEND_HOST_PORT` | `8081` | Host port mapped to the Playmox Frontend Nginx container (`80`). |
| `FRONTEND_API_BASE_URL` | `http://localhost:8080/` | Base URL of the Playmox API as reached by the user's browser (must include trailing slash). |
| `Cors__AllowedOrigins__0` | `http://localhost:8081` | Indexed CORS allowed origins (`Cors__AllowedOrigins__0`, `Cors__AllowedOrigins__1`, ...). |
| `BOOTSTRAP_ADMIN_LOGIN` | `admin` | Default Playmox administrator username seeded on startup. |
| `BOOTSTRAP_ADMIN_PASSWORD` | `ChangeMe_AdminPassword123!` | Default Playmox administrator password seeded on startup. |
| `BOOTSTRAP_ADMIN_FIRST_NAME` | `Playmox` | First name of the bootstrap administrator. |
| `BOOTSTRAP_ADMIN_LAST_NAME` | `Admin` | Last name of the bootstrap administrator. |
| `PASSWORD_SALT` | `ChangeMe_VeryLongRandomSalt` | Cryptographic salt used for Argon2id password hashing (`Security:PasswordSalt`). |
| `APP_SECRET` | `ChangeMe_VeryLongRandomAppSecret_AtLeast32Chars` | Master key (minimum 32 characters) used for JWT signing and AES-256 encryption (`SecureSecret`). |
| `SWAGGER_USER` | `ChangeMe_SwaggerUser` | Basic Auth username for `/swagger` when running outside `Development`. |
| `SWAGGER_PASSWORD` | `ChangeMe_SwaggerPassword` | Basic Auth password for `/swagger` when running outside `Development`. |
| `PROXMOX_ALLOW_SELF_SIGNED_CERTIFICATES` | `true` | Allows `ProxmoxAutomationClient` to connect to Proxmox nodes using default self-signed TLS certificates. |

### Accessing Playmox Over LAN or a Public Domain (CORS Configuration)

Because Blazor WebAssembly runs directly in the user's browser, `FRONTEND_API_BASE_URL` must point to an address reachable from the client browser, and the frontend origin must be listed in `Cors__AllowedOrigins__*`:

```dotenv
FRONTEND_API_BASE_URL=http://192.168.1.20:8080/
Cors__AllowedOrigins__0=http://192.168.1.20:8081
Cors__AllowedOrigins__1=http://localhost:8081
```

---

## 3. Starting & Updating the Stack

Build and start all services in detached mode:

```bash
docker compose up -d --build
```

### Automatic Migrations & Data Seeding
When `playmox-api` starts:
1. It checks `dbContext.Database.GetPendingMigrations()` and automatically executes `dbContext.Database.Migrate()`.
2. `BootstrapDataSeeder.SeedInitialData` ensures:
   - The `Admin` and `User` roles exist.
   - The bootstrap administrator account (`BOOTSTRAP_ADMIN_LOGIN`) exists with `Admin` and `User` roles and a default high-capacity `UserQuota` (`32` CPU cores, `64 GB` RAM, `1000 GB` Disk, `20` instances, `200` public ports, SSH enabled).
   - All built-in `GameDefinitions` (`blank-vm`, `minecraft-java`, `terraria`, `unturned`, `factorio`, `cs2`) and their JSON Schemas are up to date.

---

## 4. Running Locally Without Docker (IDE / .NET CLI)

If you want to run `API` and `Frontend/PlaymoxWeb` directly from Visual Studio, Rider, or the `.NET 8.0` CLI while keeping SQL Server in Docker:

1. Start only the database container:
   ```bash
   docker compose up -d playmox-db
   ```
2. Configure `API/appsettings.Development.json` (or user secrets) with your local SQL Server connection string, `Security:PasswordSalt`, and `Security:AppSecret`.
3. Run the backend API:
   ```bash
   dotnet run --project API/PlaymoxAPI.csproj
   ```
4. Run the Blazor WebAssembly frontend:
   ```bash
   dotnet run --project Frontend/PlaymoxWeb/PlaymoxWeb.csproj
   ```
5. To add a new Entity Framework Core migration after modifying domain entities in `API/BLL/BO/`:
   ```bash
   dotnet ef migrations add <MigrationName> --project API/PlaymoxAPI.csproj
   ```
