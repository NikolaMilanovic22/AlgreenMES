# AlgreenMES — Sveobuhvatni tehnički audit za multi-tenant readiness

**Verzija**: snapshot na dan 2026-04-28
**Cilj**: single source of truth o stanju kodebaze pre bilo kakvih izmena za skaliranje na više tenanata. Sve izmene se planiraju **posle** ovog audita.
**Skenirana područja**: backend `/mnt/d/Projects/AlgreenMES`, frontend monorepo `/mnt/d/Projects/AlgreenMES front/algreen-tracker`.

**Klasifikacija nalaza**:
- ✅ **SAFE** — pravilno tenant-scoped ili nije relevantno za tenant boundary
- ⚠️ **NEEDS REVIEW** — radi na trenutnom 1-tenant sistemu ali dvosmislen za multi-tenant
- 🔴 **LEAK RISK** — nema tenant filtera, ozbiljan rizik kod više tenanata
- ❌ **MISSING** — funkcionalnost koja bi normalno postojala a ne postoji

---

## 1. Tenant scoping audit

### 1a. Controller endpoints

Sumarno: **većina endpointa prima `tenantId` kao explicit parameter** (query string ili request body). Nekoliko endpointa **nema explicit tenantId** i oslanja se na implicitan boundary preko vlasništva nad entitetom — što danas radi ali je krhko.

#### Orders modul

**`src/Modules/Orders/AlGreenMES.Modules.Orders.Api/Controllers/OrdersController.cs`**

| Linija | Ruta | Entitet | tenantId source | Status |
|-------:|------|---------|-----------------|--------|
| 53–65 | `GET /api/orders` | Order | `[FromQuery] Guid tenantId` | ✅ |
| 79–93 | `GET /api/orders/master-view` | Order | `[FromQuery] Guid tenantId` | ✅ |
| 110 | `GET /api/orders/{id}` | Order | NEMA — samo orderId | ⚠️ |
| 125 | `POST /api/orders` | Order | `request.TenantId` | ✅ |
| 139 | `PUT /api/orders/{id}` | Order | `request.TenantId` | ✅ |
| 153 | `POST /api/orders/{id}/activate` | Order | NEMA | ⚠️ |
| 161 | `POST /api/orders/{id}/pause` | Order | NEMA | ⚠️ |
| 169 | `POST /api/orders/{id}/resume` | Order | NEMA | ⚠️ |
| 177 | `POST /api/orders/{id}/cancel` | Order | NEMA | ⚠️ |
| 185 | `POST /api/orders/{id}/reopen` | Order | NEMA | ⚠️ |
| 194 | `POST /api/orders/{orderId}/items` | OrderItem | NEMA | ⚠️ |
| 203 | `DELETE /api/orders/{orderId}/items/{itemId}` | OrderItem | NEMA | ⚠️ |
| 211 | `PUT /api/orders/{id}/priority` | Order | NEMA | ⚠️ |
| 219 | `PUT /api/orders/{id}/invoiced` | Order | NEMA | ⚠️ |
| 227 | `POST /api/orders/{id}/withdraw` | Order | NEMA | ⚠️ |
| 235 | `POST .../special-requests` | OrderItemSpecialRequest | NEMA | ⚠️ |
| 243 | `DELETE .../special-requests/{srId}` | OrderItemSpecialRequest | NEMA | ⚠️ |
| 251 | `PUT .../complexity` | OrderItemProcess | NEMA | ⚠️ |
| 260–267 | `POST /api/orders/{orderId}/attachments` | OrderAttachment | `[FromQuery] tenantId` | ✅ |
| 277 | `GET /api/orders/{orderId}/attachments` | OrderAttachment | NEMA | ⚠️ |
| 282–299 | `GET .../attachments/{id}/download` | OrderAttachment | NEMA — direktno repo | 🔴 |
| 303–305 | `DELETE .../attachments/{id}` | OrderAttachment | `[FromQuery] tenantId` | ✅ |

**`src/Modules/Orders/AlGreenMES.Modules.Orders.Api/Controllers/ProcessWorkflowController.cs`**

| Linija | Ruta | tenantId source | Status |
|-------:|------|-----------------|--------|
| 33 | `POST .../{id}/block` | NEMA | ⚠️ |
| 41 | `POST .../{id}/unblock` | NEMA | ⚠️ |
| 48 | `POST .../{id}/complete` | NEMA | ⚠️ |
| 56 | `POST .../{id}/restart` | NEMA | ⚠️ |
| 64 | `POST .../{id}/withdraw` | NEMA | ⚠️ |
| 71 | `POST .../{id}/start` | NEMA | ⚠️ |
| 78 | `POST .../{id}/stop` | NEMA | ⚠️ |
| 85 | `POST .../{id}/resume` | NEMA | ⚠️ |
| 92 | `POST .../pause-station` | `request.TenantId` | ✅ |
| 99 | `POST .../resume-station` | `request.TenantId` | ✅ |

**`src/Modules/Orders/AlGreenMES.Modules.Orders.Api/Controllers/DashboardController.cs`** — svih 5 endpointa (linije 25, 32, 39, 46, 53) primaju `[FromQuery] Guid tenantId`. ✅

**`src/Modules/Orders/AlGreenMES.Modules.Orders.Api/Controllers/NotificationsController.cs`** — **kritično**:

| Linija | Ruta | tenantId source | Status |
|-------:|------|-----------------|--------|
| 28 | `GET /api/notifications?userId=...` | NEMA | 🔴 |
| 47 | `GET /api/notifications/unread-count?userId=...` | NEMA | 🔴 |
| 56 | `PUT /api/notifications/{id}/read` | NEMA | 🔴 |
| 62 | `PUT /api/notifications/{id}/unread` | NEMA | 🔴 |
| 68 | `PUT /api/notifications/read-all?userId=...` | NEMA | 🔴 |
| 75 | `DELETE /api/notifications/{id}` | NEMA | 🔴 |
| 82 | `DELETE /api/notifications?userId=...` | NEMA | 🔴 |

**Razlog 🔴**: query je samo po `userId`. `userId` **nije globalno jedinstven između tenanata** — dva tenanta mogu imati istog korisnika sa istim Guid-om (teoretski malo verovatno za novi Guid, ali model dozvoljava). Ovo je centralna tačka curenja podataka.

**`src/Modules/Orders/AlGreenMES.Modules.Orders.Api/Controllers/BlockRequestsController.cs`**

| Linija | Ruta | tenantId source | Status |
|-------:|------|-----------------|--------|
| 27–40 | `GET /api/block-requests` | `[FromQuery] tenantId` | ✅ |
| 59 | `POST /api/block-requests` | `request.TenantId` | ✅ |
| 69 | `POST /api/block-requests/{id}/approve` | NEMA | ⚠️ |
| 79 | `POST /api/block-requests/{id}/reject` | NEMA | ⚠️ |

**`src/Modules/Orders/AlGreenMES.Modules.Orders.Api/Controllers/ChangeRequestsController.cs`** — slično: linije 28, 60–61, 85 ✅; linije 95, 105 ⚠️.

**`src/Modules/Orders/AlGreenMES.Modules.Orders.Api/Controllers/ReportsController.cs`** — sve 3 rute (linije 25, 34, 49) primaju `tenantId`. ✅

#### Production modul

**`src/Modules/Production/AlGreenMES.Modules.Production.Api/Controllers/ProcessesController.cs`**

| Linija | Ruta | tenantId source | Status |
|-------:|------|-----------------|--------|
| 39–50 | `GET /api/processes` | `[FromQuery] tenantId` | ✅ |
| 68 | `GET /api/processes/{id}` | NEMA | ⚠️ |
| 78 | `POST /api/processes` | `request.TenantId` | ✅ |
| 90 | `PUT /api/processes/{id}` | `request.TenantId` (preko UpdateProcessCommand) | ✅ |
| 104 | `POST /api/processes/reorder` | NEMA | ⚠️ |
| 114 | `DELETE /api/processes/{id}` | NEMA | ⚠️ |
| 125 | `POST .../activate` | NEMA | ⚠️ |
| 135 | `POST .../sub-processes` | NEMA | ⚠️ |
| 146 | `PUT .../sub-processes/{spId}` | NEMA | ⚠️ |
| 157 | `POST .../sub-processes/reorder` | NEMA | ⚠️ |
| 168 | `DELETE .../sub-processes/{spId}` | NEMA | ⚠️ |

**`src/Modules/Production/AlGreenMES.Modules.Production.Api/Controllers/ProductCategoriesController.cs`** — slično obrasac: GET list, POST, eksplicitan tenantId; ostale rute (`{id}` operacije, sub-resources) bez tenantId, ⚠️.

#### Identity modul

**`src/Modules/Identity/AlGreenMES.Modules.Identity.Api/Controllers/UsersController.cs`**

| Linija | Ruta | tenantId source | Status |
|-------:|------|-----------------|--------|
| 33–47 | `GET /api/users` | `[FromQuery] tenantId` | ✅ |
| 64 | `GET /api/users/{id}` | NEMA | ⚠️ |
| 74 | `POST /api/users` | `request.TenantId` | ✅ |
| 91 | `PUT /api/users/{id}` | `request.TenantId` | ✅ |
| 101 | `POST /api/users/{id}/change-password` | NEMA | 🔴 |
| 112 | `POST /api/users/{id}/reset-password` | NEMA | 🔴 |
| (kraj) | `DELETE /api/users/{id}` | NEMA | ⚠️ |

**Razlog 🔴 za password endpoints**: admin može da resetuje password korisniku iz **drugog** tenanta ako zna njegov ID. Treba dodati tenant validaciju.

**`src/Modules/Identity/AlGreenMES.Modules.Identity.Api/Controllers/AuthController.cs`**

| Linija | Ruta | tenantId source | Status |
|-------:|------|-----------------|--------|
| 24 | `POST /api/auth/login` | `request.TenantCode` | ✅ |
| 34 | `POST /api/auth/refresh` | NEMA — samo refresh token | 🔴 |

**Razlog 🔴**: `RefreshTokenRepository.GetByTokenAsync(token)` ne filtrira po tenantId (vidi 1b). Ako bi se ikad dogodio kolizija refresh tokena ili napad, tenantId iz starog tokena ne bi bio validiran.

**`src/Modules/Identity/AlGreenMES.Modules.Identity.Api/Controllers/ShiftsController.cs`** — linije 25, 36, 56 ✅; linija 67 (PUT) bez tenantId ⚠️.

#### Tenancy modul

**`src/Modules/Tenancy/AlGreenMES.Modules.Tenancy.Api/Controllers/TenantsController.cs`** — sve rute baratuju Tenant entitetom kao root aggregate, **nema cross-tenant rizika** ali **ima drugi rizik**: nema autorizacije za super-admin role. Bilo ko sa validnim JWT-om može da listuje sve tenante. Status: 🔴 (autorizacioni gap, ne tenant leak gap).

---

### 1b. Query terminatori (`FindAsync`, `FirstOrDefaultAsync`, itd.)

Klasifikacija svih query terminatora u `src/`:

#### Orders — OrderRepository
**`src/Modules/Orders/AlGreenMES.Modules.Orders.Infrastructure/Repositories/OrderRepository.cs`**

| Linija | Metoda | Filter | Status |
|-------:|--------|--------|--------|
| 22 | `GetByIdAsync(id)` | `o.Id == id` | 🔴 |
| 29 | `GetByIdWithItemsAsync(id)` | `o.Id == id` + Include | 🔴 |
| 41 | `GetByIdWithFullDetailsAsync(id)` | `o.Id == id` + Include chain | 🔴 |
| 45–56 | `GetByTenantIdAsync` | `o.TenantId == tenantId` | ✅ |
| 93–127 | `GetPagedAsync` | `o.TenantId == tenantId` | ✅ |
| 132–207 | `GetPagedWithProcessesAsync` | `o.TenantId == tenantId` | ✅ |

#### Orders — OrderAttachmentRepository
| Linija | Metoda | Filter | Status |
|-------:|--------|--------|--------|
| 18–21 | `GetByOrderIdAsync` | samo OrderId | ⚠️ (indirektan boundary) |
| 23–27 | `GetByOrderItemIdAsync` | samo OrderItemId | ⚠️ |
| 31 | `GetByIdAsync(id)` | samo Id | 🔴 |

#### Orders — ostali repositoriji
| Fajl:Linija | Metoda | Status |
|-------------|--------|--------|
| `PushSubscriptionRepository.cs:20` | `GetByEndpointAsync` (filter: `Endpoint == endpoint && IsActive`) | 🔴 |
| `ChangeRequestRepository.cs:22` | `GetByIdAsync` | 🔴 |
| `BlockRequestRepository.cs:22` | `GetByIdAsync` | 🔴 |
| `BlockRequestRepository.cs:45–49` | `GetPagedAsync` (filter: `TenantId == tenantId`) | ✅ |
| `NotificationRepository.cs:21` | `GetByIdAsync` | 🔴 |
| `NotificationRepository.cs:27–29` | `GetByUserIdAsync` (filter: `UserId == userId`) | 🔴 |
| `OrderItemProcessRepository.cs:20, 27, 37, 47` | razne `GetByIdAsync` varijante | 🔴 |
| `OrderItemProcessRepository.cs:58–67` | `GetInProgressByProcessIdAsync` (filter: TenantId + ProcessId) | ✅ |
| `WorkSessionRepository.cs:21` | `GetByIdAsync` | 🔴 |
| `WorkSessionRepository.cs:27` | `GetActiveByUserIdAsync` (filter: UserId + CheckOutTime null) | 🔴 |
| `WorkSessionRepository.cs:38–44` | `GetByDateAsync` (filter: TenantId + Date) | ✅ |

#### Production
| Fajl:Linija | Metoda | Status |
|-------------|--------|--------|
| `ProcessRepository.cs:21` | `GetByIdAsync` | 🔴 |
| `ProcessRepository.cs:28` | `GetByIdWithSubsAsync` | 🔴 |
| `ProcessRepository.cs:31–38` | `GetByTenantIdAsync` (filter: TenantId) | ✅ |
| `ProductCategoryRepository.cs:21` | `GetByIdAsync` | 🔴 |
| `ProductCategoryRepository.cs:26–34` | `GetByIdWithDetailsAsync` (Include chain) | 🔴 |
| `ProductCategoryRepository.cs:36–42` | `GetByTenantIdAsync` (filter: TenantId) | ✅ |
| `SpecialRequestTypeRepository.cs:21` | `GetByIdAsync` | 🔴 |
| `SpecialRequestTypeRepository.cs:24–30` | `GetByTenantIdAsync` (filter: TenantId) | ✅ |

#### Identity
| Fajl:Linija | Metoda | Status |
|-------------|--------|--------|
| `UserRepository.cs:22` | `GetByIdAsync` | 🔴 |
| `UserRepository.cs:29` | `GetByIdWithProcessesAsync` | 🔴 |
| `UserRepository.cs:37` | `GetByEmailAsync` (Email + TenantId) | ✅ |
| `UserRepository.cs:45` | `GetByEmailWithRolesAsync` (Email + TenantId) | ✅ |
| `RefreshTokenRepository.cs:20` | `GetByTokenAsync(token)` — **nema tenantId** | 🔴 |
| `ShiftRepository.cs:21` | `GetByIdAsync` | 🔴 |
| `TenantLookupService.cs:20` | `GetByCodeAsync` (Code) | ✅ (Tenant je root) |

#### Tenancy
| Fajl:Linija | Metoda | Status |
|-------------|--------|--------|
| `TenantRepository.cs:22` | `GetByIdAsync` | ✅ (Tenant je root aggregate) |
| `TenantRepository.cs:30` | `GetByCodeAsync` | ✅ |

**Predlog popravke** (pattern, ne implementacija): za svaki 🔴 metod u repozitorijima, dodati `Guid tenantId` parametar i `.Where(x => x.TenantId == tenantId)` u LINQ. Alternativno, najbolja opcija: **Global query filter na DbContext** (vidi 1f) — to popravlja sve 🔴 odjednom.

