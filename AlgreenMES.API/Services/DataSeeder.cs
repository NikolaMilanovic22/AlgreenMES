using AlGreenMES.Modules.Identity.Application.Services;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Modules.Identity.Infrastructure.Persistence;
using AlGreenMES.Modules.Orders.Domain.Entities;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Infrastructure.Persistence;
using AlGreenMES.Modules.Production.Domain.Entities;
using AlGreenMES.Modules.Production.Infrastructure.Persistence;
using AlGreenMES.Modules.Tenancy.Domain.Entities;
using AlGreenMES.Modules.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AlgreenMES.API.Services;

public static class DataSeeder
{
    public static async Task SeedAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var services = scope.ServiceProvider;

        var tenancyDb = services.GetRequiredService<TenancyDbContext>();
        var identityDb = services.GetRequiredService<IdentityDbContext>();
        var productionDb = services.GetRequiredService<ProductionDbContext>();
        var ordersDb = services.GetRequiredService<OrdersDbContext>();
        var passwordHasher = services.GetRequiredService<IPasswordHasher>();

        // 1. Get or create Demo Tenant
        var tenant = await tenancyDb.Tenants.FirstOrDefaultAsync(t => t.Code == "DEMO");
        if (tenant == null)
        {
            tenant = Tenant.Create("Demo Company", "demo");
            tenancyDb.Tenants.Add(tenant);
            await tenancyDb.SaveChangesAsync();
        }

        var tenantId = tenant.Id;

        // DataSeeder runs at startup with no HTTP context — every query against a tenant-scoped
        // entity must IgnoreQueryFilters and pass tenantId explicitly.

