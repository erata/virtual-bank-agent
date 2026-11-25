using VirtualBank.Infrastructure.Integration;
using VirtualBank.Application.Common.Actions;
using VirtualBank.Application.Credit.Commands.ApproveCreditApplication;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VirtualBank.Application.Assistant;
using VirtualBank.Application.Assistant.Abstractions;
using VirtualBank.Application.Authorization.Auditing;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Credit.Abstractions;
using VirtualBank.Application.Customers.Abstractions;
using VirtualBank.Infrastructure.Assistant;
using VirtualBank.Infrastructure.Assistant.Offline;
using VirtualBank.Infrastructure.Assistant.SemanticKernel;
using VirtualBank.Infrastructure.Auditing;
using VirtualBank.Infrastructure.Identity;
using VirtualBank.Infrastructure.Persistence;
using VirtualBank.Infrastructure.Persistence.Credit;
using VirtualBank.Infrastructure.Persistence.Customers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pgvector.Npgsql;
using VirtualBank.Application.Documents;
using VirtualBank.Application.Documents.Commands.IngestDocuments;
using VirtualBank.Infrastructure.Documents;

namespace VirtualBank.Infrastructure;

/// <summary>Registers the adapters for the Application ports. Called from the composition root (Web).</summary>
public static class DependencyInjection
{
    public const string DefaultAudience = "virtual-bank-api";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var catalogPath = Required(configuration, "Authorization:CatalogPath");
        var customerDb = configuration.GetConnectionString("CustomerDb")
            ?? throw new InvalidOperationException("Connection string 'CustomerDb' is required.");
        var creditDb = configuration.GetConnectionString("CreditDb")
            ?? throw new InvalidOperationException("Connection string 'CreditDb' is required.");

        // Identity: JWT validation for this API's audience, claims -> trusted principal.
        services.AddKeycloakJwtAuthentication(configuration, configuration["Keycloak:Audience"] ?? DefaultAudience);
        services.AddSingleton(PermissionCatalog.FromFile(catalogPath));
        services.AddSingleton(new MachineIdentityOptions(
            configuration.GetSection("Authorization:MachineClients").GetChildren().Select(c => c.Value).OfType<string>()));
        services.AddSingleton<IPrincipalResolver, PrincipalResolver>();

        // Persistence: one pool per database, each with its own least-privilege login. Factories so the container
        // disposes the pools on shutdown.
        services.AddSingleton(_ => new CustomerDatabase(NpgsqlDataSource.Create(customerDb)));
        services.AddSingleton(_ => new CreditDatabase(NpgsqlDataSource.Create(creditDb)));
        services.AddSingleton<ICustomerRepository, PostgresCustomerRepository>();
        services.AddSingleton<ISelfServicePaymentRepository, PostgresSelfServicePaymentRepository>();
        services.AddSingleton<IAccountBookingRepository, PostgresAccountBookingRepository>();
        services.AddSingleton<ICreditApplicantDirectory, CustomerApplicantDirectory>();
        services.AddSingleton<ICreditApplicationRepository, PostgresCreditApplicationRepository>();
        // M6: approvals execute under the separate credit_approval login. Missing configuration = approvals refused.
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPendingActionStore, InMemoryPendingActionStore>();
        if (configuration.GetConnectionString("CreditApprovalDb") is { Length: > 0 } creditApprovalDb)
        {
            services.AddSingleton(_ => new CreditApprovalDatabase(NpgsqlDataSource.Create(creditApprovalDb)));
            services.AddSingleton<ICreditApprovalExecutor, PostgresCreditApprovalExecutor>();
        }
        else
        {
            services.AddSingleton<ICreditApprovalExecutor, DisabledCreditApprovalExecutor>();
        }