---

### 1c. Include chains

#### `OrderRepository.cs:25–30` — `GetByIdWithItemsAsync`
```csharp
.Include(o => o.Items)
.FirstOrDefaultAsync(o => o.Id == id, ct);
```
**Status: 🔴** — Items nasljeđuju tenant boundary preko Order-a, ali Order sam nema tenant filter.

#### `OrderRepository.cs:32–41` — `GetByIdWithFullDetailsAsync`
```csharp
.Include(o => o.Items)
    .ThenInclude(i => i.Processes)
        .ThenInclude(p => p.SubProcesses)
            .ThenInclude(sp => sp.Logs)
.Include(o => o.Items)
    .ThenInclude(i => i.SpecialRequests)
.FirstOrDefaultAsync(o => o.Id == id, ct);
```
**Status: 🔴** — duboki Include chain bez tenant boundarya na rootu. Items, Processes, SubProcesses, Logs, SpecialRequests svi su tenant-scoped entiteti ali se traversaluju preko Order-a koji nije filtriran.

#### `ProductCategoryRepository.cs:24–34` — `GetByIdWithDetailsAsync`
```csharp
.Include(c => c.Processes).ThenInclude(p => p.Process)
.Include(c => c.Dependencies).ThenInclude(d => d.Process)
.Include(c => c.Dependencies).ThenInclude(d => d.DependsOnProcess)
.FirstOrDefaultAsync(c => c.Id == id, ct);
```
**Status: 🔴** — Procesi i dependencies se traversaluju iz kategorije bez tenant boundarya na rootu.

#### `BlockRequestRepository.cs:45–49` — `GetPagedAsync`
```csharp
.Include(br => br.OrderItemProcess).ThenInclude(p => p!.OrderItem).ThenInclude(i => i.Order)
.Where(br => br.TenantId == tenantId);
```
**Status: ✅** — root je filtriran, traversal je siguran.

---

### 1d. `IgnoreQueryFilters()`

**Rezultat**: 0 instanci u celoj kodebazi. ✅ — niko ne zaobilazi query filtere (jer ih i nema, vidi 1f).

---

### 1e. Raw SQL

**Rezultat**: 0 instanci `ExecuteSqlRaw`, `ExecuteSqlInterpolated`, `FromSqlRaw`, `FromSqlInterpolated`. ✅ — sve ide kroz EF Core, nema raw SQL injection rizika.

---

### 1f. DbContext `HasQueryFilter` — **KRITIČAN NALAZ**

Pregled svakog DbContext-a:

| DbContext | Lokacija | `HasQueryFilter` calls | Tenant-scoped entiteti |
|-----------|----------|------------------------|-----------------------|
| `OrdersDbContext` | `src/Modules/Orders/.../Persistence/OrdersDbContext.cs` | **0** | Order, OrderItem, OrderItemProcess, OrderItemSubProcess, OrderItemSpecialRequest, OrderItemSubProcessLog, WorkSession, ChangeRequest, BlockRequest, Notification, PushSubscription, OrderAttachment |
| `ProductionDbContext` | `src/Modules/Production/.../Persistence/ProductionDbContext.cs` | **0** | Process, SubProcess, ProductCategory, ProductCategoryProcess, ProductCategoryDependency, SpecialRequestType |
| `IdentityDbContext` | `src/Modules/Identity/.../Persistence/IdentityDbContext.cs` | **0** | User, UserProcess, Shift, RefreshToken |
| `TenancyDbContext` | `src/Modules/Tenancy/.../Persistence/TenancyDbContext.cs` | **0** | (samo Tenant, TenantSettings — ne treba filter) |

**Status: 🔴 KRITIČNO** — **nijedan tenant-scoped entitet nema global query filter**. Ovo je arhitektonski izvor svih 🔴 nalaza u sekciji 1b. Svaki `.FirstOrDefaultAsync(x => x.Id == id)` bez explicit tenantId filtera curi.

**Predlog pattern-a**: u svakom DbContext-u, u `OnModelCreating`:
```csharp
var tenantId = _tenantService.GetCurrentTenantId();
modelBuilder.Entity<Order>().HasQueryFilter(o => o.TenantId == tenantId);
modelBuilder.Entity<OrderItem>().HasQueryFilter(i => i.TenantId == tenantId);
// ... za svaki tenant entitet
```
Zahteva injection `ITenantService` u DbContext (preko konstruktora). Ovo je jedan od najvažnijih posledica ovog audita.

---

### 1g. SignalR hub-ovi

#### `ProductionHub`
**Lokacija**: `src/Modules/Orders/AlGreenMES.Modules.Orders.Api/Hubs/ProductionHub.cs`
**Registracija**: `Program.cs:128` na ruti `/hubs/production`
**Autorizacija**: `[Authorize]` (linija 10)

**`OnConnectedAsync` (linije 20–45)**:
- Linija 22: čita `tenant_id` claim iz JWT-a.
- Linija 25: `Groups.AddToGroupAsync(connectionId, $"tenant-{tenantId}")` — ✅ tenant grupa.
- Linije 29–41: učitava korisnikove dodeljene procese, dodaje connection u `process-{processId}` grupu za svaki — ✅ procesi vezani za korisnika u DB-u.

**Manuelne grupne metode (linije 53–71)**:
- `JoinTenantGroup(string tenantId)` (53–56): **🔴 ne validira da li poslati `tenantId` odgovara JWT claim-u korisnika**. Korisnik može da pozove `JoinTenantGroup("drugi-tenant-guid")` i prima tuđe broadcastove.
- `LeaveTenantGroup` (58–61): isti rizik u suprotnom smeru.
- `JoinProcessGroup(string processId)` (63–66): **⚠️ ne validira da je korisnik dodeljen procesu**. Korisnik može da slušaprocese kojima nije dodeljen.
- `LeaveProcessGroup` (68–71): manje opasno.

**Predlog pattern-a**:
```csharp
public async Task JoinTenantGroup(string tenantId)
{
    var jwtTenantId = Context.User?.FindFirst("tenant_id")?.Value;
    if (jwtTenantId != tenantId) throw new HubException("Forbidden");
    await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant-{tenantId}");
}
```

**Backplane**: in-memory (default). Linija u `Program.cs` registruje `AddSignalR()` bez `.AddStackExchangeRedis()`. ✅ za jedan box; **🔴 prepreka za horizontalno skaliranje** (vidi sekciju 8).

**Connection lifecycle**:
- `OnDisconnectedAsync` (linije 47–50): nema explicit cleanup; SignalR auto-uklanja connection iz grupa. ✅
- **Nema resilience logike** (auto-reconnect je na klijentskoj strani, vidi `packages/signalr-client` u frontend monorepo-u).

---

### 1h. Background work (hosted services, scheduled tasks)

#### `DeadlineWarningService`
**Lokacija**: `AlgreenMES.API/Services/DeadlineWarningService.cs`
**Registracija**: `Program.cs:89` `AddHostedService<DeadlineWarningService>()`

**Pattern**:
- Linija 14: ciklus svakih 30 minuta (`Task.Delay(TimeSpan.FromMinutes(30))`).
- Linija 47: `using var scope = _scopeFactory.CreateScope()` — pravilan scoped pattern za background services.
- Linije 52–55: `tenancyDb.Tenants.Where(t => t.IsActive).ToListAsync()` — listuje sve tenante eksplicitno.
- Linije 57–120: petlja po tenantima, za svaki:
  - Linija 59–61: učitava tenant settings.
  - Linija 66–71: `.Where(o => o.TenantId == tenant.Id)` — eksplicitan tenant filter. ✅
  - Linija 75–120: provera deadline-a, broadcast notifikacija po tenantu.

**Status: ✅** — pravilno baratanje tenant kontekstom u background pos lu. Iteracija po tenantima je explicit.

#### Ostali servisi u `Program.cs`
| Linija | Servis | Lifetime | Tenant context |
|-------:|--------|----------|----------------|
| 81 | `ITenantService → TenantService` | Scoped | Čita iz HttpContext-a — ✅ za request scope |
| 84 | `IProductionEventService` | Scoped | ✅ |
| 85 | `IProcessChangeNotifier` | Scoped | ✅ |
| 86 | `IReferenceCheckService` | Scoped | ✅ |

**Status: ✅** — sve scoped abstraction-e dobijaju tenantId iz request context-a.

---

### 1i. File storage paths

**Trenutni format**: `{BasePath}/orders/{tenantId}/{orderId}/{stored-guid}.{ext}`

**Lokacija builderom puta**:
**`src/Modules/Orders/.../UploadOrderAttachment/UploadOrderAttachmentCommandHandler.cs:85–86`**
```csharp
var storedFileName = $"{Guid.NewGuid()}{extension}";
var relativePath = Path.Combine("orders", request.TenantId.ToString(), request.OrderId.ToString(), storedFileName);
```

**Komponente**:
- `BasePath` (`FileStorageSettings.cs:5`): default `./uploads`, posle hardenga iz prethodnog taska postavljen na `/opt/algreen/uploads` u produkciji.
- `orders/` — hardcoded prefix.
- `{tenantId:Guid}` — **tenant izolacija na nivou direktorijuma** ✅
- `{orderId:Guid}` — order izolacija
- `{Guid.NewGuid()}.{ext}` — random ime, neguessable ✅

**Path traversal protection**: `LocalFileStorageService.GetSafePath()` (linije 56–62):
```csharp
var fullPath = Path.GetFullPath(Path.Combine(_basePath, relativePath));
if (!fullPath.StartsWith(_basePath, StringComparison.OrdinalIgnoreCase))
    throw new UnauthorizedAccessException("Invalid file path.");
```
**Status: ✅**.

**Tenant validacija u handleru**:
- Linija 40–41: `if (order.TenantId != request.TenantId) throw new DomainException(...)` ✅

**Download endpoint** (`OrdersController.cs:282–299`):
- Linija 284: `_attachmentRepository.GetByIdAsync(id)` — **🔴 nema tenant filter** (vidi 1b).
- Linija 285: `if (attachment.OrderId != orderId) return NotFound();` — proverava order match ali **ne tenant match**.
- Predlog: dodati `if (attachment.TenantId != currentTenantId) return NotFound();`.

---

## 2. Database & migration workflow

### 2a. Gde i kako se pokreću migracije

**Lokacija**: `AlgreenMES.API/Program.cs:130–138`
```csharp
using var migrationScope = app.Services.CreateScope();
var sp = migrationScope.ServiceProvider;
await sp.GetRequiredService<TenancyDbContext>().Database.MigrateAsync();
await sp.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
await sp.GetRequiredService<ProductionDbContext>().Database.MigrateAsync();
await sp.GetRequiredService<OrdersDbContext>().Database.MigrateAsync();
```

**Status**: migracije se izvršavaju **na startup-u aplikacije**, sekvencijalno, za sva 4 DbContext-a. `deploy.sh` ne poziva `dotnet ef database update` — sve je oslonjeno na startup.

**Rizici**:
- Ako migracija pukne, app se ne diže. Customer nema sistem dok se ne intervene ručno.
- Neplanirana migracija u staging-u + auto-apply na prod = potencijalna katastrofa.
- Multi-instance scenario (kada budemo skalirali horizontalno): dva istovremena startup-a mogu da pokreću `Migrate()` paralelno — race condition. Trebaće migration lock ili izdvojen migration step.

### 2b. Lista migracija

#### Tenancy modul
`src/Modules/Tenancy/AlGreenMES.Modules.Tenancy.Infrastructure/Migrations/`

| Datum | Fajl | Opis |
|-------|------|------|
| 2026-02-10 | `20260210170702_InitialCreate.cs` | Schema `tenancy`. Tabele: `tenants` (id, name, code, is_active, created_at, updated_at), `tenant_settings` (default_warning_days, default_critical_days, warning_color, critical_color) |
| 2026-03-05 | `20260305132112_AddAuditColumnsToTenantSettings.cs` | Audit columns na `tenant_settings` |

#### Identity modul
`src/Modules/Identity/AlGreenMES.Modules.Identity.Infrastructure/Migrations/`

| Datum | Fajl | Opis |
|-------|------|------|
| 2026-02-10 | `20260210173613_InitialIdentity.cs` | Schema `identity`. Tabele: `users`, `refresh_tokens`, `shifts` |
| 2026-02-10 | `20260210203332_AddCanIncludeWithdrawnToUser.cs` | Boolean kolona `can_include_withdrawn_in_analysis` |
| 2026-03-18 | `20260318193618_MultipleProcessesPerUser.cs` | Drop `users.process_id`, dodaje `user_processes` junction tabelu |

#### Production modul
| Datum | Fajl | Opis |
|-------|------|------|
| 2026-02-10 | `20260210202952_InitialProduction.cs` | Schema `production`. Tabele: `processes`, `sub_processes`, `product_categories`, `product_category_processes`, `product_category_dependencies`, `special_request_types` |
| 2026-03-05 | `20260305123913_FilterSubProcessUniqueIndex.cs` | Tenant-scoped unique index na `sub_processes` |
| 2026-03-05 | `20260305133605_RemoveDefaultValueSentinels.cs` | Cleanup |
| 2026-03-10 | `20260310173817_AddCategoryWarningCriticalDays.cs` | Per-category deadline override polja |

#### Orders modul
| Datum | Fajl | Opis |
|-------|------|------|
| 2026-02-10 | `20260210203013_InitialOrders.cs` | Schema `orders`. Tabele: `orders`, `order_items`, `order_item_processes`, `order_item_sub_processes`, `order_item_sub_process_logs`, `notifications`, `work_sessions`, `change_requests`, `block_requests` |
| 2026-02-13 | `20260213221415_ChangeWorkSessionDateToDateOnly.cs` | Tip izmena |
| 2026-02-24 | `20260224115557_ReplaceWorkSessionIdWithUserId.cs` | FK izmena |
| 2026-02-24 | `20260224123139_AddPushSubscriptionTable.cs` | Web push subscriptions |
| 2026-02-24 | `20260224164355_RemoveDefaultValueSentinels.cs` | Cleanup |
| 2026-02-25 | `20260225105542_AddOrderAttachments.cs` | Order attachments tabela |
| 2026-03-05 | `20260305132102_AddAuditColumnsToMutableEntities.cs` | Audit columns |
| 2026-03-10 | `20260310183743_AddOrderItemIdToAttachment.cs` | Item-level attachments |
| 2026-03-11 | `20260311172709_AddPauseResumeToOrderItemProcess.cs` | Pause/resume tracking |
| 2026-03-18 | `20260318193653_RemoveWorkSessionProcessId.cs` | Pratič `MultipleProcessesPerUser` |
| 2026-04-23 | `20260423082710_AddCompletedAtToOrder.cs` | Completed timestamp |
| 2026-04-23 | `20260423085246_AddIsInvoicedToOrder.cs` | Invoiced flag |

**Ukupno**: 21 migracija u 4 modula. Svaki modul ima **svoju `__EFMigrationsHistory` tabelu** u svojoj šemi (modulna izolacija ✅).

### 2c. Tabele BEZ tenant_id

Pregled svih 24 entiteta:

| Entitet | Schema.Table | Has tenant_id? | Status |
|---------|--------------|----------------|--------|
| Tenant | tenancy.tenants | NO | ✅ root aggregate |
| TenantSettings | tenancy.tenant_settings | YES (FK) | ✅ |
| User | identity.users | YES | ✅ |
| UserProcess | identity.user_processes | YES | ✅ |
| Shift | identity.shifts | YES | ✅ |
| RefreshToken | identity.refresh_tokens | YES | ✅ |
| Order | orders.orders | YES | ✅ |
| OrderItem | orders.order_items | YES | ✅ |
| OrderItemProcess | orders.order_item_processes | YES | ✅ |
| OrderItemSubProcess | orders.order_item_sub_processes | YES | ✅ |
| OrderItemSubProcessLog | orders.order_item_sub_process_logs | YES | ✅ |
| OrderItemSpecialRequest | orders.order_item_special_requests | YES | ✅ |
| OrderAttachment | orders.order_attachments | YES | ✅ |
| WorkSession | orders.work_sessions | YES | ✅ |
| ChangeRequest | orders.change_requests | YES | ✅ |
| BlockRequest | orders.block_requests | YES | ✅ |
| Notification | orders.notifications | YES | ✅ |
| PushSubscription | orders.push_subscriptions | YES | ✅ |
| Process | production.processes | YES | ✅ |
| SubProcess | production.sub_processes | YES | ✅ |
| ProductCategory | production.product_categories | YES | ✅ |
| ProductCategoryProcess | production.product_category_processes | YES | ✅ |
| ProductCategoryDependency | production.product_category_dependencies | YES | ✅ |
| SpecialRequestType | production.special_request_types | YES | ✅ |

