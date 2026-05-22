# Technology Stack

## Framework & Runtime

- **.NET 9** - Target framework for all projects
- **C# 13** - Language version with nullable reference types enabled
- **PostgreSQL** - Primary database

## Backend Stack

- **ASP.NET Core 9** - Web API
- **Entity Framework Core 9** - ORM with Npgsql provider
- **MediatR** - CQRS implementation
- **FluentValidation** - Request validation
- **JWT Authentication** - Custom implementation (no ASP.NET Identity)
- **Swashbuckle** - API documentation (Swagger)

## Frontend Stack

- **Blazor Server (.NET 10)** - Server-side rendering with SignalR
- **Custom CSS** - No Tailwind in components (Tailwind only for build tooling)
- **CSS Variables** - Design tokens in `app.css`
- **Component-scoped CSS** - `.razor.css` files

## Build Tools

### Backend (.NET)
```bash
# Restore dependencies
dotnet restore

# Build solution
dotnet build

# Run API (from EEMOCantilanSDS.Api folder)
dotnet run

# Run Blazor Client (from EEMOCantilanSDS.Client folder)
dotnet run

# Create migration (from solution root)
dotnet ef migrations add MigrationName --project EEMOCantilanSDS.Infrastructure --startup-project EEMOCantilanSDS.Api

# Update database
dotnet ef database update --project EEMOCantilanSDS.Infrastructure --startup-project EEMOCantilanSDS.Api

# Run tests
dotnet test
```

### Frontend (CSS)
```bash
# Watch CSS changes (from EEMOCantilanSDS.Client folder)
npm run watch

# Install dependencies
npm install
```

## Key NuGet Packages

**API Layer:**
- Microsoft.AspNetCore.Authentication.JwtBearer (9.0.5)
- Microsoft.AspNetCore.OpenApi (9.0.14)
- Swashbuckle.AspNetCore (9.0.5)

**Infrastructure Layer:**
- Microsoft.EntityFrameworkCore (9.0.4)
- Microsoft.EntityFrameworkCore.Tools (9.0.4)
- Npgsql.EntityFrameworkCore.PostgreSQL (9.0.4)

**Application Layer:**
- MediatR
- FluentValidation
- AutoMapper (optional, only when profiles exist)

**Client Layer:**
- Microsoft.AspNetCore.Components.WebAssembly.Server (for Blazor Server)

## Database

**Provider:** Npgsql (PostgreSQL)  
**Connection String:** Configured in `appsettings.json`

### PostgreSQL Type Mappings
Always use PostgreSQL-native types:
- `text` (not `nvarchar(max)`)
- `character varying(n)` (not `nvarchar(n)`)
- `boolean` (not `bit`)
- `uuid` (not `uniqueidentifier`)
- `timestamp with time zone` (not `datetime`/`datetime2`)
- `numeric(18,2)` for decimals
- `integer` for ints
- `jsonb` for JSON data

## Development Ports

- **API:** https://localhost:7167 or http://localhost:5198
- **Blazor Client:** http://localhost:5173 or https://localhost:5173
- **CORS:** Configured for local development origins

## Authentication

- **Access Token:** JWT, 15 minute expiry, stored in cookie
- **Refresh Token:** 7 day expiry, httpOnly cookie
- **Password Hashing:** `PasswordHasher<BaseUser>` from Microsoft.AspNetCore.Identity (library only, no Identity framework)

## Testing

- **Framework:** xUnit
- **Project:** EEMOCantilanSDS.Testing

## Common Commands Summary

```bash
# Start API
cd EEMOCantilanSDS.Api
dotnet run

# Start Blazor Client
cd EEMOCantilanSDS.Client
dotnet run

# Watch CSS (in separate terminal)
cd EEMOCantilanSDS.Client
npm run watch

# Add migration
dotnet ef migrations add MigrationName --project EEMOCantilanSDS.Infrastructure --startup-project EEMOCantilanSDS.Api

# Update database
dotnet ef database update --project EEMOCantilanSDS.Infrastructure --startup-project EEMOCantilanSDS.Api

# Run tests
dotnet test

# Build entire solution
dotnet build

# Clean solution
dotnet clean
```

## Environment Configuration

Configuration files:
- `appsettings.json` - Base configuration
- `appsettings.Development.json` - Development overrides
- Connection strings, JWT secrets, and API URLs configured per environment
