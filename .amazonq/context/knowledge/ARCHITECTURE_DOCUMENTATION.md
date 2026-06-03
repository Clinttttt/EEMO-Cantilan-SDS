# EEMO Cantilan SDS — Architecture Documentation

---

## Architecture Overview

Clean Architecture + Domain-Driven Design (DDD) + CQRS

```
┌─────────────────────────────────────────────────────────────┐
│                    Presentation Layer                        │
│  ┌──────────────────────┐      ┌──────────────────────┐    │
│  │   Blazor Server      │      │    ASP.NET Core API  │    │
│  │   (.NET 10  Web UI)    │◄────►│    (Controllers)     │    │
│  └──────────────────────┘      └──────────────────────┘    │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    Application Layer                         │
│  CQRS (Commands & Queries) + MediatR + FluentValidation    │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                      Domain Layer                            │
│  Entities + Enums + Constants + Business Rules              │
└─────────────────────────────────────────────────────────────┘
                              ▲
                              │
┌─────────────────────────────────────────────────────────────┐
│                   Infrastructure Layer                       │
│  EF Core + PostgreSQL + Repositories + UnitOfWork           │
└─────────────────────────────────────────────────────────────┘
```

**Dependency Rule:** Presentation → Application → Domain ← Infrastructure

---

## Key Patterns

### 1. Clean Architecture
- Domain has ZERO external dependencies
- Application defines interfaces, Infrastructure implements
- Dependencies flow inward only

### 2. Domain-Driven Design (DDD)
**Entities:**
- Private setters, static `Create()` factory methods
- Business rules enforced internally

**Aggregates:**
- Stall → Contracts, PaymentRecords, DailyCollections
- BaseUser → AdminUser, CollectorUser

### 3. CQRS
**Commands** (Write): `CreateStallCommand`, `RecordPaymentCommand`  
**Queries** (Read): `GetStallsByFacilityQuery`, `GetPaymentHistoryQuery`

All return `Result<T>`

### 4. MediatR Pipeline
```
Controller → MediatR.Send() → ValidationBehavior → Handler → Result<T>
```

### 5. Repository + Unit of Work
```csharp
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

### 6. Result Pattern
Type-safe error handling:
```csharp
Result<T>.Success(value)
Result<T>.NotFound()
Result<T>.Failure(error)
Result<T>.ValidationFailure(errors)
```

### 7. Typed API Clients (Blazor → API)
```csharp
// Interface (Application)
public interface IStallsApiClient
{
    Task<Result<StallDto>> GetStallAsync(Guid id);
}

// Implementation (Infrastructure)
public class StallsApiClient(HttpClient http) : HandleResponse, IStallsApiClient
{
    public async Task<Result<StallDto>> GetStallAsync(Guid id)
        => await GetAsync<StallDto>($"/api/stalls/{id}");
}
```

### 8. Authentication & Audit
- JWT access tokens (15 min); refresh tokens hashed at rest, revoked on logout
- No ASP.NET Identity (custom implementation)
- Account lockout: 5 failed attempts = 15 min lock
- Role-gated endpoints; web is admin/head only, collectors on mobile
- Collector attribution comes from the authenticated actor (never the client); financial mutations are audited automatically via a SaveChanges interceptor

---

## Layer Responsibilities

| Layer | Purpose | Dependencies |
|-------|---------|--------------|
| **Domain** | Business logic, entities | None |
| **Application** | Use cases, CQRS, validation | Domain |
| **Infrastructure** | Data access, EF Core | Application + Domain |
| **API** | HTTP endpoints | Application + Infrastructure |
| **Client** | Blazor UI | None (calls API via HTTP) |

---

## Technology Stack

| Layer | Technologies |
|-------|--------------|
| **Frontend** | Blazor Server (.NET 10), Razor, CSS |
| **API** | ASP.NET Core 9, MediatR, FluentValidation, JWT |
| **Domain** | C# 13, .NET 9 |
| **Data** | EF Core 9, Npgsql (PostgreSQL) |
| **Testing** | xUnit |

---

## SOLID Principles

✅ **Single Responsibility** - Each class has one reason to change  
✅ **Open/Closed** - Extend via behaviors, don't modify  
✅ **Liskov Substitution** - TPH inheritance  
✅ **Interface Segregation** - Small, focused interfaces  
✅ **Dependency Inversion** - Depend on abstractions  

---

## Key Decisions

**Why Clean Architecture?**  
✅ Testability, maintainability, flexibility

**Why CQRS?**  
✅ Optimized read models, clear separation

**Why MediatR?**  
✅ Decoupled handlers, pipeline behaviors

**Why Repository Pattern?**  
✅ Abstracted data access, testable

**Why Result Pattern?**  
✅ Type-safe error handling, no exceptions

**Why No ASP.NET Identity?**  
✅ Simpler, full control over user model

---

## Summary

✅ Clean Architecture - Strict layer separation  
✅ DDD - Rich domain model  
✅ CQRS - Separated reads/writes  
✅ MediatR - Decoupled handlers  
✅ Repository + UnitOfWork - Abstracted data access  
✅ Result Pattern - Type-safe errors  
✅ FluentValidation - Declarative validation  
✅ JWT Auth - Secure authentication  
✅ EF Core - Modern ORM  

**Testable, maintainable, scalable.** 🚀