        // 2. Get or create Admin User
        var adminUser = await identityDb.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == "admin@demo.com" && u.TenantId == tenantId);
        if (adminUser == null)
        {
            var passwordHash = passwordHasher.HashPassword("Admin123!");
            adminUser = User.Create(tenantId, "admin@demo.com", passwordHash, "Admin", "User", UserRole.Admin);
            identityDb.Users.Add(adminUser);
            await identityDb.SaveChangesAsync();
        }
        else if (!passwordHasher.VerifyPassword("Admin123!", adminUser.PasswordHash))
        {
            // Reset password if hash is invalid
            adminUser.ChangePassword(passwordHasher.HashPassword("Admin123!"));
            await identityDb.SaveChangesAsync();
        }

        // 3. Get or create Processes A-K
        var existingProcesses = await productionDb.Processes
            .IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId)
            .ToListAsync();

        var processDefs = new (string Code, string Name, int Order)[]
        {
            ("A", "Krojenje", 1),
            ("B", "Kantiranje", 2),
            ("C", "CNC", 3),
            ("D", "Bušenje", 4),
            ("E", "Montaža", 5),
            ("F", "Brušenje", 6),
            ("G", "Grundiranje", 7),
            ("H", "Farbanje", 8),
            ("I", "Lakiranje", 9),
            ("J", "Pakiranje", 10),
            ("K", "Kontrola kvaliteta", 11)
        };

        var processEntities = new List<Process>();
        var needsSave = false;
        foreach (var (code, name, order) in processDefs)
        {
            var existing = existingProcesses.FirstOrDefault(p => p.Code == code);
            if (existing != null)
            {
                processEntities.Add(existing);
            }
            else
            {
                var process = Process.Create(tenantId, code, name, order, adminUser.Id);
                processEntities.Add(process);
                productionDb.Processes.Add(process);
                needsSave = true;
            }
        }

        if (needsSave)
            await productionDb.SaveChangesAsync();

        // 4. Get or create Product Category "Vrata Pivot" with all processes
        var pivotDoors = await productionDb.ProductCategories
            .IgnoreQueryFilters()
            .Include(c => c.Processes)
            .Include(c => c.Dependencies)
            .FirstOrDefaultAsync(c => c.Name == "Vrata Pivot" && c.TenantId == tenantId);

        if (pivotDoors == null)
        {
            pivotDoors = ProductCategory.Create(tenantId, "Vrata Pivot", "Pivot vrata - standardna kategorija", adminUser.Id);
            productionDb.ProductCategories.Add(pivotDoors);

            for (var i = 0; i < processEntities.Count; i++)
            {
                pivotDoors.AddProcess(processEntities[i].Id, i + 1);
            }

            // Add process dependencies:
            // Grundiranje (G) depends on Brušenje (F)
            // Farbanje (H) depends on Grundiranje (G)
            // Lakiranje (I) depends on Farbanje (H)
            var brusenje = processEntities.First(p => p.Code == "F");
            var grundiranje = processEntities.First(p => p.Code == "G");
            var farbanje = processEntities.First(p => p.Code == "H");
            var lakiranje = processEntities.First(p => p.Code == "I");

            pivotDoors.AddDependency(grundiranje.Id, brusenje.Id);
            pivotDoors.AddDependency(farbanje.Id, grundiranje.Id);
            pivotDoors.AddDependency(lakiranje.Id, farbanje.Id);

            await productionDb.SaveChangesAsync();
        }

        // 5. Get or create Special Request Types
        if (!await productionDb.SpecialRequestTypes.IgnoreQueryFilters()
            .AnyAsync(s => s.Code == "PESK" && s.TenantId == tenantId))
        {
            var peskarenje = SpecialRequestType.Create(tenantId, "PESK", "Peskarenje", "Dodaje proces peskarenja prije farbanja");
            productionDb.SpecialRequestTypes.Add(peskarenje);

            var brusenje = processEntities.First(p => p.Code == "F");
            var grundiranje = processEntities.First(p => p.Code == "G");
            var farbanje = processEntities.First(p => p.Code == "H");
            var lakiranje = processEntities.First(p => p.Code == "I");

            var samoFarbanje = SpecialRequestType.Create(tenantId, "SF", "Samo farbanje", "Samo proces farbanja - preskače ostale procese");
            samoFarbanje.SetOnlyProcesses(
                brusenje.Id,
                grundiranje.Id,
                farbanje.Id,
                lakiranje.Id
            );
            productionDb.SpecialRequestTypes.Add(samoFarbanje);

            await productionDb.SaveChangesAsync();
        }

        // =====================================================================
        // NEW SEED DATA
        // =====================================================================

        // 6. More Users
        var demoPassword = passwordHasher.HashPassword("Demo123!");
        var userDefs = new (string Email, string FirstName, string LastName, UserRole Role, string[] ProcessCodes)[]
        {
            ("manager@demo.com", "Marko", "Marković", UserRole.Manager, []),
            ("sales@demo.com", "Ana", "Anić", UserRole.SalesManager, []),
            ("coord@demo.com", "Ivan", "Ivić", UserRole.Coordinator, []),
            ("worker1@demo.com", "Petar", "Petrović", UserRole.Department, ["A", "B"]),
            ("worker2@demo.com", "Jovan", "Jovanović", UserRole.Department, ["E"]),
            ("worker3@demo.com", "Nikola", "Nikolić", UserRole.Department, ["F", "G", "H"]),
            ("worker4@demo.com", "Luka", "Lukić", UserRole.Department, ["J", "K"]),
        };

        var userEntities = new Dictionary<string, User> { ["admin@demo.com"] = adminUser };

        foreach (var (email, firstName, lastName, role, processCodes) in userDefs)
        {
            var user = await identityDb.Users
                .IgnoreQueryFilters()
                .Include(u => u.UserProcesses)
                .FirstOrDefaultAsync(u => u.Email == email && u.TenantId == tenantId);
            if (user == null)
            {
                user = User.Create(tenantId, email, demoPassword, firstName, lastName, role);
                if (processCodes.Length > 0)
                {
                    var processIds = processCodes
                        .Select(code => processEntities.First(p => p.Code == code).Id)
                        .ToList();
                    user.AssignProcesses(tenantId, processIds);
                }
                identityDb.Users.Add(user);
            }
            userEntities[email] = user;
        }

        await identityDb.SaveChangesAsync();

        // 7. Shifts
        var shiftDefs = new (string Name, TimeOnly Start, TimeOnly End)[]
        {
            ("Jutarnja smjena", new TimeOnly(6, 0), new TimeOnly(14, 0)),
            ("Popodnevna smjena", new TimeOnly(14, 0), new TimeOnly(22, 0)),
            ("Noćna smjena", new TimeOnly(22, 0), new TimeOnly(6, 0)),
        };

        foreach (var (name, start, end) in shiftDefs)
        {
            if (!await identityDb.Shifts.IgnoreQueryFilters()
                .AnyAsync(s => s.Name == name && s.TenantId == tenantId))
            {
                var shift = Shift.Create(tenantId, name, start, end);
                identityDb.Shifts.Add(shift);
            }
        }

        await identityDb.SaveChangesAsync();

        // 8. Sub-Processes
        var processesWithSubs = await productionDb.Processes
            .IgnoreQueryFilters()
            .Include(p => p.SubProcesses)
            .Where(p => p.TenantId == tenantId)
            .ToListAsync();

        var subProcessDefs = new (string ProcessCode, (string Name, int Order)[] Subs)[]
        {
            ("E", new[] { ("Predmontaža", 1), ("Finalna montaža", 2) }),
            ("H", new[] { ("Priprema površine", 1), ("Nanošenje boje", 2), ("Sušenje", 3) }),
            ("I", new[] { ("Priprema laka", 1), ("Nanošenje laka", 2), ("Sušenje laka", 3) }),
        };

        var needsSubSave = false;
        foreach (var (processCode, subs) in subProcessDefs)
        {
            var process = processesWithSubs.First(p => p.Code == processCode);
            foreach (var (subName, subOrder) in subs)
            {
                if (!process.SubProcesses.Any(sp => sp.Name == subName || sp.SequenceOrder == subOrder))
                {
                    var subProcess = process.AddSubProcess(subName, subOrder);
                    productionDb.SubProcesses.Add(subProcess);
                    needsSubSave = true;
                }
            }
        }

        if (needsSubSave)
            await productionDb.SaveChangesAsync();

        // Reload processes with sub-processes to get IDs
        processesWithSubs = await productionDb.Processes
            .IgnoreQueryFilters()
            .Include(p => p.SubProcesses)
            .Where(p => p.TenantId == tenantId)
            .ToListAsync();

        // 9. More Product Categories
        // "Vrata Standard" — A, B, D, E, F, G, H, I, J, K (skip C/CNC)
        var vrataStandard = await productionDb.ProductCategories
            .IgnoreQueryFilters()
            .Include(c => c.Processes)
            .Include(c => c.Dependencies)
            .FirstOrDefaultAsync(c => c.Name == "Vrata Standard" && c.TenantId == tenantId);

        if (vrataStandard == null)
        {
            vrataStandard = ProductCategory.Create(tenantId, "Vrata Standard", "Standardna vrata bez CNC-a", adminUser.Id);
            productionDb.ProductCategories.Add(vrataStandard);

            var standardProcessCodes = new[] { "A", "B", "D", "E", "F", "G", "H", "I", "J", "K" };
            var seqOrder = 1;
            foreach (var code in standardProcessCodes)
            {
                var process = processEntities.First(p => p.Code == code);
                vrataStandard.AddProcess(process.Id, seqOrder++);
            }

            AddSurfaceDependencies(vrataStandard, processEntities);
            await productionDb.SaveChangesAsync();
        }

        // "Prozori" — A, B, C, F, G, H, I, J, K
        var prozori = await productionDb.ProductCategories
            .IgnoreQueryFilters()
            .Include(c => c.Processes)
            .Include(c => c.Dependencies)
            .FirstOrDefaultAsync(c => c.Name == "Prozori" && c.TenantId == tenantId);

        if (prozori == null)
        {
            prozori = ProductCategory.Create(tenantId, "Prozori", "Prozori - standardna kategorija", adminUser.Id);
            productionDb.ProductCategories.Add(prozori);

            var prozoriProcessCodes = new[] { "A", "B", "C", "F", "G", "H", "I", "J", "K" };
            var seqOrder = 1;
            foreach (var code in prozoriProcessCodes)
            {
                var process = processEntities.First(p => p.Code == code);
                prozori.AddProcess(process.Id, seqOrder++);
            }

            AddSurfaceDependencies(prozori, processEntities);
            await productionDb.SaveChangesAsync();
        }

        // =====================================================================
        // 10. Orders with Items, Processes, Sub-Processes, and State Progression
        // =====================================================================

        if (await ordersDb.Orders.IgnoreQueryFilters()
            .AnyAsync(o => o.OrderNumber == "ORD-2026-001" && o.TenantId == tenantId))
            return; // Orders already seeded — skip the rest

        // Reload all categories with their processes for order item creation
        var allCategories = await productionDb.ProductCategories
            .IgnoreQueryFilters()
            .Include(c => c.Processes)
            .Where(c => c.TenantId == tenantId)
            .ToListAsync();

        var catPivot = allCategories.First(c => c.Name == "Vrata Pivot");
        var catStandard = allCategories.First(c => c.Name == "Vrata Standard");
        var catProzori = allCategories.First(c => c.Name == "Prozori");

        // Helper: add processes and sub-processes to an order item based on its category
        void AddProcessesAndSubProcesses(OrderItem item, ProductCategory category)
        {
            foreach (var catProc in category.Processes.OrderBy(cp => cp.SequenceOrder))
            {
                var oip = item.AddProcess(catProc.ProcessId, catProc.DefaultComplexity);
                // Add sub-processes if the production process has any
                var prodProcess = processesWithSubs.FirstOrDefault(p => p.Id == catProc.ProcessId);
                if (prodProcess != null)
                {
                    foreach (var sub in prodProcess.SubProcesses.Where(s => s.IsActive).OrderBy(s => s.SequenceOrder))
                    {
                        oip.AddSubProcess(sub.Id);
                    }
                }
            }
        }

        // Helper: complete a process (start, complete sub-processes, then complete)
        void CompleteProcess(OrderItemProcess oip)
        {
            if (oip.Status == ProcessStatus.Pending)
                oip.Start();
            foreach (var sp in oip.SubProcesses)
            {
                if (sp.Status == SubProcessStatus.Pending) sp.Start();
                if (sp.Status == SubProcessStatus.InProgress) sp.Complete();
            }
            oip.Complete();
        }

        // --- ORD-2026-001: Standard, Priority 1, +14 days, Active ---
        // 2 items of Vrata Pivot; item1 process A started; item2 process A completed, B started
        var order1 = Order.Create(tenantId, "ORD-2026-001", DateTime.UtcNow.AddDays(14),
            1, OrderType.Standard, adminUser.Id, "Prva narudžba - pivot vrata");
        var item1_1 = order1.AddItem(catPivot.Id, "Vrata Pivot", 2, "Stavka 1");
        AddProcessesAndSubProcesses(item1_1, catPivot);
        var item1_2 = order1.AddItem(catPivot.Id, "Vrata Pivot", 2, "Stavka 2");
        AddProcessesAndSubProcesses(item1_2, catPivot);
        ordersDb.Orders.Add(order1);

        order1.Activate();

        // Item 1: process A InProgress
        var item1_1_procA = item1_1.Processes.First(p => p.ProcessId == processEntities.First(pe => pe.Code == "A").Id);
        item1_1_procA.Start();

        // Item 2: process A completed, process B started
        var item1_2_procA = item1_2.Processes.First(p => p.ProcessId == processEntities.First(pe => pe.Code == "A").Id);
        CompleteProcess(item1_2_procA);
        var item1_2_procB = item1_2.Processes.First(p => p.ProcessId == processEntities.First(pe => pe.Code == "B").Id);
        item1_2_procB.Start();

        await ordersDb.SaveChangesAsync();

        // --- ORD-2026-002: Standard, Priority 2, +21 days, Draft ---
        var order2 = Order.Create(tenantId, "ORD-2026-002", DateTime.UtcNow.AddDays(21),
            2, OrderType.Standard, adminUser.Id, "Druga narudžba - standardna vrata");
        var item2_1 = order2.AddItem(catStandard.Id, "Vrata Standard", 1);
        AddProcessesAndSubProcesses(item2_1, catStandard);
        ordersDb.Orders.Add(order2);
        await ordersDb.SaveChangesAsync();

        // --- ORD-2026-003: Repair, Priority 1, +7 days, Active ---
        // Prozori; processes A,B,C completed; F InProgress
        var order3 = Order.Create(tenantId, "ORD-2026-003", DateTime.UtcNow.AddDays(7),
            1, OrderType.Repair, adminUser.Id, "Popravka prozora");
        var item3_1 = order3.AddItem(catProzori.Id, "Prozori", 1);
        AddProcessesAndSubProcesses(item3_1, catProzori);
        ordersDb.Orders.Add(order3);

        order3.Activate();

        // Complete A, B, C
        var prozoriCodes = new[] { "A", "B", "C" };
        foreach (var code in prozoriCodes)
        {
            var proc = item3_1.Processes.First(p => p.ProcessId == processEntities.First(pe => pe.Code == code).Id);
            CompleteProcess(proc);
        }

        // Start F (InProgress)
        var item3_1_procF = item3_1.Processes.First(p => p.ProcessId == processEntities.First(pe => pe.Code == "F").Id);
        item3_1_procF.Start();

        await ordersDb.SaveChangesAsync();

        // --- ORD-2026-004: Standard, Priority 3, +30 days, Paused ---
        var order4 = Order.Create(tenantId, "ORD-2026-004", DateTime.UtcNow.AddDays(30),
            3, OrderType.Standard, adminUser.Id, "Narudžba na čekanju");
        var item4_1 = order4.AddItem(catPivot.Id, "Vrata Pivot", 1);
        AddProcessesAndSubProcesses(item4_1, catPivot);
        ordersDb.Orders.Add(order4);

        order4.Activate();
        order4.Pause();

        await ordersDb.SaveChangesAsync();

        // --- ORD-2026-005: Complaint, Priority 2, Completed ---
        // Create with future date (domain validates), then complete all processes
        var order5 = Order.Create(tenantId, "ORD-2026-005", DateTime.UtcNow.AddDays(1),
            2, OrderType.Complaint, adminUser.Id, "Reklamacija - završena");
        var item5_1 = order5.AddItem(catStandard.Id, "Vrata Standard", 1);
        AddProcessesAndSubProcesses(item5_1, catStandard);
        ordersDb.Orders.Add(order5);

        order5.Activate();

        // Complete ALL processes
        foreach (var proc in item5_1.Processes)
        {
            CompleteProcess(proc);
        }

        order5.MarkCompleted();
        await ordersDb.SaveChangesAsync();

        // =====================================================================
        // 10b. Orders that create "Incoming" data for tablet
        // These have processes partially done so that later processes show as incoming
        // (pending with unmet dependencies)
        // =====================================================================

        // --- ORD-2026-006: Pivot, Priority 1, +10 days, Active ---
        // A-F completed, G (Grundiranje) InProgress → H (Farbanje) pending, blocked by G
        // This shows as "incoming" for worker3 (Nikola, process H)
        var order6 = Order.Create(tenantId, "ORD-2026-006", DateTime.UtcNow.AddDays(10),
            1, OrderType.Standard, adminUser.Id, "Hitna narudžba - pivot vrata za klijenta Petrović");
        var item6_1 = order6.AddItem(catPivot.Id, "Vrata Pivot", 3, "Stavka 1 - bijela");
        AddProcessesAndSubProcesses(item6_1, catPivot);
        ordersDb.Orders.Add(order6);

        order6.Activate();

        // Complete A through F
        foreach (var code in new[] { "A", "B", "C", "D", "E", "F" })
        {
            var proc = item6_1.Processes.First(p => p.ProcessId == processEntities.First(pe => pe.Code == code).Id);
            CompleteProcess(proc);
        }
        // Start G (Grundiranje) — InProgress, so H is blocked by G
        var item6_1_procG = item6_1.Processes.First(p => p.ProcessId == processEntities.First(pe => pe.Code == "G").Id);
        item6_1_procG.Start();

        await ordersDb.SaveChangesAsync();

        // --- ORD-2026-007: Standard, Priority 2, +5 days, Active ---
        // A-E completed, F (Brušenje) InProgress → G pending (blocked by F), H pending (blocked by G)
        // Shows as incoming for worker3 (process H) — blocked by G which is blocked by F
        var order7 = Order.Create(tenantId, "ORD-2026-007", DateTime.UtcNow.AddDays(5),
            2, OrderType.Repair, adminUser.Id, "Popravka vrata - hitan rok");
        var item7_1 = order7.AddItem(catStandard.Id, "Vrata Standard", 1, "Popravka okvira");
        AddProcessesAndSubProcesses(item7_1, catStandard);
        ordersDb.Orders.Add(order7);

        order7.Activate();

        // Vrata Standard uses: A, B, D, E, F, G, H, I, J, K (no C)
        foreach (var code in new[] { "A", "B", "D", "E" })
        {
            var proc = item7_1.Processes.First(p => p.ProcessId == processEntities.First(pe => pe.Code == code).Id);
            CompleteProcess(proc);
        }
        // Start F (Brušenje) — InProgress
        var item7_1_procF = item7_1.Processes.First(p => p.ProcessId == processEntities.First(pe => pe.Code == "F").Id);
        item7_1_procF.Start();

        await ordersDb.SaveChangesAsync();

        // --- ORD-2026-008: Pivot, Priority 3, +18 days, Active ---
        // A-F completed, G completed, H pending but with sub-processes
        // This one goes to queue (not incoming) for worker3 since G is done
        // BUT also: second item where A-D completed, E InProgress
        // For worker3: item2 has H blocked by G (which is pending, blocked by F which is pending)
        var order8 = Order.Create(tenantId, "ORD-2026-008", DateTime.UtcNow.AddDays(18),
            3, OrderType.Standard, adminUser.Id, "Velika narudžba - 2 stavke pivot vrata");
        var item8_1 = order8.AddItem(catPivot.Id, "Vrata Pivot", 2, "Stavka 1 - crna");
        AddProcessesAndSubProcesses(item8_1, catPivot);
        var item8_2 = order8.AddItem(catPivot.Id, "Vrata Pivot", 1, "Stavka 2 - siva");
        AddProcessesAndSubProcesses(item8_2, catPivot);
        ordersDb.Orders.Add(order8);

        order8.Activate();

        // Item 1: A-G completed → H is in queue for worker3 (not incoming)
        foreach (var code in new[] { "A", "B", "C", "D", "E", "F", "G" })
        {
            var proc = item8_1.Processes.First(p => p.ProcessId == processEntities.First(pe => pe.Code == code).Id);
            CompleteProcess(proc);
        }

        // Item 2: A-D completed, E InProgress → F,G,H all pending
        // H is blocked by G (which is blocked by F which is pending)
        foreach (var code in new[] { "A", "B", "C", "D" })
        {
            var proc = item8_2.Processes.First(p => p.ProcessId == processEntities.First(pe => pe.Code == code).Id);
            CompleteProcess(proc);
        }
        var item8_2_procE = item8_2.Processes.First(p => p.ProcessId == processEntities.First(pe => pe.Code == "E").Id);
        item8_2_procE.Start();

        await ordersDb.SaveChangesAsync();

        // =====================================================================
        // 11. Work Sessions
        // =====================================================================

        var worker1 = userEntities["worker1@demo.com"];
        var worker2 = userEntities["worker2@demo.com"];
        var worker3 = userEntities["worker3@demo.com"];
        var manager = userEntities["manager@demo.com"];
        var sales = userEntities["sales@demo.com"];
        var coord = userEntities["coord@demo.com"];

        // Worker1 (Petar) — checked out
        var ws1 = WorkSession.CheckIn(tenantId, worker1.Id);
        ws1.CheckOut();
        ordersDb.WorkSessions.Add(ws1);

        // Worker2 (Jovan) — still active
        var ws2 = WorkSession.CheckIn(tenantId, worker2.Id);
        ordersDb.WorkSessions.Add(ws2);

        // Worker3 (Nikola) — checked out
        var ws3 = WorkSession.CheckIn(tenantId, worker3.Id);
        ws3.CheckOut();
        ordersDb.WorkSessions.Add(ws3);

        await ordersDb.SaveChangesAsync();

        // =====================================================================
        // 12. Block Requests
        // =====================================================================

        // Block request 1: ORD-2026-001, item 1, process B (Pending) — Pending block request
        var item1_1_procB = item1_1.Processes.First(p => p.ProcessId == processEntities.First(pe => pe.Code == "B").Id);
        var blockReq1 = BlockRequest.CreateForProcess(tenantId, item1_1_procB.Id,
            worker1.Id, "Nedostaje materijal za kantiranje");
        ordersDb.BlockRequests.Add(blockReq1);

        // Block request 2: ORD-2026-003, item 1, process F (InProgress) — Approved, process blocked
        var blockReq2 = BlockRequest.CreateForProcess(tenantId, item3_1_procF.Id,
            worker3.Id, "Stroj za brušenje u kvaru");
        blockReq2.Approve(manager.Id, "Odobreno - čeka se popravka stroja");
        item3_1_procF.Block(manager.Id, "Stroj za brušenje u kvaru");
        ordersDb.BlockRequests.Add(blockReq2);

        await ordersDb.SaveChangesAsync();

        // =====================================================================
        // 13. Change Requests
        // =====================================================================

        // PriorityChange on ORD-2026-001 — Pending
        var changeReq1 = ChangeRequest.Create(tenantId, order1.Id, sales.Id,
            ChangeRequestType.PriorityChange, "Kupac traži hitnu isporuku - povećati prioritet");
        ordersDb.ChangeRequests.Add(changeReq1);

        // Resume on ORD-2026-004 — Approved by admin
        var changeReq2 = ChangeRequest.Create(tenantId, order4.Id, manager.Id,
            ChangeRequestType.Resume, "Materijal stigao - nastaviti proizvodnju");
        changeReq2.Approve(adminUser.Id, "Odobreno");
        ordersDb.ChangeRequests.Add(changeReq2);

        // Cancel on ORD-2026-002 — Rejected by admin
        var changeReq3 = ChangeRequest.Create(tenantId, order2.Id, coord.Id,
            ChangeRequestType.Cancel, "Kupac želi otkazati narudžbu");
        changeReq3.Reject(adminUser.Id, "Odbijeno - narudžba je već u pripremi");
        ordersDb.ChangeRequests.Add(changeReq3);

        await ordersDb.SaveChangesAsync();

        // =====================================================================
        // 14. Notifications
        // =====================================================================

        var notif1 = Notification.Create(tenantId, manager.Id, NotificationType.DeadlineWarning,
            "Rok isporuke blizu", "Narudžba ORD-2026-003 ima rok isporuke za 7 dana.",
            "Order", order3.Id);
        ordersDb.Notifications.Add(notif1);

        var notif2 = Notification.Create(tenantId, manager.Id, NotificationType.BlockRequest,
            "Zahtjev za blokadu", "Novi zahtjev za blokadu procesa brušenja na ORD-2026-003.",
            "BlockRequest", blockReq2.Id);
        ordersDb.Notifications.Add(notif2);

        var notif3 = Notification.Create(tenantId, adminUser.Id, NotificationType.DeadlineCritical,
            "Kritičan rok", "Narudžba ORD-2026-005 ima istekao rok isporuke!",
            "Order", order5.Id);
        ordersDb.Notifications.Add(notif3);

        var notif4 = Notification.Create(tenantId, worker1.Id, NotificationType.ProcessCompleted,
            "Proces završen", "Proces krojenja je uspješno završen na stavci narudžbe ORD-2026-001.",
            "OrderItemProcess", item1_2_procA.Id);
        ordersDb.Notifications.Add(notif4);

        await ordersDb.SaveChangesAsync();
    }

    /// <summary>
    /// Adds standard surface treatment dependencies: G→F, H→G, I→H
    /// </summary>
    private static void AddSurfaceDependencies(ProductCategory category, List<Process> processEntities)
    {
        var brusenje = processEntities.First(p => p.Code == "F");
        var grundiranje = processEntities.First(p => p.Code == "G");
        var farbanje = processEntities.First(p => p.Code == "H");
        var lakiranje = processEntities.First(p => p.Code == "I");

        category.AddDependency(grundiranje.Id, brusenje.Id);
        category.AddDependency(farbanje.Id, grundiranje.Id);
        category.AddDependency(lakiranje.Id, farbanje.Id);
    }
}
