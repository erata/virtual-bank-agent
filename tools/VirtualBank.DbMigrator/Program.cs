using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using VirtualBank.Infrastructure.Persistence.EntityFramework;

// This executable is the sole DDL/seeding authority. API roles never run migrations or seed writes.
// Roles customer_migrator/credit_migrator/document_migrator are LOGIN members of NOLOGIN schema owners.
var root = Environment.GetEnvironmentVariable("VIRTUALBANK_REPO") ?? Directory.GetCurrentDirectory();
var fixtures = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root,"infra","seed","fixtures.json")));
var payments = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root,"infra","seed","financial-fixtures.json")));
var realm = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root,"infra","keycloak","realm-virtual-bank.json")));
var subjectIds = realm.RootElement.GetProperty("users").EnumerateArray()
    .ToDictionary(x=>x.GetProperty("username").GetString()!, x=>x.GetProperty("id").GetString()!, StringComparer.Ordinal);
var only = args.Contains("--skip-documents", StringComparer.Ordinal);

// Never infer trusted principals from untrusted text, never migrate as runtime API or postgres superuser.
await using var customerConnection = await OwnedConnection("MIGRATOR_CUSTOMER_DB", "customer_owner");
await using var creditConnection = await OwnedConnection("MIGRATOR_CREDIT_DB", "credit_owner");
var co = new DbContextOptionsBuilder<CustomerDbContext>().UseNpgsql(customerConnection).Options;
var cr = new DbContextOptionsBuilder<CreditDbContext>().UseNpgsql(creditConnection).Options;
await using var customer = new CustomerDbContext(co);
await using var credit = new CreditDbContext(cr);

// Guard against incorrectly adopting pre-EF, SQL-created databases. We do not delete or overwrite legacy data.
await GuardLegacyAsync(customerConnection, "customer");
await GuardLegacyAsync(creditConnection, "credit");
var cApplied = (await customer.Database.GetAppliedMigrationsAsync()).ToHashSet(StringComparer.Ordinal);
var rApplied = (await credit.Database.GetAppliedMigrationsAsync()).ToHashSet(StringComparer.Ordinal);
const string customerInitial = "20261123010000_InitialCustomerSchema";
const string creditInitial = "20261123020000_InitialCreditSchema";
const string customerSecure = "20261123011000_SecureCustomerSchema";
const string creditSecure = "20261123021000_SecureCreditSchema";
if (!cApplied.Contains(customerInitial)) await customer.Database.MigrateAsync(customerInitial);
if (!rApplied.Contains(creditInitial)) await credit.Database.MigrateAsync(creditInitial);

// Seeding requires owner access before FORCE RLS policies are installed. Existing secured data is NEVER re-seeded.
if (!cApplied.Contains(customerSecure) && !rApplied.Contains(creditSecure))
{
    await SeedCustomersAsync(customer, fixtures.RootElement, subjectIds);
    await SeedCreditAsync(credit, fixtures.RootElement, subjectIds);
    await SeedPaymentsAsync(customer, payments.RootElement);
}
else if (!cApplied.Contains(customerSecure) || !rApplied.Contains(creditSecure))
    throw new InvalidOperationException("Partially secured DB: manual recovery required; no silent reseed.");
await customer.Database.MigrateAsync();
await credit.Database.MigrateAsync();

if (!only)
{
    await using var documentConnection = await OwnedConnection("MIGRATOR_DOCUMENTS_DB", "document_owner");
    await GuardLegacyAsync(documentConnection, "docs");
    var dopt = new DbContextOptionsBuilder<DocumentsDbContext>()
        .UseNpgsql(documentConnection, o => o.UseVector()).Options;
    await using var docs = new DocumentsDbContext(dopt);
    await docs.Database.MigrateAsync();
}
Console.WriteLine("EF Core Code First migrations applied; RLS enabled; demo seed verified.");

static async Task<NpgsqlConnection> OwnedConnection(string env, string role)
{
    var value = Environment.GetEnvironmentVariable(env);
    if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"Missing {env}");
    var builder = new NpgsqlConnectionStringBuilder(value);
    if (builder.Username is "postgres" or "customer_api" or "credit_api" or "document_api")
        throw new InvalidOperationException("Migrations require a dedicated migrator role, never a superuser or runtime API role.");
    var connection = new NpgsqlConnection(builder.ConnectionString);
    await connection.OpenAsync();
    try
    {
        await using var command = new NpgsqlCommand($"SET ROLE {role}", connection);
        await command.ExecuteNonQueryAsync();
        return connection;
    }
    catch { await connection.DisposeAsync(); throw; }
}

static async Task GuardLegacyAsync(NpgsqlConnection conn, string schema)
{
    const string sql = """
        SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = @schema AND table_name = @table)
           AND NOT EXISTS (SELECT 1 FROM information_schema.tables
                           WHERE table_schema = 'public' AND table_name = '__EFMigrationsHistory')
        """;
    await using var command = new NpgsqlCommand(sql, conn);
    command.Parameters.AddWithValue("schema", schema);
    command.Parameters.AddWithValue("table", schema switch
    {
        "customer" => "customers", "credit" => "credit_applications", _ => "documents"
    });
    if (await command.ExecuteScalarAsync() is true)
        throw new InvalidOperationException($"Legacy SQL schema detected in {schema}. Back up the volume and run a deliberate migration/reset; automatic EF adoption is unsafe.");
}