**Zaključak**: **svih 24 entiteta** imaju `tenant_id` (osim `Tenant` koji ne treba). Schema je čista, problem nije u modelu — problem je u **upitima koji ga ne koriste** (vidi 1b, 1f).

### 2d. Cross-tenant FK rizici

| FK | Lokacija | Status |
|----|----------|--------|
| `OrderItemProcess.process_id → Process.id` | cross-modul (orders → production) | ⚠️ — oba imaju tenant_id ali nema check constraint da se poklapaju |
| `ProductCategoryDependency.process_id → Process.id` | unutar Production modula | ⚠️ — isti rizik |
| `ProductCategoryDependency.depends_on_process_id → Process.id` | Production | ⚠️ |
| `OrderItemSubProcess.order_item_process_id → OrderItemProcess.id` | unutar Orders | ⚠️ |
| `RefreshToken.user_id → users.id` | Identity | 🔴 — **FK constraint NIJE eksplicitan u migraciji** (`20260210173613_InitialIdentity.cs`). Postoji kolona ali ne FK. |

**Predlog pattern-a**: dodati composite FK + check constraint na `(tenant_id, parent_id)` da se osigura da parent i child dele tenant. Alternativno, application-level invariant proverava se u command handleru pre svakog inserta.

### 2e. Backup setup

**Pretraga**:
- `deploy.sh` (1–14): nema backup poziva.
- `Dockerfile`: nema.
- `appsettings*.json`: nema backup config-a.
- Repo nema `*.sh` fajlova vezanih za backup.

**Status: ❌ MISSING** — nema bilo kakve automatizovane backup infrastrukture u repo-u. Pretpostavka: postoji nešto na serveru ručno, ali nije pod version control-om.

---

## 3. Auth & identity layer

### 3a. JWT → tenant flow

**Issuance**: `src/Modules/Identity/.../Services/JwtTokenService.cs:21–49`
```csharp
new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
new Claim(JwtRegisteredClaimNames.Email, user.Email),
new Claim("tenant_id", user.TenantId.ToString()),  // <-- custom claim
new Claim(ClaimTypes.Role, user.Role.ToString()),
new Claim("first_name", user.FirstName),
new Claim("last_name", user.LastName)
```

**Login**: `src/Modules/Identity/.../Login/LoginCommandHandler.cs:37–69`
1. Tenant lookup po `TenantCode` (case-insensitive `ToUpperInvariant()`)
2. User lookup po `Email + tenant_id`
3. Password verify (BCrypt)
4. JWT generation sa `tenant_id` claim
5. RefreshToken kreiran sa tenant_id (ali u DB-u, vidi 2d)

**Validation**: `Program.cs:48–76`
```csharp
ValidateIssuer = true,
ValidateAudience = true,
ValidateLifetime = true,
ValidateIssuerSigningKey = true,
```
**Linije 62–75**: SignalR query string token passthrough za `/hubs/*` i `/attachments/*` rute (potrebno jer browser WebSocket ne šalje custom Authorization header).

**Resolution**: `AlgreenMES.API/Services/TenantService.cs:15–22`
```csharp
public Guid GetCurrentTenantId()
{
    var tenantIdClaim = _httpContextAccessor.HttpContext?.User?.FindFirst("tenant_id");
    if (tenantIdClaim is null || !Guid.TryParse(tenantIdClaim.Value, out var tenantId))
        return Guid.Empty;  // <-- silent fallback
    return tenantId;
}
```

**⚠️ Nalaz**: `Guid.Empty` se vraća tiho kada je claim missing/invalid. Pozivajući kod dobije `Guid.Empty` i može da ga koristi u upitima — što je suptilno opasno. Trebalo bi `throw UnauthorizedException`.

### 3b. Role checks

**Enum**: `src/Modules/Identity/.../Domain/Entities/UserRole.cs:3–10`
```csharp
public enum UserRole { Admin, Manager, Coordinator, SalesManager, Department }
```

**`[Authorize(Roles = "...")]` lista**:

| Fajl | Roles | Broj endpointa |
|------|-------|----------------|
| `Identity/.../ShiftsController.cs` | `Admin,Manager` | 2 |
| `Identity/.../UsersController.cs` | `Admin,Manager` | 1 |
| `Identity/.../UsersController.cs` | `Admin` | 4 |
| `Orders/.../BlockRequestsController.cs` | `Admin,Manager,Coordinator` | 2 |
| `Orders/.../OrdersController.cs` | `Admin,Manager,SalesManager` | 2 |
| `Orders/.../OrdersController.cs` | `Admin,Manager,Coordinator` | 5 |
| `Orders/.../OrdersController.cs` | `Admin,Manager` | 2 |
| `Orders/.../OrdersController.cs` | `Admin,Manager,SalesManager,Coordinator` | 1 |
| `Orders/.../ProcessWorkflowController.cs` | `Admin,Manager,Coordinator` | 3 |
| `Orders/.../ChangeRequestsController.cs` | `Admin,Manager,SalesManager` | 1 |

**Case-sensitivity**: ASP.NET Core po defaultu **jeste case-sensitive** za `[Authorize(Roles=...)]`. JWT generiše `user.Role.ToString()` što daje `"Admin"`, `"Manager"`, itd. — match je tačan. Ali frontend role check (po memoriji) je case-insensitive — to je samo na klijentskoj strani.

**`User.IsInRole(...)` ili `HasClaim(...)` u kodu**: 0 instanci. Sve preko `[Authorize]`.

### 3c. Current-user / current-tenant abstrakcije

**Postoji**:
- `ITenantService` u `src/BuildingBlocks/AlGreenMES.BuildingBlocks.Common/Interfaces/ITenantService.cs:6–9`
  ```csharp
  public interface ITenantService { Guid GetCurrentTenantId(); }
  ```
- Implementacija: `AlGreenMES.API/Services/TenantService.cs`
- Registracija: `Program.cs:81` — Scoped.

**NE postoji**:
- `ICurrentUserService` ili sl. — nema dedikovanu abstrakciju za `userId`, `email`, `role`.
- Kod čita direktno iz `User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value` na više mesta — **nekonzistentnost**.

**Inkonzistentnost mesta**:
- Neki controlleri injectuju `ITenantService` i koriste `_tenantService.GetCurrentTenantId()`.
- Drugi parsiraju iz `request.TenantId` (request body/query).
- Treći (kao OrdersController.cs:262 za upload) parsiraju userId direktno iz JWT claim-a u akciji.

**Predlog**: uvesti `ICurrentUserService` sa metodama `GetUserId()`, `GetEmail()`, `GetRole()`, `GetTenantId()` — single source of truth.

### 3d. Impersonation

**Pretraga**: `impersonat`, `sudo`, `act.as.user` — 0 rezultata.

**Status: ❌ MISSING (greenfield)** — admin "log in as another user" / "switch into tenant X" logika ne postoji. Ovo je posao koji će super-admin panel zahtevati od nule.

---

## 4. File storage layer

### 4a. `IFileStorageService`

**Interface**: `src/Modules/Orders/.../Application/Interfaces/IFileStorageService.cs:1–9`
```csharp
public interface IFileStorageService
{
    Task<string> SaveFileAsync(string relativePath, Stream fileStream, CancellationToken ct);
    Task<Stream?> GetFileAsync(string relativePath, CancellationToken ct);
    Task DeleteFileAsync(string relativePath, CancellationToken ct);
    Task DeleteDirectoryAsync(string relativePath, CancellationToken ct);
}
```

**Jedina implementacija**: `LocalFileStorageService` u `src/Modules/Orders/.../Infrastructure/Services/LocalFileStorageService.cs:6–63`

**Bypass-evi**: pretraga za `File.WriteAll`, `File.Create`, `StreamWriter`, `FileStream` van `LocalFileStorageService` — 0 rezultata. ✅ — niko ne piše direktno na disk.

### 4b. Upload path format

(Detaljno u sekciji 1i.) Sažeto: `{BasePath}/orders/{tenantId:guid}/{orderId:guid}/{guid}.{ext}`.

### 4c. Filename predictability

- Disk filename: **`Guid.NewGuid()` + ekstenzija** — neguessable. ✅
- Original filename čuva se u DB-u (`OrderAttachment.OriginalFileName`), ne na disku.
- Direktorijumski deo (`orders/{tenantId}/{orderId}/`) je **predvidljiv** ako napadač zna tenant + order ID — ali listing direktorijuma se ne servira preko API-ja, i path traversal je sprečen (1i).

### 4d. Validacija na upload

**Lokacija**: `FileStorageSettings.cs:3–15` + `UploadOrderAttachmentCommandHandler.cs:34–106` + `OrdersController.cs` upload endpoint.

| Provera | Limit | Lokacija |
|---------|-------|----------|
| Max file size | 10 MB | `FileStorageSettings.cs:6` + handler:64–65 + `[RequestSizeLimit(10*1024*1024)]` na controller-u |
| Allowed extensions | .jpg, .jpeg, .png, .pdf | `FileStorageSettings.cs:8` + handler:60–61 |
| Allowed content types | image/jpeg, image/png, application/pdf | `FileStorageSettings.cs:9–14` + handler:45–46 |
| Max files per order/item | 10 | `FileStorageSettings.cs:7` + handler:81–82 |
| Order existence + tenant match | — | handler:36–41 |
| Item ownership (kada je item-level) | — | handler:68–73 |
| **Virus scan** | — | ❌ **MISSING** |

---

## 5. Deployment & ops

### 5a. `deploy.sh` u celini

**Backend** (`/mnt/d/Projects/AlgreenMES/deploy.sh`):
```bash
#!/bin/bash
set -e

echo "🔨 Building backend..."
dotnet publish AlgreenMES.API/AlgreenMES.API.csproj -c Release -o ./publish

echo "📦 Uploading to server..."
rsync -az --delete --exclude='appsettings.Production.json' ./publish/ root@46.101.166.137:/opt/algreen/api/

echo "🔄 Restarting API..."
ssh root@46.101.166.137 "systemctl restart algreen-api"

echo "✅ Backend deployed!"
```

**Frontend** (`/mnt/d/Projects/AlgreenMES front/algreen-tracker/deploy.sh`):
```bash
#!/bin/bash
set -e
export VITE_API_BASE_URL=https://tracker-api.algreen.rs/api
export VITE_SIGNALR_URL=https://tracker-api.algreen.rs/hubs/production
TARGET=${1:-all}

if [ "$TARGET" = "dashboard" ] || [ "$TARGET" = "all" ]; then
  pnpm --filter dashboard build
  rsync -az --delete apps/dashboard/dist/ root@46.101.166.137:/opt/algreen/dashboard/
fi

if [ "$TARGET" = "tablet" ] || [ "$TARGET" = "all" ]; then
  pnpm --filter tablet build
  rsync -az --delete apps/tablet/dist/ root@46.101.166.137:/opt/algreen/tablet/
fi
```

Postoji i `deploy-windows.sh` koja koristi `scp.exe` umesto rsync-a (Windows kompatibilnost).

### 5b. systemd unit fajlovi

**Pretraga**: `*.service` u repo-u → 0 rezultata. systemd unit `algreen-api.service` postoji **samo na serveru** (`/etc/systemd/system/algreen-api.service`) i **nije pod version control-om**.

**Status: ⚠️** — gubimo single source of truth za infrastrukturni config.

### 5c. Health check endpoint

**Pretraga**: `MapHealthChecks`, `AddHealthChecks` u `Program.cs` → 0 rezultata.

**Status: ❌ MISSING** — nema `/health`, `/health/live`, `/health/ready`. Load balancer i monitoring ne mogu razlikovati "app radi" od "app je u zombie state-u".

### 5d. Frontend build artifacts

**Workspace**: `/mnt/d/Projects/AlgreenMES front/algreen-tracker/pnpm-workspace.yaml`
```yaml
packages:
  - 'packages/*'
  - 'apps/*'
```

**Build skripte** (`package.json`):
- `pnpm --filter dashboard build` → `apps/dashboard/dist/`
- `pnpm --filter tablet build` → `apps/tablet/dist/`

**Razlikovanje dashboard vs tablet**:
- Dashboard: standardan React + Vite, port 5931 u dev. Dependencies generičke. Build → static SPA.
- Tablet: extra deps u `package.json`:
  - `nosleep.js` (drži ekran budnim na tabletu)
  - `pdfjs-dist` (PDF preview)
  - `workbox-precaching`, `vite-plugin-pwa` (PWA / offline)
  - `tailwindcss`
- Tablet `vite.config.ts:1–48`: `VitePWA` plugin sa manifestom, `display: 'standalone'`, `orientation: 'portrait'`, theme color `#2e7d32`.

**Production deploy**:
- Oba app-a idu na isti server (`46.101.166.137`).
- Različiti direktorijumi: `/opt/algreen/dashboard/` vs `/opt/algreen/tablet/`.
- Pretpostavka: nginx ispred radi reverse proxy po path/host-u (nije u repo-u).
- Oba app-a koriste **isti API endpoint** (`https://tracker-api.algreen.rs/api`).

### 5e. Environment variables

**Backend `appsettings.json`** (root):
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=algreen_mes;Username=postgres;Password=postgres"
  },
  "JwtSettings": {
    "Secret": "CHANGE_ME_IN_PRODUCTION_USE_A_LONG_SECRET_KEY",
    "Issuer": "AlGreenMES",
    "Audience": "AlGreenMES",
    "ExpirationMinutes": 60
  },
  "WebPush": {
    "VapidPublicKey": "",
    "VapidPrivateKey": "",
    "VapidSubject": "mailto:support@algreen.rs",
    "Enabled": false
  }
}
```

**Backend `appsettings.Development.json`**:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5433;Database=algreen_mes;Username=milosmitrovic;Password="
  },
  "WebPush": {
    "VapidPublicKey": "BBT7Tg_EjRRdkStpspknQw3ckEkIaqUoAi3mLyijZNpgMk_tcHAEzliINq-egodJBn0F0YmXDA4pLo64jvAcRLU",
    "VapidPrivateKey": "Qu5p3q01SxtvUvbdkJlwses5eOOhbnLsdxo0MOAF3Mo",
    "VapidSubject": "mailto:support@algreen.rs",
    "Enabled": true
  }
}
```

**🔴 SECURITY FINDING**: WebPush `VapidPrivateKey` je commit-ovan u repo-u. Mora se rotirati i preseliti u env var ili secrets manager.

**Frontend env**:
- `VITE_API_BASE_URL` — `packages/api-client/src/axios-instance.ts:5` — default `http://localhost:5030/api`, override iz `deploy.sh`.
- `VITE_SIGNALR_URL` — set u `deploy.sh`.
- **Ne postoji**: `VITE_ENVIRONMENT`, `VITE_TENANT_ID`, ni feature flags.

---

## 6. Pilot-specific code paths

### 6a. Hardcoded factory imena, ID-jevi, tenant slug-ovi

**`AlgreenMES.API/Services/DataSeeder.cs:28–35`**:
```csharp
var tenant = await tenancyDb.Tenants.FirstOrDefaultAsync(t => t.Code == "DEMO");
if (tenant == null)
{
    tenant = Tenant.Create("Demo Company", "demo");
    tenancyDb.Tenants.Add(tenant);
    await tenancyDb.SaveChangesAsync();
}
```
- Tenant code `"DEMO"` i ime `"Demo Company"` su hardcoded.
- Ovo je **demo** seed, ne pilot-specifičan po imenu — ali ceo dataset koji se posle veže za njega **jeste pilot-specifičan** (vidi 6e).

