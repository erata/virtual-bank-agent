using VirtualBank.Presentation.Actions;
using VirtualBank.Presentation.Access;
using VirtualBank.Presentation.Assistant;
using VirtualBank.Presentation.Credit;
using VirtualBank.Presentation.Customers;
using VirtualBank.Presentation.Documents;
using Microsoft.AspNetCore.Routing;

namespace VirtualBank.Presentation;

/// <summary>The complete HTTP surface of the business modules.</summary>
public static class EndpointMapping
{
    public static IEndpointRouteBuilder MapVirtualBankApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapCustomerEndpoints();
        endpoints.MapSelfServiceEndpoints();
        endpoints.MapCreditEndpoints();
        endpoints.MapAssistantEndpoints();
        endpoints.MapDocumentEndpoints();
        endpoints.MapAccessEndpoints();
        endpoints.MapActionEndpoints();
        return endpoints;
    }
}