static string String(JsonElement e,string key) => e.GetProperty(key).GetString()!;
static string? SubjectOrNull(JsonElement e, IReadOnlyDictionary<string,string> users)
{
    if (!e.TryGetProperty("keycloakUsername",out var u) || u.ValueKind != JsonValueKind.String) return null;
    return users.TryGetValue(u.GetString()!,out var id) ? id : throw new InvalidOperationException("Unknown Keycloak subject in customer fixtures.");
}
static async Task SeedCustomersAsync(CustomerDbContext db, JsonElement fixture, IReadOnlyDictionary<string,string> users)
{
    if (await db.Customers.AnyAsync()) return;
    foreach (var x in fixture.GetProperty("customers").EnumerateArray())
        db.Customers.Add(new CustomerRow {
            Id=String(x,"id"),Branch=String(x,"branch"),FirstName=String(x,"firstName"),LastName=String(x,"lastName"),
            Email=String(x,"email"),Phone=String(x,"phone"),Iban=String(x,"iban"),DateOfBirth=DateOnly.Parse(String(x,"dateOfBirth")),
            Segment=String(x,"segment"),IdentitySubjectId=SubjectOrNull(x,users)
        });
    await db.SaveChangesAsync();
    foreach (var x in fixture.GetProperty("accounts").EnumerateArray())
        db.Accounts.Add(new AccountRow {
            Id=String(x,"id"),CustomerId=String(x,"customerId"),Type=String(x,"type"),
            Currency=String(x,"currency"),Balance=x.GetProperty("balance").GetDecimal()
        });
    // Read-only projection is derived from the same Credit fixture source, never maintained separately.
    var assignments = fixture.GetProperty("creditApplications").EnumerateArray()
        .Where(x=>x.GetProperty("assignedAnalyst").ValueKind==JsonValueKind.String)
        .Select(x=>(Customer:String(x,"customerId"), Analyst:users[String(x,"assignedAnalyst")])).Distinct();
    foreach(var (cid,analyst) in assignments)
        db.CreditAssignments.Add(new CreditAssignmentRow { CustomerId=cid,AnalystId=analyst });
    await db.SaveChangesAsync();
}
static async Task SeedCreditAsync(CreditDbContext db, JsonElement fixture, IReadOnlyDictionary<string,string> users)
{
    if (await db.CreditApplications.AnyAsync()) return;
    foreach(var x in fixture.GetProperty("creditApplications").EnumerateArray())
    {
        var prep = String(x,"preparedBy");
        var ass = x.GetProperty("assignedAnalyst");
        db.CreditApplications.Add(new CreditApplicationRow {
            Id=String(x,"id"), CustomerId=String(x,"customerId"),Amount=x.GetProperty("amount").GetDecimal(),
            Currency=String(x,"currency"),Status=String(x,"status"),Purpose=String(x,"purpose"),
            PreparedBy=users.TryGetValue(prep,out var prepId)?prepId:prep,
            AssignedAnalystId=ass.ValueKind==JsonValueKind.String?users[ass.GetString()!]:null,Version=1
        });
    }
    await db.SaveChangesAsync();
}
static async Task SeedPaymentsAsync(CustomerDbContext db, JsonElement fixture)
{
    if (await db.ScheduledPayments.AnyAsync()) return;
    var today=DateOnly.FromDateTime(DateTime.UtcNow);
    foreach(var x in fixture.GetProperty("scheduledPayments").EnumerateArray())
        db.ScheduledPayments.Add(new ScheduledPaymentRow {
            Id=String(x,"id"),CustomerId=String(x,"customerId"),Description=String(x,"description"),
            Amount=x.GetProperty("amount").GetDecimal(),Currency=String(x,"currency"),
            DueDate=today.AddDays(x.GetProperty("daysFromToday").GetInt32()),Status=String(x,"status")
        });
    foreach(var x in fixture.GetProperty("transactions").EnumerateArray())
        db.Transactions.Add(new AccountTransactionRow {
            Id=String(x,"id"),CustomerId=String(x,"customerId"),Description=String(x,"description"),
            Amount=x.GetProperty("amount").GetDecimal(),Currency=String(x,"currency"),
            BookedOn=today.AddDays(x.GetProperty("daysFromToday").GetInt32())
        });
    foreach(var x in fixture.GetProperty("standingOrders").EnumerateArray())
        db.StandingOrders.Add(new StandingOrderRow {
            Id=String(x,"id"),CustomerId=String(x,"customerId"),Description=String(x,"description"),
            Amount=x.GetProperty("amount").GetDecimal(),Currency=String(x,"currency"),
            Frequency=String(x,"frequency"),NextDueDate=today.AddDays(x.GetProperty("daysFromToday").GetInt32()),Active=x.GetProperty("active").GetBoolean()
        });
    await db.SaveChangesAsync();
}