**Procesi (linije 60–72)** — sve na srpskom: Krojenje, Kantiranje, CNC, Bušenje, Montaža, Brušenje, Grundiranje, Farbanje, Lakiranje, Pakiranje, Kontrola kvaliteta. Ovo je **drvna industrija** — hardcoded za jedan tip fabrike. Drugi klijent koji nije drvna industrija ne može da koristi ovu seed.

**Kategorije proizvoda (linije 96–126, 249–293)**: "Vrata Pivot", "Vrata Standard", "Prozori" — vrlo specifično za jednog proizvođača vrata/prozora.

**Special request types (linije 129–148)**: "PESK" (Peskarenje), "SF" (Samo farbanje) — opet specifično za drvnu/proizvodnu industriju.

### 6b. `if (tenant.Name == "...")` patterni

**Pretraga**: 0 rezultata. ✅ — nema branch logike po tenant imenu.

### 6c. Custom workflows / business pravila

- **Process dependencies** (linije 112–123, 615–624): "Grundiranje zavisi od Brušenja", "Farbanje zavisi od Grundiranja", "Lakiranje zavisi od Farbanja". Ovo je **podaci, ne kod** — seedovano u `product_category_dependencies` tabeli, što je dobro (data-driven, ne hardcoded).
- **Special request types** ("PESK", "SF"): isto, data-driven preko `special_request_types` tabele.
- **Order types** (`OrderType.Standard`, `Repair`, `Complaint`): enum, generičan ✅.

**Zaključak**: workflow je dobro **modeliran kao podatak** — premeštanje na nove tenante je stvar drugačijeg seed dataset-a, ne code change-a. To je velika prednost arhitekture.

### 6d. Hardcoded role names

`UserRole` enum: Admin, Manager, SalesManager, Coordinator, Department. Ovo izgleda **standardno**, ne factory-specifično. ✅

### 6e. `DataSeeder.cs` — duboka analiza

**Lokacija**: `AlgreenMES.API/Services/DataSeeder.cs:15–626` (610+ linija seed koda)

**Šta seeduje**:

| Linije | Entitet | Količina | Idempotent? |
|--------|---------|----------|-------------|
| 29–35 | Tenant ("DEMO") | 1 | ✅ |
| 40–190 | Korisnici (admin@demo.com + 7 radnika) | 8 | ✅ |
| 56–94 | Procesi (A–K) | 11 | ✅ |
| 96–293 | Product Categories (3 sa procesima i dependencies) | 3 | ✅ |
| 129–148 | Special Request Types | 2 | ✅ |
| 193–198 | Smene (Jutarnja, Popodnevna, Noćna) | 3 | ✅ |
| 299–428 | Demo Orders (ORD-2026-001 do 008) | 8 | ✅ (proverava prvi) |
| 517–541 | Work Sessions | 3 | ❌ **NIJE** |
| 543–560 | Block Requests | 2 | ❌ **NIJE** |
| 562–583 | Change Requests | 3 | ❌ **NIJE** |
| 585–609 | Notifikacije | 4 | ❌ **NIJE** |

**Klasifikacija**:
- ✅ Tenants, processes, categories, special requests, shifts, users, orders: **idempotent** (provera `FirstOrDefault` pre inserta).
- ❌ Work sessions, block requests, change requests, notifications: **NIJE idempotent** — ponovni run će dupli ranje.

**Pilot-specific deo** (mora da postane per-tenant config, ne hardcoded u seederu):
- Procesi A–K (drvna industrija)
- Kategorije ("Vrata Pivot", "Vrata Standard", "Prozori")
- Special request types ("PESK", "SF")
- Smene (lokal vremenske zone i naming)

**Generičan baseline (može ostati globalno)**:
- UserRole enum (Admin, Manager, etc.)
- OrderType enum
- Audit columns logic

**Predlog refactoring-a**:
- DataSeeder se cepa u dva: `BaselineSeeder` (univerzalne stvari) i `TenantSeeder(tenantId, templateName)` (per-tenant template, npr. "drvna-industrija", "metalska-industrija", "tekstil"). Super-admin panel poziva `TenantSeeder` sa željenim template-om.
- Demo data (work sessions, block requests, itd.) se izoluje u `DemoDataSeeder` koji se pokreće samo u Development environment-u.

---

## 7. Real-time layer (SignalR)

### 7a. Lista hub-ova

**Samo jedan hub**: `ProductionHub` u `src/Modules/Orders/.../Hubs/ProductionHub.cs`.

**Url**: `/hubs/production`

**Metode** (klijentske invoke):
- `JoinTenantGroup(string tenantId)` — linija 53–56 — **🔴 nedostaje validacija** (vidi 1g)
- `LeaveTenantGroup(string tenantId)` — 58–61
- `JoinProcessGroup(string processId)` — 63–66 — **⚠️**
- `LeaveProcessGroup(string processId)` — 68–71

**Server-to-client poruke** (preko `ProductionEventService` i `ProcessChangeNotifier`):
- `OrderUpdated`, `OrderItemProcessUpdated`, `OrderItemSubProcessUpdated`
- `WorkSessionUpdated`
- `BlockRequestCreated/Approved/Rejected`
- `ChangeRequestCreated/Approved/Rejected`
- `NotificationCreated`
- `DeadlineWarning` (iz `DeadlineWarningService`)

### 7b. Backplane

**Konfiguracija**: `Program.cs` poziva `builder.Services.AddSignalR()` bez `.AddStackExchangeRedis(...)`. **In-memory backplane.** ✅ za jedan instance app-a; **🔴 prepreka za horizontalno skaliranje** (jedan klijent konektovan na instance A neće dobiti broadcast iz instance B).

### 7c. Connection lifecycle

- `OnConnectedAsync` (linije 20–45): automatski join u `tenant-{tenantId}` i `process-{processId}` grupe.
- `OnDisconnectedAsync` (linije 47–50): SignalR auto-cleanup grupa, eksplicitan kod nije potreban.
- **Klijentska resilience**: `packages/signalr-client` u frontend monorepo-u — koristi `@microsoft/signalr` koji ima ugrađen `withAutomaticReconnect()` (default delay sekvenca: 0, 2, 10, 30s). Posle 30s-og pokušaja, app mora ručno re-connect.

### 7d. Message volume

**Logging**: ne postoji eksplicitno logiranje broja SignalR poruka. Default Microsoft.AspNetCore.SignalR logger samo ima `Information` level event-e za connect/disconnect. Bez OpenTelemetry ili Prometheus metrics-a, pravi message volume nije observable.

---

## 8. Šta NE postoji (gaps)

| Feature | Status | Why it matters at multi-tenant scale |
|---------|--------|--------------------------------------|
| **Rate limiting** (`AddRateLimiter`) | ❌ | Brute-force / DoS na login, abuse od strane jednog tenanta gasi sve |
| **Audit log tabela** (changes-tracking) | ❌ | Audit kolone (`CreatedBy`, `UpdatedBy`) postoje na `AuditableEntity` ali **nisu populated** — nema `SaveChangesInterceptor` koji ih puni. Nema istorije promena. |
| **`tenant_features` tabela** | ❌ | Per-tenant feature flags su nužni za prodaju različitih nivoa proizvoda |
| **Per-tenant resource quotas** | ❌ | Jedan tenant može da napuni disk, eats DB connections, itd. |
| **Soft delete** (`IsDeleted` + query filter) | ❌ | Hard delete je trenutni pattern. GDPR i recovery scenariji teško |
| **Idempotency keys** na mutacijama | ❌ | Network retry → duple narudžbine, duple notifikacije |
| **Correlation IDs** u logovima | ❌ | Distribuirani tracing, debug request-a kroz API → DB → SignalR |
| **Strukturalno logging** (Serilog JSON) | ❌ | Default `Microsoft.Extensions.Logging` text format, nema parseabilnih logova |
| **OpenTelemetry / distributed tracing** | ❌ | Nema instrumentacije |
| **API versioning** (`/api/v1/...`) | ❌ | Breaking change-evi će biti bolni |
| **Swagger u prod** | ✅ Samo dev | `Program.cs:120–121` — `app.MapOpenApi()` u `if (Environment.IsDevelopment())` |
| **CORS** | 🔴 **OVERLY PERMISSIVE** | `Program.cs:102–111`: `SetIsOriginAllowed(_ => true).AllowCredentials()` — **CSRF risk** |
| **HTTPS redirect** | ✅ | `Program.cs:123` `UseHttpsRedirection()` |
| **HSTS** | ❌ | Nema `UseHsts()` |
| **Sentry / error tracking** | ❌ | Greške u produkciji niko ne vidi dok se klijent ne javi |
| **DB connection retry** (`EnableRetryOnFailure`) | ❌ | Transient PG outage = trenutni fail; sa retry-em se mnogo restorira |
| **Migration history per modul** | ✅ | Svaki DbContext ima svoj `__EFMigrationsHistory` u svojoj šemi |
| **Seed idempotency** | ⚠️ | Baseline jeste, demo data nije |
| **Global request size limit** | ⚠️ | Per-endpoint `[RequestSizeLimit]` na upload, ali nema globalnog default-a |
| **Virus scan na upload** | ❌ | 10 MB attachment-i bez ikakve provere |

---

## Sumarna lista nalaza po kritičnosti

### 🔴 KRITIČNO (popraviti pre 2. tenanta)

1. **Nema `HasQueryFilter` ni na jednom DbContext-u** za tenant scoping → svaki `GetByIdAsync` bez explicit tenantId curi.
2. **Notification endpoints** (`NotificationsController`, `NotificationRepository`) bez `tenantId` — userId nije tenant-unique.
3. **RefreshToken.GetByTokenAsync** bez tenant filtera; nema čak ni FK constraint na `users` tabelu.
4. **Reset password endpoints** (`UsersController:101, 112`) bez tenant validacije.
5. **CORS overly permissive**: `SetIsOriginAllowed(_ => true).AllowCredentials()` — CSRF napad otvoren.
6. **WebPush VAPID private key commit-ovan u `appsettings.Development.json`** — mora rotacija + preseljenje u secrets.
7. **`TenantsController` nije zaštićen super-admin role check-om** — bilo ko sa JWT-om listuje sve tenante.
8. **`ProductionHub.JoinTenantGroup`** ne validira da poslati tenantId odgovara JWT claim-u.
9. **Order attachment download** (`OrdersController.cs:282–299`) ne validira `attachment.TenantId` pre serviranja fajla.

### 🟡 HIGH (popraviti pre prodavanja proizvoda)

10. **Single-ID `GetByIdAsync` patterns** u svim repozitorijima (Process, ProductCategory, SpecialRequestType, User, Shift, OrderItemProcess, BlockRequest, ChangeRequest, OrderAttachment).
11. **Workflow controller endpoints** bez explicit tenantId (block/unblock/complete/restart/start/stop/resume).
12. **Approve/Reject endpoints** za block i change requests bez tenantId.
13. **Cross-module FK-ovi** bez constraint-a koji forsira tenant match (OrderItemProcess→Process, ProductCategoryDependency→Process, OrderItemSubProcess→OrderItemProcess).
14. **Migracije se izvršavaju na startup-u** — fragilan pattern za multi-instance scale.
15. **Audit kolone postoje ali nisu populated** — nema `SaveChangesInterceptor`.
16. **Nema health check endpoint** — load balancer / monitoring ne razlikuju zombie state.
17. **`TenantService.GetCurrentTenantId()` vraća `Guid.Empty` na fail** — silent fallback, treba throw.
18. **Demo seed data nije idempotent** (work sessions, block/change requests, notifications).

### 🟢 MEDIUM (best practice za skaliranje)

19. **Nema rate limiting**.
20. **Nema audit log tabele** (separate history table).
21. **Nema `tenant_features` tabele** za per-tenant feature flags.
22. **Nema per-tenant resource quotas**.
23. **Nema soft delete** patterna.
24. **Nema idempotency keys** na mutacijama.
25. **Nema correlation IDs** u logovima.
26. **Nema strukturalnog logging-a** (Serilog JSON).
27. **Nema OpenTelemetry**.
28. **Nema API versioning-a**.
29. **Nema HSTS header-a**.
30. **Nema Sentry / error tracking-a**.
31. **Nema `EnableRetryOnFailure`** na DbContext-u.
32. **Nema virus scan-a** na upload-u.
33. **SignalR backplane je in-memory** — blokira horizontalno skaliranje (treba Redis).
34. **systemd unit nije pod version control-om**.
35. **DataSeeder hardcoduje pilot-specific dataset** (drvna industrija); treba per-tenant template sistem.

---

## Šta ovaj audit NIJE pokrio

- **Performance audit**: nije rađen profiling, nisu mereni N+1 query problemi, indeksi nisu eksplicitno proveravani.
- **Penetration test**: ovo je code-level audit; pravi pentesting (SQL injection probe, JWT signature attacks, race conditions) zahteva drugu vrstu posla.
- **Frontend tenant scoping**: frontend dashboard i tablet kod nisu pregledani da li negde keš-iraju tenant-specifične podatke u Redux/Zustand store-u na način koji curi između sesija.
- **GDPR/legal compliance**: nije proveravano postoji li pravilno data retention, deletion, export po tenantu.
- **Backup/restore drill**: nisu testirani postojeći (server-side, ručni) backup procesi.

---

## Predlog redosleda popravki (informativno, ne plan)

Ako se krene u multi-tenant hardening, ovo je optimalan redosled (kasniji koraci zavise od ranijih):

1. **Najjeftinije + najveći leverage**: dodati `HasQueryFilter` na sve DbContext-e + `ICurrentUserService` abstrakciju. Ovaj jedan korak rešava ~70% 🔴 nalaza pod tačkama 1, 2, 3, 4.
2. **CORS popravka** + **WebPush key rotation** — 30 minuta posla, eliminiše dva ozbiljna security finding-a.
3. **Authorize super-admin role na `TenantsController`** — par linija koda.
4. **`SaveChangesInterceptor` za audit kolone** — povezuje `ICurrentUserService` sa entitetima.
5. **Health check endpoint** — `app.MapHealthChecks("/health")` + Postgres connection check.
6. **SignalR group join validation** + tenant attachment download check.
7. **Refactor seedera** u BaselineSeeder + TenantSeeder(template) + DemoDataSeeder.
8. **`tenant_features` tabela** + `ITenantFeatureService` (priprema za prvog plaćajućeg drugog klijenta).
9. **Rate limiting** + **Sentry** + **structured logging** (operativno tooling pre 2. customera).
10. **Backup automatizacija** + **migration discipline** (zasebna step od app startup-a).

Sve posle toga (OpenTelemetry, soft delete, idempotency keys, schema-per-tenant) je već u domenu zrele SaaS platforme i treba da se planira posle 5+ klijenata.

---

**Kraj prvog dela audita.** Sledi dopuna sekcijama 9–18.

---

# DOPUNA AUDITA — sekcije 9–18

**Dopunjeno**: 2026-04-28 (isti dan kao prvi deo).
**Cilj**: pokriti komponente koje prvi audit nije obuhvatio — frontend tenant scoping, test coverage, performance scan, frontend pilot-specific code, security config dubinski, logging, frontend runtime, dependency hygiene, data model assumptions, recovery scenariji.
**Klasifikacija**: ista kao u prvom delu (✅ SAFE / ⚠️ NEEDS REVIEW / 🔴 LEAK RISK / ❌ MISSING).

---

## 9. Frontend tenant scoping audit

### 9a. Gde se čuva tenant_id posle login-a

