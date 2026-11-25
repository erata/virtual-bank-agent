using System.Data.Common;
using VirtualBank.Infrastructure.Identity;
using VirtualBank.Infrastructure.Documents;
using VirtualBank.Infrastructure.Persistence;

namespace VirtualBank.Infrastructure;

/// <summary>
/// Readiness = every dependency needed to serve a request: both databases and verifiable Keycloak signing keys.
/// Returns the name of the first failing dependency (no details), or null when ready.
/// </summary>
public sealed class ReadinessProbe(CustomerDatabase customers, CreditDatabase credit, DocumentReaderDatabase documents,
    IHttpClientFactory httpClients, KeycloakOptions keycloak)
{
    public async Task<string?> FirstUnavailableAsync(CancellationToken cancellationToken)
    {
        if (!await CanQueryAsync(customers.DataSource, cancellationToken))
        {
            return "Customer database unavailable";
        }

        if (!await CanQueryAsync(credit.DataSource, cancellationToken))
        {
            return "Credit database unavailable";
        }

        if (!await CanQueryAsync(documents.DataSource, cancellationToken))
        {
            return "Document database unavailable";
        }

        var client = httpClients.CreateClient(JwtAuthentication.KeycloakMetadataClient);
        return await SigningKeyProbe.AvailableAsync(client, keycloak, cancellationToken) ? null : "Identity provider unavailable";
    }

    private static async Task<bool> CanQueryAsync(DbDataSource dataSource, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch (DbException)
        {
            return false;
        }
    }
}