        services.AddSingleton<IAuthorizationAudit, LoggerAuthorizationAudit>();
        services.AddSingleton<ReadinessProbe>();
        // Document store: separate least-privilege credentials for reading, ingestion and ACL metadata.
        var documents = configuration.GetConnectionString("DocumentsDb")
            ?? throw new InvalidOperationException("ConnectionStrings:DocumentsDb is required.");
        var documentWriter = configuration.GetConnectionString("DocumentWriterDb")
            ?? throw new InvalidOperationException("ConnectionStrings:DocumentWriterDb is required.");
        var documentAdmin = configuration.GetConnectionString("DocumentAclAdminDb")
            ?? throw new InvalidOperationException("ConnectionStrings:DocumentAclAdminDb is required.");
        static NpgsqlDataSource VectorSource(string connectionString)
        {
            var builder = new NpgsqlDataSourceBuilder(connectionString);
            builder.UseVector();
            return builder.Build();
        }
        services.AddSingleton(_ => new DocumentReaderDatabase(VectorSource(documents)));
        services.AddSingleton(_ => new DocumentWriterDatabase(VectorSource(documentWriter)));
        services.AddSingleton(_ => new DocumentAclAdminDatabase(NpgsqlDataSource.Create(documentAdmin)));
        services.AddSingleton<IDocumentSearchRepository, PostgresDocumentSearchRepository>();
        services.AddSingleton<IDocumentExcerptRepository, PostgresDocumentExcerptRepository>();
        services.AddSingleton<IDocumentCatalogRepository, PostgresDocumentCatalogRepository>();
        services.AddSingleton<IDocumentIngestionRepository, PostgresDocumentIngestionRepository>();
        services.AddSingleton<IDocumentAclAdministrationRepository, PostgresDocumentAclAdministrationRepository>();
        var ragOptions = configuration.GetSection("Documents").Get<DocumentRagOptions>() ?? new DocumentRagOptions();
        ragOptions = ragOptions with { ApiKey = ragOptions.Provider switch
        {
            DocumentEmbeddingProvider.OpenRouter => configuration["OPENROUTER_API_KEY"],
            DocumentEmbeddingProvider.OpenAI => configuration["OPENAI_API_KEY"],
            _ => null
        } };
        ragOptions.Validate();
        services.AddSingleton(ragOptions);
        services.AddSingleton(new DocumentSearchQualityOptions(ragOptions.MinimumCosineSimilarity));
        if (ragOptions.Provider == DocumentEmbeddingProvider.Ollama)
        {
            services.AddHttpClient<IEmbeddingProvider, OllamaEmbeddingProvider>(http =>
            {
                http.BaseAddress = new Uri(ragOptions.Endpoint);
                http.Timeout = TimeSpan.FromMinutes(3);
            });
        }
        else
        {
            services.AddHttpClient<IEmbeddingProvider, ApiEmbeddingProvider>(http =>
            {
                http.BaseAddress = new Uri(ragOptions.Endpoint.TrimEnd('/') + "/");
                http.Timeout = TimeSpan.FromMinutes(3);
                http.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ragOptions.ApiKey);
            });
        }
        services.AddSingleton<IDocumentSourceReader, FileSystemDocumentSource>();
        services.AddSingleton<IDocumentFileStore, FileSystemDocumentFileStore>();
        services.AddScoped<ISecureDocumentSearch, SecureDocumentSearch>();
        services.AddScoped<SecureDocumentExcerpt>();
        services.AddScoped<SecureDocumentCatalog>();
        services.AddScoped<IngestDocumentsHandler>();
        services.AddScoped<DocumentAclAdministration>();
        services.AddSingleton<IConversationStore, InMemoryConversationStore>();
        services.AddAssistantRuntime(configuration);
        return services;
    }

    /// <summary>
    /// Assistant runtime: Semantic Kernel + local Ollama by default; cloud OpenRouter/OpenAI are opt-in.
    /// Misconfiguration (e.g. OpenRouter without a key) fails at startup rather than at the first request.
    /// </summary>
    public static IServiceCollection AddAssistantRuntime(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(AssistantOptions.Section).Get<AssistantOptions>() ?? new AssistantOptions();
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            options = options with { ApiKey = options.Provider == AssistantProvider.OpenAI
                ? configuration["OPENAI_API_KEY"] : configuration["OPENROUTER_API_KEY"] };
        }

        // Provider-specific defaults: an OpenRouter switch must not inherit the local Ollama URL/model.
        if (options.Provider == AssistantProvider.OpenRouter &&
            string.IsNullOrWhiteSpace(configuration["Assistant:Endpoint"]))
        {
            options = options with
            {
                Endpoint = new Uri("https://openrouter.ai/api/v1"),
                Model = string.IsNullOrWhiteSpace(configuration["Assistant:Model"])
                    ? "openai/gpt-4o" : options.Model,
            };
        }
        if (options.Provider == AssistantProvider.OpenAI &&
            string.IsNullOrWhiteSpace(configuration["Assistant:Endpoint"]))
        {
            options = options with {
                Endpoint = new Uri("https://api.openai.com/v1"),
                Model = string.IsNullOrWhiteSpace(configuration["Assistant:Model"]) ? "gpt-4o" : options.Model
            };
        }
        options.Validate();
        services.AddSingleton(options);
        services.AddSingleton(options.Limits);
        services.AddSingleton<IAssistantDiagnostics, LoggerAssistantDiagnostics>();

        if (options.Provider is AssistantProvider.OpenRouter or AssistantProvider.OpenAI or AssistantProvider.Ollama)
        {
            services.AddSemanticKernelRuntime(options);
            services.AddSingleton<IAssistantRuntime, SemanticKernelAssistantRuntime>();
        }
        else
        {
            services.AddSingleton<IAssistantRuntime, OfflineAssistantRuntime>();
        }

        return services;
    }

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] ?? throw new InvalidOperationException($"Configuration value '{key}' is required.");
}