#### localStorage — token storage
**`packages/api-client/src/token-manager.ts:1–30`**:
```typescript
const TOKEN_KEY = 'algreen_token';
const REFRESH_TOKEN_KEY = 'algreen_refresh_token';

export const tokenManager = {
  getToken(): string | null { return localStorage.getItem(TOKEN_KEY); },
  setTokens(token: string, refreshToken: string): void {
    localStorage.setItem(TOKEN_KEY, token);
    localStorage.setItem(REFRESH_TOKEN_KEY, refreshToken);
  },
  clear(): void {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(REFRESH_TOKEN_KEY);
  },
};
```
**Status: ✅** — JWT u localStorage je standardna praksa, nije šifrovan ali se oslanja na HTTPS i same-origin policy. Ranjiv samo na XSS.

#### Zustand persist — auth store
**`packages/auth/src/auth-store.ts:62–68`**:
```typescript
{
  name: 'algreen-auth',
  partialize: (state) => ({
    user: state.user,
    tenantId: state.tenantId,
    isAuthenticated: state.isAuthenticated,
  }),
}
```
**Status: ✅** — `tenantId` se čuva u localStorage pod ključem `algreen-auth` kao plain text string. Validacija se oslanja na backend (JWT je single source of truth).

#### i18n — language preference
**`packages/i18n/src/config.ts:35–39`**:
```typescript
detection: {
  order: ['localStorage'],
  lookupLocalStorage: 'i18nextLng',
  caches: ['localStorage'],
}
```
**Status: ✅** — samo language code (`'sr'`/`'en'`), nije sensitive.

#### UI preferences (page size)
**`apps/dashboard/src/pages/orders/OrderListPage.tsx:520–522`**:
```typescript
const saved = localStorage.getItem('orders-pageSize');
return saved ? Number(saved) : 20;
```
**Status: ✅** — UI preferenca, nije tenant-scoped.

#### Pretraga ostalih storage layer-a
- `sessionStorage`: 0 instanci
- `document.cookie`: 0 instanci
- IndexedDB: 0 instanci

**Status: ✅** — koristi se samo localStorage (Zustand persist).

### 9b. Cross-session leak između login sesija

#### Logout funkcija
**`packages/auth/src/auth-store.ts:49–57`**:
```typescript
logout: () => {
  tokenManager.clear();
  set({
    user: null,
    tenantId: null,
    isAuthenticated: false,
    error: null,
  });
},
```
**Status: ✅** — briše tokene + reset auth state-a.

#### Force logout na 401
**`packages/api-client/src/axios-instance.ts:15–21`**:
```typescript
function forceLogout() {
  tokenManager.clear();
  if (_onForceLogout) { _onForceLogout(); }
  window.location.href = '/login';
}
```
**Status: ✅** — interceptor čisti tokene i redirektuje.

#### React Query cache — NIJE eksplicitno čišćen na logout
**`apps/dashboard/src/App.tsx:10–18`** + **`apps/tablet/src/App.tsx:5–13`** — `QueryClient` je inicijalizovan ali nema `queryClient.clear()` ili `resetQueries()` u logout flow-u.

**Status: ⚠️** — query cache iz user A sesije ostaje u memoriji. Ako user B login-uje u istom tab-u, mogli bi (teoretski) biti vraćeni keširani podaci dok ne istekne `staleTime: 30_000`. Backend bi odbio sve mutacije jer JWT ne odgovara, ali read-only data može da curi vizualno.

**Predlog popravke**:
```typescript
// u logout action-u
queryClient.clear();
```

#### Offline pending actions store (tablet) — NIJE čišćen na logout
**`apps/tablet/src/offline/offline-store.ts:46`**:
```typescript
clearPendingActions: () => set({ pendingActions: [] }),
```
**Status: 🔴 LEAK RISK** — `clearPendingActions()` se NE poziva u logout flow-u. Ako user A ima queued offline mutations (npr. tablet bio offline, čekale akcije), te akcije ostaju u localStorage pod ključem `algreen-offline`. Kada user B login-uje, neće se replay-ovati (drugi tenantId u JWT-u), ali stale data ostaje.

**Predlog popravke**: u CheckOutPage.tsx ili auth-store.ts logout, dodati `useOfflineStore.getState().clearPendingActions();`.

#### Work session store (tablet)
**`apps/tablet/src/stores/work-session-store.ts:15–19`** — clear poziva se u CheckOutPage.tsx:44. ✅

#### Layout store (dashboard)
**`apps/dashboard/src/stores/layout-store.ts`** — samo `fullscreen` boolean. ✅

#### Switch tenant UI
Nema. Svaki login je fresh authentication sa `tenantCode`. ✅

### 9c. Kako api-client prosleđuje tenant_id

#### JWT sadrži tenant_id claim
**`packages/auth/src/jwt-utils.ts:6`**:
```typescript
export interface JwtPayload {
  tenant_id: string;
  // ...
}
```
**Status: ✅**.

#### Axios interceptor automatski dodaje JWT
**`packages/api-client/src/axios-instance.ts:24–30`**:
```typescript
apiClient.interceptors.request.use((config) => {
  const token = tokenManager.getToken();
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});
```
**Status: ✅**.

#### API endpoints eksplicitno prosleđuju tenantId

| Fajl | Linija | Endpoint | Kako |
|------|-------:|----------|------|
| `packages/api-client/src/api/orders.ts` | 28–34 | `getAll(params)` | query param |
| `packages/api-client/src/api/orders.ts` | 40–71 | `create(data)` | FormData `TenantId` |
| `packages/api-client/src/api/orders.ts` | 137–150 | `uploadAttachment` | query param |
| `packages/api-client/src/api/users.ts` | 6 | `getAll` | query param |
| `packages/api-client/src/api/dashboard.ts` | 11–29 | sve dashboard rute | query param |
| `packages/api-client/src/api/tablet.ts` | 10–26 | tablet rute | query param + userId |
| `packages/api-client/src/api/reports.ts` | 24–36 | report rute | query param |
| `packages/api-client/src/api/process-workflow.ts` | 45–51 | `pauseStation` | request body `tenantId` |
| `packages/api-client/src/api/notifications.ts` | 13–40 | sve notification rute | NEMA — samo userId |

**Status notifikacija: ⚠️** — frontend ne šalje tenantId, oslanja se da backend filter-uje preko JWT claim-a. Trenutno backend NE filtrira (vidi sekciju 1a 🔴 nalaze) — kombinacija **frontend ne šalje + backend ne filtrira** = **dvostruki gap**.

### 9d. Keširani tenant podaci u store-ovima

| Store | Lokacija | Sadržaj | Invalidacija na logout | Status |
|-------|----------|---------|-----------------------|--------|
| Auth Store | `packages/auth/src/auth-store.ts` | user, tenantId, isAuthenticated | ✅ | ✅ |
| Work Session Store | `apps/tablet/src/stores/work-session-store.ts` | checkInTime | ✅ (CheckOutPage:44) | ✅ |
| Offline Store | `apps/tablet/src/offline/offline-store.ts` | pendingActions | ❌ | 🔴 |
| Layout Store | `apps/dashboard/src/stores/layout-store.ts` | fullscreen | N/A | ✅ |
| React Query Cache | `App.tsx` | razne query data | ❌ | ⚠️ |

#### React Query keys — neki nemaju tenantId
- `['orders']` — bez tenantId ⚠️
- `['notifications', userId]` — userId only ⚠️
- `['block-requests-pending-count', tenantId]` — sa tenantId ✅
- Razno — mešovito

**Status: ⚠️** — query keys nisu konzistentno tenant-scoped. Ako se React Query cache ne briše na logout (a ne briše se), stale data iz `['orders']` ostaje pristupačan dok ne istekne `staleTime`.

### 9e. signalr-client tenant validacija

#### Connection setup
**`packages/signalr-client/src/connection-manager.ts:35–62`**:
```typescript
export function createConnection(token: string, url?: string): signalR.HubConnection {
  const hubUrl = url || SIGNALR_URL || 'http://localhost:5030/hubs/production';
  
  connection = new signalR.HubConnectionBuilder()
    .withUrl(hubUrl, { accessTokenFactory: () => token })
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds: (retryContext) => {
        const delay = Math.min(1000 * Math.pow(2, retryContext.previousRetryCount), 30000);
        return delay;
      },
    })
    .configureLogging(signalR.LogLevel.Warning)
    .build();
  return connection;
}
```
**Status: ✅** — JWT prosleđen kroz `accessTokenFactory`, backend ekstraktuje tenant_id.

#### Join/Leave tenant group — bez client-side validacije
**`packages/signalr-client/src/connection-manager.ts:109–119`**:
```typescript
export async function joinTenantGroup(tenantId: string): Promise<void> {
  if (connection?.state === signalR.HubConnectionState.Connected) {
    await connection.invoke('JoinTenantGroup', tenantId);
  }
}
```
**Status: ⚠️** — klijent prosleđuje tenantId koji dobije iz auth store-a. Auth store dobija iz JWT-a. **Klijent nema explicit check** da poslati tenantId odgovara JWT claim-u. Backend MORA da validira (vidi sekciju 1g 🔴 nalaz — backend trenutno NE validira).

### 9f. Service worker / PWA cache (tablet)

#### Registracija
**`apps/tablet/src/main.tsx:12–14`**:
```typescript
if (import.meta.env.PROD && 'serviceWorker' in navigator) {
  navigator.serviceWorker.register('/sw.js', { scope: '/' });
}
```
**Status: ✅** — samo u prod.

#### Precaching strategy
**`apps/tablet/src/sw.ts:1–12`**:
```typescript
import { cleanupOutdatedCaches, precacheAndRoute } from 'workbox-precaching';
declare const self: ServiceWorkerGlobalScope;
precacheAndRoute(self.__WB_MANIFEST);
cleanupOutdatedCaches();
self.addEventListener('install', () => { self.skipWaiting(); });
self.addEventListener('activate', (event) => { event.waitUntil(self.clients.claim()); });
```

#### Glob patterns
**`apps/tablet/vite.config.ts:37–39`**:
```typescript
injectManifest: {
  globPatterns: ['**/*.{js,css,html,ico,png,svg}'],
},
```
**Status: ✅** — keš-iraju se SAMO statički asset-i (JS, CSS, HTML, ikone), **NE API odgovori**.

#### Push handler i navigation cache
**`apps/tablet/src/sw.ts:51–88`** — koristi `caches.open('sw-navigate')` za iOS PWA fallback navigation path. Čuva samo string putanje, ne tenant data.

**Status: ✅** — nema curenja tenant data preko service worker keša.

### 9g. Debug / dev panel u frontend-u

#### Console logging
- **`packages/signalr-client/src/use-signalr-event.ts:14, 20, 27`** — log event registracija (samo imena, ne data)
- **`apps/tablet/src/layouts/TabletLayout.tsx:72–86`** — `console.log('[SignalR] Joined tenant:', tenantId)` — loguje tenantId u console (nije secret, već u JWT-u)
- **`packages/api-client/src/axios-instance.ts:91`** — `console.error('SignalR connection failed:', err)`

**Status: ✅** — minimalan logging, nema exposure secret-a.

#### Debug UI / global window expose
- 0 instanci `window.__auth__`, `window.__store__`, `window.__REDUX_DEVTOOLS__`
- Nema admin debug panela
- Nema in-app debug view

**Status: ✅**.

---

## 10. Postojeća test coverage

### 10a. Backend test projekti

**Pretraga**:
```bash
find /mnt/d/Projects/AlgreenMES -type d -name "*Tests" -o -name "*.Tests"
grep -r "xunit\|NUnit\|MSTest\|FluentAssertions\|Moq\|NSubstitute" --include="*.csproj"
find -name "*Tests.csproj" -o -name "*.Tests.csproj"
```

**Rezultat**: **0 test projekata**.

`AlgreenMES.sln` sadrži 19 projekata, svi su production:
- 1 API entry: `AlgreenMES.API`
- 2 BuildingBlocks (Common, EventBus)
- 4 modula × 4 sloja (Domain, Application, Infrastructure, Api) = 16 modul projekata
- Ukupno: 657 .cs fajlova, 0 test fajlova

**Status: ❌ MISSING** — apsolutno bez backend testova.

### 10b. Test types

**Status: ❌ MISSING** — bez test projekata, nema unit / integration / e2e podele.

### 10c. Coverage po modulu

| Modul | Test klase | Coverage |
|-------|-----------|----------|
| Tenancy | 0 | 0% |
| Identity | 0 | 0% |
| Production | 0 | 0% |
| Orders | 0 | 0% |
| BuildingBlocks | 0 | 0% |

**Status: ❌ MISSING**.

### 10d. Tenant isolation testovi

**Status: 🔴 LEAK RISK** — niti jedan test ne potvrđuje da Tenant A ne može da pristupi Tenant B podacima. Za multi-tenant SaaS sistem ovo je kritičan gap. Ako se danas implementira `HasQueryFilter` (predloženo u sekciji 1f), nema načina da se proveri da nije nešto promašeno.

### 10e. Frontend testovi

**Pretraga**:
```bash
find /mnt/d/Projects/AlgreenMES\ front -type f -name "*.test.ts*" -o -name "*.spec.ts*"
find . -name "vitest.config.*" -o -name "jest.config.*" -o -name "playwright.config.*" -o -name "cypress.config.*"
```

**Rezultat**:
- 0 test fajlova u app kodu (1 hit u node_modules, nije relevantno)
- 0 test config fajlova
- `package.json` skripte u `apps/dashboard`, `apps/tablet`, root: samo `lint` i `typecheck`, nema `test`
- Nema devDependencies na vitest, jest, @testing-library/react, playwright, cypress

**Status: ❌ MISSING** — frontend testova nema.

### 10f. CI konfiguracija

**Pretraga**:
```bash
find -name ".github" -type d
find -name ".gitlab-ci.yml" -o -name "azure-pipelines.yml" -o -name "Jenkinsfile" -o -name ".circleci"
```

**Rezultat**: 0 CI/CD fajlova u oba repo-a.

Postoji `render.yaml` u backend repo-u za Render.com deployment, ali to je deploy config, ne CI.

**Status: ❌ MISSING** — bez automated testiranja, lintinga, deploy pipeline-a. Sve manuelno.

---

## 11. Performance / N+1 quick scan

### 11a. Deep .Include() chain-ovi (3+ nivoa)

#### `OrderRepository.cs:32–41` — `GetByIdWithFullDetailsAsync` (4 nivoa)
```csharp
.Include(o => o.Items)
    .ThenInclude(i => i.Processes)
        .ThenInclude(p => p.SubProcesses)
            .ThenInclude(sp => sp.Logs)
.Include(o => o.Items)
    .ThenInclude(i => i.SpecialRequests)
```
**Status: ⚠️** — 4-level chain učitava ceo order tree uključujući sve sub-process logove. Moglo bi se zameniti sa `.Select()` projekcijom za samo skalarne podatke.

#### `OrderRepository.cs:69–81` — `GetActiveOrdersWithProcessesAsync` (4 nivoa)
Isti pattern kao gore — 4-level chain. **Status: ⚠️** — duplicated logic.

#### `OrderRepository.cs:130–137` — `GetPagedWithProcessesAsync` (4 nivoa + Attachments)
```csharp
.Include(o => o.Items)
    .ThenInclude(i => i.Processes)
        .ThenInclude(p => p.SubProcesses)
            .ThenInclude(sp => sp.Logs)
.Include(o => o.Attachments)
```
**Status: ⚠️** — pagination radi ali Cartesian explosion sa 4-level chain-om može da generiše ogromne row count-ove.

#### `BlockRequestRepository.cs:43–48` (3 nivoa)
```csharp
.Include(br => br.OrderItemProcess)
    .ThenInclude(p => p!.OrderItem)
        .ThenInclude(i => i.Order)
```
**Status: ⚠️** — prihvatljiv za search, ali mogla bi se koristiti projekcija.

#### `OrderItemProcessRepository.cs:30–36` i `:40–46` (3 nivoa, duplicirana metoda)
```csharp
.Include(p => p.SubProcesses).ThenInclude(sp => sp.Logs)
.Include(p => p.OrderItem).ThenInclude(oi => oi.Order)
```
Linije 30-36 i 40-46 su **identične** — code smell. **Status: ⚠️**.

