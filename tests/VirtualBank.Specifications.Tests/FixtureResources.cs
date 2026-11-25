using System.Globalization;
using System.Text.Json;
using VirtualBank.Application.Authorization.Resources;
using VirtualBank.Application.Credit.Authorization;
using VirtualBank.Model.Credit;
using VirtualBank.Application.Customers.Authorization;
using VirtualBank.Application.Documents.Authorization;
using VirtualBank.Model.Documents;

namespace VirtualBank.Specifications.Tests;

/// <summary>
/// Resolves matrix resource references ("customer:C-1002") to descriptors whose attributes come
/// from the canonical seed fixtures, i.e. the same data the databases are seeded with.
/// </summary>
public sealed class FixtureResources
{
    private readonly Dictionary<string, CustomerResource> _customers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CreditApplicationResource> _applications = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DocumentResource> _documents = new(StringComparer.Ordinal);

    public static FixtureResources Load(string fixturesPath, IReadOnlyDictionary<string, string> subjectIds)
    {
        using var fixtures = JsonDocument.Parse(File.ReadAllText(fixturesPath));
        var root = fixtures.RootElement;
        var resources = new FixtureResources();

        string Subject(string username) => subjectIds.TryGetValue(username, out var id) ? id : username;

        var applications = root.GetProperty("creditApplications").EnumerateArray().ToList();
        foreach (var app in applications)
        {
            var analyst = app.GetProperty("assignedAnalyst");
            var application = CreditApplication.Rehydrate(
                id: CreditApplicationId.Parse(app.GetProperty("id").GetString()!),
                customerId: app.GetProperty("customerId").GetString()!,
                amount: Money.Of(app.GetProperty("amount").GetDecimal(), app.GetProperty("currency").GetString()!),
                status: Enum.Parse<CreditApplicationStatus>(app.GetProperty("status").GetString()!, ignoreCase: true),
                purpose: app.GetProperty("purpose").GetString()!,
                preparedBy: Subject(app.GetProperty("preparedBy").GetString()!),
                assignedAnalystId: analyst.ValueKind == JsonValueKind.String ? Subject(analyst.GetString()!) : null);
            resources._applications[application.Id.Value] = new CreditApplicationResource(application);
        }

        foreach (var customer in root.GetProperty("customers").EnumerateArray())
        {
            var id = customer.GetProperty("id").GetString()!;
            // Same derivation as the credit_assignments projection generated for customer_db.
            var assigned = resources._applications.Values
                .Select(r => r.Application)
                .Where(a => a.CustomerId == id && a.AssignedAnalystId is not null)
                .Select(a => a.AssignedAnalystId!)
                .ToHashSet(StringComparer.Ordinal);
            resources._customers[id] = new CustomerResource(id, customer.GetProperty("branch").GetString()!, assigned);
        }

        // Canonical documents/ACL fixtures: the same manifest consumed by the ingestion worker.
        using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryPaths.Root, "sample-docs", "manifest.json"))))
        {
            foreach (var doc in manifest.RootElement.EnumerateArray())
            {
                var entries = doc.GetProperty("acl").EnumerateArray().Select(entry =>
                    new DocumentAclEntry(entry.GetProperty("subjectKind").GetString()!,
                        entry.GetProperty("subjectValue").GetString()!, entry.GetProperty("effect").GetString()!)).ToArray();
                var branch = doc.GetProperty("branch");
                var resource = new DocumentResource(doc.GetProperty("id").GetString()!,
                    doc.GetProperty("classification").GetInt32(),
                    branch.ValueKind == JsonValueKind.Null ? null : branch.GetString(), entries);
                resources._documents.Add(resource.DocumentId, resource);
            }
        }
        return resources;
    }

    public ResourceDescriptor Resolve(string reference)
    {
        var separator = reference.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0)
        {
            throw new FormatException($"Invalid resource reference '{reference}'.");
        }

        var type = reference[..separator];
        var id = reference[(separator + 1)..];
        return type switch
        {
            CustomerResource.ResourceType => _customers.TryGetValue(id, out var c) ? c : throw Missing(reference),
            CreditApplicationResource.ResourceType => _applications.TryGetValue(id, out var a) ? a : throw Missing(reference),
            "document" => _documents.TryGetValue(id, out var document) ? document : new NamedResource(type, id),
            _ => new NamedResource(type, id),
        };
    }

    private static KeyNotFoundException Missing(string reference) =>
        new(string.Create(CultureInfo.InvariantCulture, $"Resource '{reference}' is not defined in infra/seed/fixtures.json."));
}
