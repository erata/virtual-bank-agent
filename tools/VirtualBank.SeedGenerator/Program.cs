using System.Text.Json;

// Legacy tool name retained for scripts. EF Core DbMigrator now owns ALL schema and deterministic inserts.
// --check verifies canonical fixture shape and Keycloak sub identifiers; it never generates SQL.
static string FindRoot(string start)
{
    for (var d=new DirectoryInfo(start);d is not null;d=d.Parent)
        if (File.Exists(Path.Combine(d.FullName,"PROJECT_SPEC.md"))) return d.FullName;
    throw new InvalidOperationException("Repository root unavailable.");
}
var root=FindRoot(AppContext.BaseDirectory);
using var fixtures=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"infra","seed","fixtures.json")));
using var realm=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"infra","keycloak","realm-virtual-bank.json")));
using var payments=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"infra","seed","financial-fixtures.json")));
var users=realm.RootElement.GetProperty("users").EnumerateArray().Select(x=>x.GetProperty("username").GetString()!).ToHashSet(StringComparer.Ordinal);
var customers=fixtures.RootElement.GetProperty("customers").EnumerateArray().ToArray();
var accounts=fixtures.RootElement.GetProperty("accounts").EnumerateArray().ToArray();
var customerIds=customers.Select(x=>x.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);
if (customerIds.Count!=customers.Length) throw new InvalidOperationException("Duplicate Customer IDs in seed.");
foreach (var c in customers)
{
    if (c.TryGetProperty("keycloakUsername",out var login) && !users.Contains(login.GetString()!))
        throw new InvalidOperationException("Customer identity is absent from Keycloak: "+login.GetString());
}
foreach(var acc in accounts)
{
    if (!customerIds.Contains(acc.GetProperty("customerId").GetString()!)) throw new InvalidOperationException("Orphan seeded account.");
    if (acc.GetProperty("currency").GetString() is not ("EUR" or "USD" or "TRY"))
        throw new InvalidOperationException("Unexpected currency in demo fixture.");
}
foreach(var name in new[]{"scheduledPayments","transactions","standingOrders"})
    foreach(var row in payments.RootElement.GetProperty(name).EnumerateArray())
        if (!customerIds.Contains(row.GetProperty("customerId").GetString()!))
            throw new InvalidOperationException("Orphan payment seed entry: "+name);
if (!new[]{"EUR","USD","TRY"}.All(currency=>accounts.Any(a=>a.GetProperty("currency").GetString()==currency)))
    throw new InvalidOperationException("Seed must cover EUR/USD/TRY.");
Console.WriteLine($"EF seed fixtures valid: {customers.Length} customers, {accounts.Length} accounts; 3 currencies.");
return 0;