#### `ProductCategoryRepository.cs:24–33` (2 nivoa, prihvatljivo)
```csharp
.Include(c => c.Processes).ThenInclude(p => p.Process)
.Include(c => c.Dependencies).ThenInclude(d => d.Process)
.Include(c => c.Dependencies).ThenInclude(d => d.DependsOnProcess)
```
**Status: ✅** — 2-level, prihvatljivo.

### 11b. ToListAsync + in-memory Where/Select (anti-pattern)

#### `DashboardQueryService.cs:45–67` — `GetDeadlineWarningsAsync`
```csharp
var activeOrders = await _ordersDb.Orders
    .AsNoTracking()
    .Include(o => o.Items)
        .ThenInclude(i => i.Processes)
    .Where(o => o.TenantId == tenantId && o.Status == OrderStatus.Active)
    .ToListAsync(cancellationToken);

foreach (var order in activeOrders) {
    // ...
    var inProgressProcess = order.Items
        .SelectMany(i => i.Processes)
        .FirstOrDefault(p => p.Status == ProcessStatus.InProgress);  // <- in-memory
    // ...
}
```
**Status: ⚠️** — date arithmetic i SelectMany u memoriji posle materijalizacije.

#### `DashboardQueryService.cs:99–107` — `GetLiveViewAsync`
```csharp
foreach (var process in processes) {
    var processItems = activeOrderItemProcesses
        .Where(oip => oip.ProcessId == process.Id)  // <- O(N²) scan
        .ToList();
}
```
**Status: ⚠️** — O(N²) scan; treba `GroupBy(oip => oip.ProcessId)` ili dictionary lookup.

### 11c. Async DB pozivi unutar foreach petlji

#### `CreateOrderCommandHandler.cs:62–91` — N+1 risk
```csharp
foreach (var itemInput in request.Items) {
    var category = await _categoryRepository.GetByIdWithDetailsAsync(...);  // DB hit
    var process = await _processRepository.GetByIdWithSubProcessesAsync(...);  // DB hit
    if (itemInput.Attachments is { Count: > 0 }) {
        foreach (var file in itemInput.Attachments)
            await SaveAttachment(file, ...);  // file I/O per attachment
    }
}
```
**Status: ⚠️** — za order sa 10 item-a → 20 DB hit-ova pre `SaveChangesAsync`. Treba batch fetch:
```csharp
var categoryIds = request.Items.Select(i => i.ProductCategoryId).ToList();
var categories = await _categoryRepository.GetByIdsAsync(categoryIds);
```

#### `WebPushService.cs:132–174` — async u petlji (legitimno)
```csharp
foreach (var sub in subscriptions) {
    await _client.RequestPushMessageDeliveryAsync(...);  // external HTTP
}
await _unitOfWork.SaveChangesAsync(...);  // single save posle petlje
```
**Status: ✅** — eksterni HTTP pozivi (Web Push API), ne DB. SaveChanges van petlje. Prihvatljiv pattern.

### 11d. Paginated endpoints bez index-a na sort kolonama

#### Orders pagination
**`OrderRepository.cs:130+`** — sortira po `OrderNumber`, `OrderType`, `Status`, `CreatedAt`, `CompletedAt`, `DeliveryDate`, `Priority`.

**Indeksi u `20260210203013_InitialOrders.cs:392–408`**:
- `(tenant_id, priority)` ✅
- `(tenant_id, delivery_date)` ✅
- `(tenant_id, order_number)` ✅
- `(tenant_id, status)` ✅
- `created_at`: koristi `(tenant_id, ...)` — može fall back na sequential scan posle filtera
- `completed_at`: nema explicit index — ⚠️

**Status: ⚠️** — većina pokrivena, par sort kolona (created_at, completed_at) nemaju explicit index.

#### Block Requests pagination
**`BlockRequestRepository.cs`** — sort po `Status`, `UpdatedAt`, `CreatedAt`. Migration ima `(tenant_id, status)`. ✅

### 11e. DbContext lifetime u DI

| Modul | Fajl | Lifetime |
|-------|------|----------|
| Identity | `IdentityInfrastructureServiceRegistration.cs:17–21` | `services.AddDbContext<IdentityDbContext>(...)` (default Scoped) |
| Orders | `OrdersInfrastructureServiceRegistration.cs:17–21` | Scoped |
| Production | `ProductionInfrastructureServiceRegistration.cs:20–24` | Scoped |
| Tenancy | `TenancyInfrastructureServiceRegistration.cs:15–19` | Scoped |

**Status: ✅** — svi Scoped (default i ispravan). Nijedan Singleton (što bi bio bug).

---

## 12. Frontend pilot-specific code

### 12a. Hardcoded srpski stringovi (industrija-specifični)

**Pretraga**:
```bash
grep -r "Krojenje\|Kantiranje\|Brušenje\|Grundiranje\|Farbanje\|Lakiranje\|Vrata Pivot\|Peskarenje\|Samo farbanje" apps/ packages/
```
**Rezultat**: 0 hard-coded instanci u frontend kodu.

**Status: ✅** — process imena i kategorije dolaze iz API-ja, ne hardcoded.

### 12b. Hardcoded role names / tenant logika

#### Role-based UI gating
**`apps/dashboard/src/routes.tsx:40–147`**:
```typescript
<RequireRole roles={[UserRole.Coordinator, UserRole.Manager, UserRole.Admin]}>
  <CoordinatorDashboard />
</RequireRole>
```
**Status: ✅** — koristi enum iz `@algreen/shared-types`.

**`apps/dashboard/src/components/SidebarMenu.tsx`**:
```typescript
const isCoordinator = role === UserRole.Coordinator || role === UserRole.Manager || role === UserRole.Admin;
const isAdminOrManager = role === UserRole.Admin || role === UserRole.Manager;
const isSales = role === UserRole.SalesManager;
```
**Status: ✅** — enum-based, ne hardcoded string.

**`apps/dashboard/src/components/RoleRedirect.tsx:10–22`** — switch po `user.role` enum-u. ✅

#### Tenant-specifična hardcoded logika
Pretraga `tenantId === '...'`, `tenant.name === '...'`: 0 instanci. ✅

### 12c. Hardcoded URLs, paths, phone numbers, emails

#### API URLs (env-controlled)
**`packages/api-client/src/axios-instance.ts:5`**:
```typescript
baseURL: import.meta.env?.VITE_API_BASE_URL || 'http://localhost:5030/api',
```
**Status: ✅** — env var, default localhost.

**`packages/signalr-client/src/connection-manager.ts:6–9`**:
```typescript
const SIGNALR_URL = typeof import.meta !== 'undefined'
  ? import.meta.env?.VITE_SIGNALR_URL
  : undefined;
```
**Status: ✅**.

#### Hardcoded u deploy skriptama (vidi se u git-u)
**`deploy.sh:4–5`**:
```bash
export VITE_API_BASE_URL=https://tracker-api.algreen.rs/api
export VITE_SIGNALR_URL=https://tracker-api.algreen.rs/hubs/production
```
**Status: ⚠️** — production URLs vidljivi u repo. Prihvatljivo za deploy script ali javno otkriva infrastrukturu.

**`deploy-windows.sh:7–8`**:
```bash
SSH_KEY="C:/Users/user/.ssh/id_ed25519"
SERVER="root@46.101.166.137"
```
**Status: 🔴** — server IP `46.101.166.137` u git-u + Windows SSH key putanja vidljiva (otkriva development setup).

#### Phone numbers / emails u frontend kodu
Pretraga: 0 hardcoded contacts.

**Status: ✅**.

### 12d. i18n setup

**Library**: `react-i18next` + `i18next` + `i18next-browser-languagedetector`

**Init** (`packages/i18n/src/config.ts:18–46`):
```typescript
i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources: {
      sr: { common: srCommon, [appNamespace]: appResources.sr },
      en: { common: enCommon, [appNamespace]: appResources.en },
    },
    defaultNS: appNamespace,
    fallbackNS: 'common',
    fallbackLng: 'sr',
    supportedLngs: ['sr', 'en'],
    detection: { order: ['localStorage'], lookupLocalStorage: 'i18nextLng' },
  });
```

**Locale fajlovi**:
- `packages/i18n/src/locales/sr/common.json` (~198 linija, ~7.5 KB)
- `packages/i18n/src/locales/en/common.json` (~198 linija, ~7.5 KB)
- Per-app: `apps/dashboard/src/i18n/locales/sr/dashboard.json`, `en/dashboard.json`
- Per-app: `apps/tablet/src/i18n/locales/sr/tablet.json`, `en/tablet.json`

**Date formatting — hardcoded srpski locale**
**`apps/tablet/src/pages/checkout/CheckOutPage.tsx:68`**:
```typescript
{new Date(checkInTime).toLocaleTimeString('sr-Latn-RS', { hour: '2-digit', minute: '2-digit', hour12: false })}
```
**Status: ⚠️** — locale je hardcoded `'sr-Latn-RS'`, ne dinamičan po `i18n.language`.

**`apps/dashboard/src/pages/admin/UsersPage.tsx`** — koristi dayjs:
```typescript
render: (d: string) => dayjs(d).format('DD.MM.YYYY.'),
```
**Status: ✅** — explicit format string, locale-neutral.

**Number / currency formatting**: nema (`Intl.NumberFormat`, `toLocaleString` na brojevima).

### 12e. Branding assets

**Logo fajlovi**:
- `apps/dashboard/public/algreen-logo.png` (8.7 KB)
- `apps/dashboard/public/algreen-logo-text.png` (26 KB)
- `apps/dashboard/public/favicon.png` (1.5 KB)
- `apps/tablet/public/algreen-logo.png` (isti)
- `apps/tablet/public/pwa-192x192.png` (20 KB)
- `apps/tablet/public/pwa-512x512.png` (73 KB)
- `apps/tablet/public/apple-touch-icon.png` (18 KB)

**Theme** — Dashboard
**`apps/dashboard/src/styles/theme.ts:3–19`**:
```typescript
export const theme: ThemeConfig = {
  token: {
    colorPrimary: '#2e7d32',  // green
    borderRadius: 6,
    colorBgContainer: '#ffffff',
  },
  components: {
    Layout: {
      siderBg: '#001529',
      headerBg: '#ffffff',
    },
  },
};
```

**Theme** — Tablet
- Tailwind config sa primary color `#2e7d32` (green)
- PWA theme color `#2e7d32`

**Status: ⚠️** — branding je **globalan**, ne per-tenant. Za multi-tenant prodaju, trebaće per-tenant config (logo URL iz tenant settings, theme color iz tenant settings).

---

## 13. Konfiguracija sigurnosti — produbljeno

### 13a. appsettings.Production.json

**Pretraga**: `find /mnt/d/Projects/AlgreenMES -name "appsettings.Production.json"` → 0 rezultata.

**Excluded u deploy.sh**:
```bash
rsync -az --delete --exclude='appsettings.Production.json' ...
```

**Status: ❌ AUDIT GAP** — postoji samo na serveru (`/opt/algreen/api/appsettings.Production.json`). Ne mogu da auditujem iz repo-a. Ovo je intentional ali stvara gap: nema version control-a, nema rollback zaštite, nema verifikacije sadržaja.

### 13b. Svi secrets u kodebazi

#### Hardcoded u `appsettings.json` (commit-ovan):
| Linija | Tip | Vrednost |
|-------:|-----|----------|
| 10 | DB Password | `postgres:postgres` (plaintext) |
| 13 | JWT Secret | `CHANGE_ME_IN_PRODUCTION_USE_A_LONG_SECRET_KEY` (placeholder) |

#### Hardcoded u `appsettings.Development.json` (commit-ovan):
| Linija | Tip | Vrednost |
|-------:|-----|----------|
| 10 | DB Password (Dev) | empty string |
| 13 | VAPID Public Key | `BBT7Tg_EjRRdkStpspknQw3ckEkIaqUoAi3mLyijZNpgMk_tcHAEzliINq...` |
| 14 | VAPID **Private** Key | `Qu5p3q01SxtvUvbdkJlwses5eOOhbnLsdxo0MOAF3Mo` |

**Status: 🔴** — VAPID private key u git-u. Mora rotacija + preseljenje u env var.

#### Demo passwords u DataSeeder.cs:
| Linija | Account | Password |
|-------:|---------|----------|
| 43 | admin@demo.com | `Admin123!` (bcrypt-hashed at write) |
| 156 | 7 demo users | `Demo123!` (bcrypt-hashed at write) |

**Status: ⚠️** — passwords visible u git history-ju. Production deploy ne sme da pokreće seeder sa demo password-ima.

#### .env / secrets.json:
Pretraga: 0 rezultata. Sve config ide kroz appsettings*.json.

### 13c. Secret rotation procedura

Pretraga README, SECURITY.md, OPERATIONS.md, ROTATION.md: nijedan ne postoji u repo-u.

Pretraga "rotat", "key versioning": 0 rezultata u kodu.

**Status: ❌ MISSING** — nema dokumentovane procedure za rotaciju JWT secret-a, DB credential-a, ili VAPID ključeva.

### 13d. Server-side environment variables

#### Što vidimo u repo-u

**`render.yaml:8–25`**:
```yaml
envVars:
  - key: ConnectionStrings__DefaultConnection
    sync: false  # postavljeno ručno u Render dashboard
  - key: JwtSettings__Secret
    generateValue: true
  - key: JwtSettings__Issuer
    value: AlGreenMES
  - key: JwtSettings__Audience
    value: AlGreenMES
  - key: JwtSettings__ExpirationMinutes
    value: "60"
  - key: WebPush__VapidPublicKey
    sync: false
  - key: WebPush__VapidPrivateKey
    sync: false
  - key: WebPush__VapidSubject
    value: "mailto:support@algreen.rs"
  - key: WebPush__Enabled
    value: "true"
```

**`Dockerfile:41–42`**:
```dockerfile
ENV ASPNETCORE_URLS=http://+:${PORT:-10000}
ENV ASPNETCORE_ENVIRONMENT=Production
```

#### Što ne vidimo
- systemd unit file (`algreen-api.service`) sa `Environment=` ili `EnvironmentFile=` — nije u repo
- DigitalOcean droplet env var sources — nije u repo

**Status: ⚠️** — Render env vars su definisani u render.yaml; DigitalOcean (production server preko deploy.sh) ima env vars samo na serveru.

### 13e. TLS termination

**Pretraga**:
```bash
grep -rn "Kestrel.*Https\|Certificate\|UseHttps" /mnt/d/Projects/AlgreenMES
```
0 rezultata u Application config-u.

**Dockerfile:41–44**:
```dockerfile
ENV ASPNETCORE_URLS=http://+:${PORT:-10000}
EXPOSE 10000
```
App expose-uje samo HTTP na portu 10000.

**Zaključak**: TLS terminira ili Render (PaaS, automatic) ili nginx/Caddy na DigitalOcean droplet-u (nije u repo). **Status: ✅ ako proxy radi pravilno; 🔴 ako se app deploy-uje direktno bez proxy-ja.**

### 13f. Security middleware

**`Program.cs:116–128`** — middleware pipeline:
```csharp
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();    // 116
if (app.Environment.IsDevelopment()) {
    app.MapOpenApi();                                     // 120
}
app.UseHttpsRedirection();                                // 123
app.UseCors();                                            // 124
app.UseAuthentication();                                  // 125
app.UseAuthorization();                                   // 126
app.MapControllers();                                     // 127
app.MapHub<ProductionHub>("/hubs/production");            // 128
```

| Middleware | Prisutan? | Status |
|------------|-----------|--------|
| `UseHsts()` | ❌ | Nedostaje |
| `UseHttpsRedirection()` | ✅ | Linija 123 |
| `UseCors()` | ✅ | Linija 124 ali **overly permissive** |
| `UseAuthentication()` | ✅ | Linija 125, redosled OK |
| `UseAuthorization()` | ✅ | Linija 126, redosled OK |
| Custom security headers (XFO, XCTO, CSP) | ❌ | Nema middleware-a |
| `UseAntiforgery()` | ❌ | Nema (REST + JWT, niži CSRF risk) |

#### CORS detalj
**`Program.cs:102–111`**:
```csharp
builder.Services.AddCors(options => {
    options.AddDefaultPolicy(policy => {
        policy.SetIsOriginAllowed(_ => true)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});
```
**Status: 🔴** — `SetIsOriginAllowed(_ => true) + AllowCredentials()` je MDN-okarakterisana CORS misconfiguration. Bilo koji site može da šalje credentialed requeste.

**Predlog**:
```csharp
policy.WithOrigins("https://dashboard.algreen.rs", "https://tablet.algreen.rs")
    .AllowAnyMethod().AllowAnyHeader().AllowCredentials();
```

### 13g. JWT secret rotation

**`Program.cs:40–59`**:
```csharp
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secret = jwtSettings["Secret"]!;
// ...
IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret))
```

**Status: ❌ MISSING** — single secret bez key versioning-a / grace period-a. Rotacija = restart aplikacije + invalidacija svih aktivnih session-a (bez graceful migration-a).

### 13h. Password policy

**`CreateUserCommandValidator.cs:13`**:
```csharp
RuleFor(x => x.Password).NotEmpty().MinimumLength(6).MaximumLength(100);
```

**`ChangePasswordCommandValidator.cs:12`**:
```csharp
RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(6).MaximumLength(100);
```

| Pravilo | Status |
|---------|--------|
| Minimum length | 6 — 🔴 (industrijski standard ≥12) |
| Complexity (lowercase/uppercase/digit/special) | ❌ |
| Password history (no reuse) | ❌ |
| Entropy check | ❌ |

**Hashing**:
**`PasswordHasher.cs:7–15`**:
```csharp
public string HashPassword(string password) => BCrypt.Net.BCrypt.HashPassword(password);
public bool VerifyPassword(string password, string hash) => BCrypt.Net.BCrypt.Verify(password, hash);
```
**Status: ✅** — BCrypt sa default cost (12), kriptografski zdrav.

**Ukupno: 🔴** — slaba politika lozinki. Prihvatljiva za demo, ne za production.

---

## 14. Logging, error handling, observability

### 14a. Default logging provider

**`appsettings.json:2–6`**:
```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft.AspNetCore": "Warning"
  }
}
```

**`appsettings.Development.json:2–7`**:
```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft.AspNetCore": "Warning",
    "Microsoft.EntityFrameworkCore.Database.Command": "Information"
  }
}
```

**Provider**: ASP.NET Core built-in ConsoleLoggerProvider. Nema Serilog, NLog, fajl sink-ova.

**Output**: Console / stdout. U produkciji journalctl ili Docker logs.

**Status: ⚠️** — bez persistentnog logging-a, bez strukturalnih polja, bez log aggregation-a.

### 14b. Global exception handler

**`AlgreenMES.API/Middleware/GlobalExceptionHandlerMiddleware.cs:23–73`**:
```csharp
public async Task InvokeAsync(HttpContext context) {
    try { await _next(context); }
    catch (NotFoundException ex) {
        _logger.LogWarning(ex, "Not found: {Code} - {Message}", ex.Code, ex.Message);
        // 404 JSON response
    }
    catch (ValidationException ex) {
        _logger.LogWarning(ex, "Validation failed: {Message}", ex.Message);
        // 422 JSON
    }
    catch (ForbiddenException ex) { /* 403 */ }
    catch (DomainException ex) { /* 400 */ }
    catch (Exception ex) {
        _logger.LogError(ex, "Unhandled exception");
        // 500 generic
    }
}
```

**Status: ✅** — middleware hvata sve, mapira na HTTP status code, loguje, ne curi stack trace.

### 14c. Stack trace u prod

Pretraga `UseDeveloperExceptionPage`: 0 rezultata.

**Default 500 response**:
```csharp
{ "error": { "code": "INTERNAL_ERROR", "message": "An unexpected error occurred." } }
```

**Status: ✅** — generic message, stack trace ne ide klijentu.

### 14d. Format error response-a

| HTTP | Format |
|------|--------|
| 404 | `{ "error": { "code": "...", "message": "..." } }` |
| 422 | `{ "error": { "code": "VALIDATION_ERROR", "message": "...", "errors": [{ "property": "...", "message": "..." }] } }` |
| 403 | `{ "error": { "code": "...", "message": "..." } }` |
| 400 | `{ "error": { "code": "...", "message": "..." } }` |
| 500 | `{ "error": { "code": "INTERNAL_ERROR", "message": "An unexpected error occurred." } }` |

**Status: ⚠️** — konzistentan custom format, **ne RFC 7807 ProblemDetails** standard.

### 14e. Auth event logs

| Event | Login | Refresh | Password Change | Password Reset | Logout |
|-------|:-----:|:-------:|:---------------:|:--------------:|:------:|
| Success log | ❌ | ❌ | ❌ | ❌ | N/A (client-side) |
| Failure log | ✅ (via exception) | ✅ (via exception) | ✅ (via exception) | ✅ (via exception) | N/A |

**`LoginCommandHandler.cs:37–69`** — failure cases (linije 39, 46, 48, 52) imaju `_logger.LogWarning`. Linija 54–68 successful login **NEMA explicit log**.

**Status: ⚠️** — failures se loguju (preko exception middleware-a), success se NE loguje. Za GDPR/compliance audit log mora da beleži uspešne autentikacije.

### 14f. Mutation logs

| Handler | Logging |
|---------|---------|
| `CreateOrderCommandHandler` | ❌ |
| `CancelOrderCommandHandler` | ❌ |
| `CompleteProcessCommandHandler` | ✅ (`_logger` field, partial) |
| `StartProcessWorkCommandHandler` | ✅ (`_logger.LogInformation` na 3 mesta sa userId/processId) |
| `DeleteUserCommandHandler` | ❌ |
| `CreateUserCommandHandler` | ❌ |
| `ChangePasswordCommandHandler` | ❌ |
| `ResetPasswordCommandHandler` | ❌ |

**Status: ⚠️** — mešovito. Process-level mutacije imaju logging, high-value (create/delete order/user, password change) **nemaju**.

### 14g. Lokacija log fajlova

- **Razvoj**: console (Visual Studio Debug)
- **Render**: container stdout → Render logs
- **DigitalOcean**: stdout → systemd journal (`journalctl -u algreen-api`)

**Status: ⚠️** — bez persistentnog file-based logging-a. Nema log rotation-a, archival-a, retention-a.

### 14h. Alerting

Pretraga: Slack, Discord, Sentry, OpsGenie, webhook, smtp, email.

Match-evi: samo email adrese u seederu (`admin@demo.com`) i VAPID subject (`mailto:support@algreen.rs`).

**Status: ❌ MISSING** — nema Sentry, Slack webhook, SMTP alerting, ničega. Greške u prod-u niko ne vidi dok se klijent ne javi.

---

## 15. Frontend deployment i runtime

### 15a. Domain / subdomain / path razlikovanje

**`deploy.sh:13`** — Dashboard:
```bash
rsync -az --delete apps/dashboard/dist/ root@46.101.166.137:/opt/algreen/dashboard/
```

**`deploy.sh:21`** — Tablet:
```bash
rsync -az --delete apps/tablet/dist/ root@46.101.166.137:/opt/algreen/tablet/
```

**Putanje na serveru**:
- `/opt/algreen/dashboard/` — dashboard SPA
- `/opt/algreen/tablet/` — tablet PWA
- `/opt/algreen/api/` — backend

Oba app-a koriste isti API (`https://tracker-api.algreen.rs/api`).

**Pretpostavka**: nginx ispred radi reverse proxy po domenima (npr. `dashboard.algreen.rs` → `/opt/algreen/dashboard/`, `tablet.algreen.rs` → `/opt/algreen/tablet/`). Konkretan setup nije u repo-u.

**Status: ⚠️** — deployment putanje vidljive ali nginx config nije pod version control-om.

### 15b. Nginx / Caddy config

**Pretraga**:
```bash
find /mnt/d/Projects/AlgreenMES\ front -name "*.nginx" -o -name "nginx.conf" -o -name "Caddyfile"
find /mnt/d/Projects/AlgreenMES -name "*.nginx" -o -name "nginx.conf" -o -name "Caddyfile"
```
Rezultat: 0.

**Status: ❌ MISSING u repo-u** — web server config se upravlja zasebno (verovatno na serveru ručno).

### 15c. Service worker registracija (tablet)

**`apps/tablet/src/main.tsx:12–14`**:
```typescript
if (import.meta.env.PROD && 'serviceWorker' in navigator) {
  navigator.serviceWorker.register('/sw.js', { scope: '/' });
}
```

**Strategija** — vidi 9f. `precacheAndRoute(self.__WB_MANIFEST)` za statičke asset-e. **NE keš-ira API odgovore.**

### 15d. PWA manifest

**`apps/tablet/vite.config.ts:15–36`**:
```typescript
manifest: {
  name: 'Algreen MES - Tablet',
  short_name: 'Algreen',
  description: 'Factory floor tablet app for Algreen MES',
  theme_color: '#2e7d32',
  background_color: '#f5f5f5',
  display: 'standalone',
  orientation: 'portrait',
  icons: [
    { src: 'pwa-192x192.png', sizes: '192x192', type: 'image/png' },
    { src: 'pwa-512x512.png', sizes: '512x512', type: 'image/png', purpose: 'any maskable' },
  ],
},
```

| Polje | Vrednost |
|-------|----------|
| name | Algreen MES - Tablet |
| short_name | Algreen |
| theme_color | #2e7d32 (zeleno) |
| display | standalone (bez browser UI) |
| orientation | portrait (zaključano) |
| icons | 192×192 + 512×512 (maskable) |

**Status: ✅** — standardan PWA manifest, nema sensitive permissions.

### 15e. Bundle size / kompleksnost

**Dashboard rute** (`apps/dashboard/src/routes.tsx`): 12 ruta
- `/login`, `/dashboard`, `/orders`, `/sales`, `/block-requests`, `/change-requests`, `/reports`
- `/admin/users`, `/admin/processes`, `/admin/product-categories`, `/admin/special-request-types`, `/admin/tenants`, `/admin/shifts`

**Tablet rute** (`apps/tablet/src/routes.tsx`): 4 rute
- `/login`, `/queue`, `/incoming`, `/notifications`, `/checkout`

**Procena bundle size-a** (tipično za React + Antd + Vite):
- Dashboard: ~500–800 KB minified, pre gzip
- Tablet: ~300–500 KB minified

**Status: ✅** — razumno za production MES app.

### 15f. Source maps u prod

**`apps/dashboard/vite.config.ts:1–9`**:
```typescript
export default defineConfig({
  plugins: [react()],
  server: { port: 5931 },
});
```

**`apps/tablet/vite.config.ts:1–48`** — nema `build.sourcemap` setting-a.

**Default Vite ponašanje**: source maps NISU uključeni u production build osim ako se eksplicitno ne uključe.

**Status: ✅** — source maps se NE deploy-uju.

---

## 16. Dependency hygiene

### 16a. Backend NuGet paketi

**Target Framework**: `net9.0` — konzistentno preko svih 19 projekata. ✅

**Verzije ključnih paketa**:

| Paket | Verzija | Status |
|-------|---------|--------|
| Microsoft.EntityFrameworkCore | 9.0.1 | ✅ |
| Microsoft.EntityFrameworkCore.Design | 9.0.1 | ✅ |
| Npgsql.EntityFrameworkCore.PostgreSQL | 9.0.4 | ✅ |
| EFCore.NamingConventions | 9.0.0 | ✅ |
| Microsoft.AspNetCore.Authentication.JwtBearer | 9.0.0 | ✅ |
| Microsoft.AspNetCore.OpenApi | 9.0.6 | ✅ |
| MediatR | 12.4.1 | ✅ |
| FluentValidation | 11.11.0 | ✅ |
| FluentValidation.DependencyInjectionExtensions | 11.11.0 | ✅ |
| Mapster | 7.4.0 | ✅ |
| Mapster.DependencyInjection | 1.0.1 | ✅ |
| BCrypt.Net-Next | 4.0.3 | ✅ |
| System.IdentityModel.Tokens.Jwt | 8.3.0 | ✅ |
| Lib.Net.Http.WebPush | 3.3.1 | ✅ |

**Pre-release versions**: 0.

**Status: ✅** — sve verzije moderne, nema pre-release, EF Core 9.x dosledan.

### 16b. Frontend npm paketi

**Resolved verzije iz `pnpm-lock.yaml`**:

| Paket | Verzija | Status |
|-------|---------|--------|
| react | 18.3.1 | ✅ |
| react-dom | 18.3.1 | ✅ |
| react-router-dom | 6.30.3 | ✅ |
| antd | 5.29.3 | ✅ |
| @tanstack/react-query | 5.90.21 | ✅ |
| zustand | 4.5.7 | ✅ |
| dayjs | 1.11.19 | ✅ |
| @microsoft/signalr | 8.0.17 | ✅ |
| axios | 1.13.5 | ✅ |
| typescript | 5.9.3 | ✅ |
| vite | 5.4.21 | ✅ |
| eslint | 8.57.1 | ✅ |
| i18next | 23.16+ | ✅ |
| react-i18next | 15.1+ | ✅ |
| @dnd-kit/core | 6.3.1 | ✅ |
| pdfjs-dist | 5.5.207 | ✅ |
| nosleep.js | 0.12.0 | ✅ |
| workbox-precaching | 7.4.0 | ✅ |
| vite-plugin-pwa | 0.20.0 | ✅ |
| tailwindcss | 3.4.0 | ✅ |
| browser-image-compression | 2.0.2 | ✅ |

**Status: ✅** — sve verzije produkciono spremne i sveže.

### 16c. .NET version + LTS status

**Target**: `net9.0` na svim projektima.

**Status .NET 9**:
- Released: 2024-11-12
- **Standard Term Support (STS)**: 18 meseci
- **Support ends: 2026-05-12**
- Audit datum: 2026-04-28 → **14 dana do EOL**

**.NET 10 (LTS)** će biti released 2025-11-12 (već je dostupan u 2026).

**Status: ⚠️ KRITIČNO** — projekat koristi STS koji ističe za 2 nedelje. Plan migracije na .NET 10 LTS hitan.

### 16d. Node.js version

**`package.json:18–21`**:
```json
"engines": {
  "node": ">=18",
  "pnpm": ">=9"
}
```

**Status Node verzija**:
- Node 18 LTS: EOL 2024-04-30 (već prošao)
- Node 20 LTS: aktivan, EOL 2026-04-30 (za 2 dana od audit datuma)
- Node 22 LTS: aktivan u LTS od 2024-10-29

**`.nvmrc` ili `.tool-versions`**: 0 fajlova nađeno.

**Status: ⚠️** — Node 18 EOL, Node 20 LTS ističe za 2 dana. Hitno upgrade na Node 22 LTS. Nedostaje `.nvmrc` za pinning.

### 16e. Dependabot / Renovate

**Pretraga**:
- `/mnt/d/Projects/AlgreenMES/.github/dependabot.yml` → ne postoji
- `/mnt/d/Projects/AlgreenMES/renovate.json` → ne postoji
- `/mnt/d/Projects/AlgreenMES front/algreen-tracker/.github/dependabot.yml` → ne postoji
- `/mnt/d/Projects/AlgreenMES front/algreen-tracker/renovate.json` → ne postoji

**Status: ❌ MISSING** — bez automated dependency updates. Security patche treba ručno pratiti.

---

## 17. Data model — tihe pretpostavke

### 17a. Unique constraints — tenant-scoped vs global

#### `20260210203013_InitialOrders.cs:398–402` — orders
```csharp
migrationBuilder.CreateIndex(
    name: "ix_orders_tenant_id_order_number",
    columns: new[] { "tenant_id", "order_number" },
    unique: true);
```
**Status: ✅** — composite `(tenant_id, order_number)`.

#### `20260210173613_InitialIdentity.cs:88–92` — users.email
```csharp
migrationBuilder.CreateIndex(
    name: "ix_users_tenant_id_email",
    columns: new[] { "tenant_id", "email" },
    unique: true);
```
**Status: ✅** — `(tenant_id, email)`. Isti email moguć u različitim tenantima.

#### `20260210202952_InitialProduction.cs:182–187` — processes.code
**Status: ✅** — `(tenant_id, code)`.

#### `20260210202952_InitialProduction.cs:189–194` — product_categories.name
**Status: ✅** — `(tenant_id, name)`.

#### `20260210202952_InitialProduction.cs:229–233` — special_request_types.code
**Status: ✅** — `(tenant_id, code)`.

#### `20260210202952_InitialProduction.cs:208–213` — product_category_dependencies
```csharp
columns: new[] { "product_category_id", "process_id", "depends_on_process_id" },
unique: true);
```
**Status: ✅** — ne sadrži tenant_id, ali product_category_id je inherentno tenant-specifičan (FK na tenant-scoped tabelu). Praktično tenant-scoped.

#### `20260210173613_InitialIdentity.cs:80–85` — refresh_tokens.token
```csharp
migrationBuilder.CreateIndex(
    name: "ix_refresh_tokens_token",
    column: "token",
    unique: true);
```
**Status: ⚠️** — token je **globalno** unique. Tehnički nije security risk (tokens su Guid + crypto), ali constraint je preusko za multi-tenant (jedan tenant ne može da reciklira token koji je već u drugom — što je u praksi nemoguće, ali model nije čist).

#### `20260210170702_InitialCreate.cs:65–70` — tenants.code
**Status: ✅** — globalno unique je ispravno (Tenant je root).

### 17b. Enum DB mapping consistency

| Enum | Conversion | DB Column Type | Konfiguracija |
|------|------------|----------------|---------------|
| OrderStatus | `.HasConversion<string>()` | `varchar(20)` | OrderConfiguration.cs:28 |
| OrderType | `.HasConversion<string>()` | `varchar(20)` | OrderConfiguration.cs:33 |
| ProcessStatus | `.HasConversion<string>()` | `varchar(20)` | OrderItemProcessConfiguration.cs:18 |
| OrderItemSubProcessStatus | `.HasConversion<string>()` | `varchar(20)` | OrderItemSubProcessConfiguration.cs:18 |
| ChangeRequestType | `.HasConversion<string>()` | `varchar(30)` | ChangeRequestConfiguration.cs:18 |
| RequestStatus | `.HasConversion<string>()` | `varchar(20)` | ChangeRequestConfiguration.cs:27 |
| NotificationType | `.HasConversion<string>()` | `varchar(30)` | NotificationConfiguration.cs:18 |
| UserRole | `.HasConversion<string>()` | `varchar(50)` | UserConfiguration.cs:32 |

**Status: ✅** — svi enum-ovi su konzistentno mapirani na string sa max length-om.

### 17c. DateTime UTC vs local

**Defaults**:
- `AuditableEntity.cs:10`: `CreatedAt = DateTime.UtcNow`
- `BlockRequest.cs:14, 63, 75, 85`: `DateTime.UtcNow`
- `ChangeRequest.cs:14, 50, 62`: `DateTime.UtcNow`
- `RefreshToken.cs:30`: `IsValid() => ExpiresAt > DateTime.UtcNow`
- `UserProcess.cs:9`: `DateTime.UtcNow`

**Migrations** — sve `created_at`, `updated_at`, `delivery_date`, `check_in_time`, etc. → `timestamp with time zone`.

**Pretraga `DateTime.Now`**: 0 rezultata u domain logici (samo `DateTime.UtcNow`).

**Status: ✅** — konzistentno UTC. `fix-time-tracking-utc-dates` commit je očigledno popravio raniju neuobičajenost.

### 17d. Money / decimal preciznost

Pretraga `decimal` na entitetima: 0 rezultata za price/amount/cost/money.

Order quantities su `int`. Sistem je quantity-based, ne pricing-based.

**Status: ✅ N/A** — sistem ne barata novcem.

### 17e. String kolone bez max length

| Kolona | Max Length | Status |
|--------|-----------|--------|
| order_number | 50 | ✅ |
| order_notes | 2000 | ✅ |
| product_name | 255 | ✅ |
| process_code | 50 | ✅ |
| process_name | 200 | ✅ |
| user_email | 256 | ✅ |
| user_password_hash | text (unbounded) | ✅ intentional (bcrypt) |
| push_subscription_endpoint | text | ✅ intentional (Web Push API) |
| push_subscription_p256dh_key | text | ✅ intentional (base64 crypto) |
| push_subscription_auth_key | text | ✅ intentional (base64) |

**Status: ✅** — sve kolone ili imaju max length ili je `text` intentional.

---

## 18. Recovery scenariji

### 18a. GDPR Article 20 — "Export all my data"

**Pretraga**: `Export`, `GetAll.*Data`, `DownloadData` — 0 dedikovanih export endpoint-a.

**Status: ❌ NOT SUPPORTED** — nema endpoint-a `/api/tenants/{id}/export`. Ručan SQL dump bi bio jedina opcija.

### 18b. GDPR Article 17 — "Delete all my data"

**Tenant deletion**: nema explicit `DeleteTenantCommand` u kodu (pretraga 0 rezultata).

**Cascade behavior** (proveren u migracijama i konfiguracijama):
- `users.tenant_id`: `OnDelete(Cascade)`
- `orders.tenant_id`: `OnDelete(Cascade)`
- `processes.tenant_id`: `OnDelete(Cascade)`
- Sve junction tables (`user_processes`, `product_category_processes`): cascade

**Audit trail**: `AuditableEntity` postoji ali ne pravi separate audit log table. Nema orphan audit data koja bi ostala posle delete-a.

**Status: ⚠️ PARTIAL** — cascade delete tehnički radi (hard delete), ali:
- Nema explicit `DeleteTenant` API endpoint-a
- Nema soft delete-a (jednom deleted = nepovratno)
- Nema `confirm token` mehanizma za zaštitu

### 18c. Granular restore (recover 100 specific orders)

**Trenutno stanje**: hard delete preko `_orderRepository.Delete(order)`. Bez soft delete polja (`is_deleted`, `deleted_at`). Bez PITR setupa.

**Status: ❌ NOT SUPPORTED** — jednom obrisano, gone. Restore moguć samo kroz pun DB restore (gubi sve promene posle backup-a).

### 18d. PITR (single-table corruption recovery)

**Render**: Render može da koristi managed Postgres (Neon, Supabase) koji imaju PITR na plaćenim planovima. Render konfiguracija nije u repo-u.

**DigitalOcean**: bare droplet, verovatno self-managed Postgres. Nema dokumentacije o WAL archiving-u u repo-u.

**Status: ❌ AUDIT GAP** — moguće da postoji na infrastructure nivou ali nije dokumentovano. Bez RTO/RPO definicije.

### 18e. Whole tenant accidentally dropped

Vidi 18b. Cascade delete je ispravno setup-ovan, ali nema:
- Soft delete za tenants (`IsActive=false` postoji ali ne sprečava cascade)
- Confirmation flow u API-ju
- Recovery mehanizma

**Status: ⚠️** — cascade radi, ali jedan pogrešan klik može izbrisati ceo tenant nepovratno.

### 18f. Migration failure mid-flight

**`Program.cs:131–138`**:
```csharp
using var migrationScope = app.Services.CreateScope();
var sp = migrationScope.ServiceProvider;
await sp.GetRequiredService<TenancyDbContext>().Database.MigrateAsync();
await sp.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
await sp.GetRequiredService<ProductionDbContext>().Database.MigrateAsync();
await sp.GetRequiredService<OrdersDbContext>().Database.MigrateAsync();
```

**Issues**:
- 4 nezavisna `MigrateAsync()` poziva, bez transaction wrapper-a
- Ako migracija 3 (Production) fail-uje posle uspešne migracije 2 (Identity), partial state
- EF Core `MigrateAsync()` jeste idempotent (čuva history u `__EFMigrationsHistory` per schema), ali partial schema može da ostane
- Bez timeout-a, hang migration može da blokira startup zauvek

**Status: ⚠️** — multi-DB migracije bez transactional guarantee-a. Trebaju cancellation token sa timeout-om i dokumentovan rollback procedure.

### 18g. Disk full from attachments

**Pretraga**: `DriveInfo`, `disk`, `quota`, `available.*bytes` u `.cs` fajlovima — 0 rezultata.

**Findings**:
- ❌ Nema disk usage check-a pre upload-a
- ❌ Nema per-tenant attachment quota
- ❌ Nema alerting-a kada se disk popuni
- ✅ Per-file size limit (10 MB) postoji preko `FileStorageSettings.MaxFileSizeBytes`

**Trenutni recovery scenario**: kada se disk napuni, upload pucа sa generic error-om, bez alerting-a, dok admin ručno ne primeti.

**Status: 🔴** — nema safeguard-a. Treba `DriveInfo.GetTotalFreeSpace()` check + per-tenant quota + alert.

### 18h. One endpoint hogs DB connections

**Connection string** (`appsettings.json:10`):
```
Host=localhost;Port=5432;Database=algreen_mes;Username=postgres;Password=postgres
```

**Eksplicitne settings**:
- `Maximum Pool Size`: nije postavljen (Npgsql default = 100)
- `CommandTimeout`: nije postavljen (Npgsql default = 30s)
- `Connection Lifetime`: nije postavljen
- `Pooling=true`: implicit (Npgsql default)

**EF Core CommandTimeout**: pretraga `CommandTimeout` u `*InfrastructureServiceRegistration.cs` — 0 rezultata.

**Status: ⚠️** — default Npgsql limiti, ali bez explicit konfiguracije. Slow query može da iscrpi pool. Bez per-endpoint timeout middleware-a.

**Predlog**:
```csharp
options.UseNpgsql(connectionString, npgsql => {
    npgsql.CommandTimeout(30);
    npgsql.EnableRetryOnFailure(3);
});
```
+ connection string append: `;Maximum Pool Size=20;CommandTimeout=30`.

---

## Konsolidovani ažurirani sumarni nalazi

### 🔴 KRITIČNO (popraviti pre 2. tenanta) — dopuna
36. **Offline pending actions store** se ne briše na logout (`apps/tablet/src/offline/offline-store.ts`).
37. **CORS overly permissive** — potvrđeno (`Program.cs:102–111`, `SetIsOriginAllowed(_ => true).AllowCredentials()`).
38. **VAPID private key u repo-u** (`appsettings.Development.json:14`).
39. **Server IP `46.101.166.137` u deploy skripti** (`deploy-windows.sh:7–8`).
40. **0 backend testova, 0 frontend testova** — multi-tenant izmene se ne mogu validirati.
41. **0 tenant isolation testova** — niti jedan test ne potvrđuje cross-tenant zaštitu.

### 🟡 HIGH — dopuna
42. **Slabija password policy** (min 6, no complexity) — `CreateUserCommandValidator.cs:13`.
43. **JWT secret bez rotation mehanizma** — `Program.cs:40–59`.
44. **Migration runtime bez transaction wrapper-a** — `Program.cs:131–138`.
45. **DB connection pool bez explicit limita ni timeout-a** — connection strings.
46. **CreateOrderCommandHandler N+1 query problem** (`CreateOrderCommandHandler.cs:62–91`).
47. **OrderRepository deep Include chain-ovi (4-level)** — duplicate logic, Cartesian explosion risk.
48. **React Query keys nisu konzistentno tenant-scoped** — neki kao `['orders']` bez tenantId.
49. **React Query cache se ne briše na logout** — stale data persist 30s posle logout-a.
50. **.NET 9 STS ističe 2026-05-12** — 14 dana do EOL od audit datuma.
51. **Node 20 LTS ističe 2026-04-30** — 2 dana od audit datuma.
52. **Successful auth events se ne loguju** (login success, refresh success, password change success) — compliance gap.
53. **High-value mutation logs nedostaju** (CreateOrder, DeleteUser, CancelOrder).

### 🟢 MEDIUM — dopuna
54. **Date formatting hardcoded `'sr-Latn-RS'` locale** — `apps/tablet/src/pages/checkout/CheckOutPage.tsx:68`.
55. **Branding (logo, theme colors) globalno hardcoded** — nije per-tenant.
56. **Production URLs (`tracker-api.algreen.rs`) u deploy.sh** — vidljivo u git-u.
57. **Bez `.nvmrc` ili `.tool-versions`** — Node verzija nije pinned.
58. **Bez Dependabot / Renovate** — ručno praćenje security patch-a.
59. **GDPR Article 20 (export) nije implementiran**.
60. **GDPR Article 17 (delete) tehnički podržan ali bez safeguard-a**.
61. **Bez disk space check-a / per-tenant attachment quota** — disk full = silent failure.
62. **Error response shape ≠ RFC 7807 ProblemDetails** — non-standard.
63. **Bez fajl-based logging-a / log retention-a** — samo console / journalctl.
64. **completed_at i created_at na orders bez explicit indeksa** — sort performance.
65. **Duplicirana metoda `GetByIdWithFullDetailsAsync` u OrderItemProcessRepository** (linije 30–36 i 40–46).
66. **DashboardQueryService O(N²) loop** (`GetLiveViewAsync`) — treba GroupBy.

### Statistike

**Ukupno nalaza** (prvi audit + dopuna):
- 🔴 Kritično: ~16 (uključujući 1 dodatni leak risk za offline store, više za missing testove)
- 🟡 High: ~17
- 🟢 Medium: ~33
- ✅ Safe: 100+ (sve potvrđene komponente)

**Komponente bez bilo kakve test pokrivenosti**: SVE.
**Komponente sa partial issues**: tenant scoping (sekcija 1, 9), password policy, logging, recovery.
**Komponente koje rade dobro**: data model (tenant_id svuda, UTC, enum mapping), file storage path strukture, frontend role gating, dependency hygiene.

---

## Šta dopuna NIJE pokrila

- **Pravi penetration test** — JWT signature attack, race conditions, SQL injection probe (kod je čist od raw SQL-a, ali property tests bi pomogli).
- **Performance under load** — bez load testing-a, ne znamo realni capacity (vidi raniji roadmap stage-ove).
- **Backup/restore drill** — nismo restore-ovali nigde.
- **Mobile / iPadOS specific issues** — service worker handler ima iOS-specific kod (`/_pending_navigate`), nije testiran.
- **Email deliverability** — nema email integracije za audit.

---

## Predlog redosleda popravki — proširen sa novim nalazima

Originalni redosled (prvi audit) + dopune:

1. **HasQueryFilter na DbContext-e** (rešava ~70% query 🔴 nalaza).
2. **CORS popravka** + **VAPID rotation** + **superadmin authorize na TenantsController**.
3. **`SaveChangesInterceptor` za audit kolone** + auth/mutation logging discipline.
4. **Health check endpoint** + **DB connection pool config** + **CommandTimeout**.
5. **SignalR group join validation** + tenant attachment download check.
6. **Logout cleanup**: clear React Query cache + offline pending actions.
7. **Refactor seedera** u BaselineSeeder + TenantSeeder + DemoDataSeeder.
8. **`tenant_features` tabela** + per-tenant config (logo, theme).
9. **Rate limiting** + **Sentry** + **structured logging**.
10. **Backup automation** + **migration discipline** (separate step od app startup).
11. **Test infrastructure setup** — bar tenant isolation tests + smoke tests pre prvog spoljašnjeg deploya.
12. **.NET 10 LTS migration** (do 2026-05-12) + **Node 22 LTS upgrade**.
13. **Password policy hardening** (min 12, complexity, password history).
14. **Disk monitoring** + **per-tenant attachment quota**.
15. **Per-tenant data export endpoint** (GDPR Article 20).
16. **Soft delete za tenants** (Article 17 safeguard).
17. **Migration transaction wrapping** + dokumentovan rollback.
18. **HSTS** + **security headers middleware** + **API versioning**.
19. **Schema-per-tenant ili DB-per-tenant** (kada broj klijenata pređe ~10).

---

**Kraj dopune.** Ovo + prvi deo audita čine kompletnu sliku sistema na dan 2026-04-28.
