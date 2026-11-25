using VirtualBank.Application.Authorization.Resources;
using VirtualBank.Model.Credit;

namespace VirtualBank.Application.Credit.Authorization;

/// <summary>A credit application as seen by the authorization engine. Wraps the domain entity loaded under RLS.</summary>
public sealed record CreditApplicationResource(CreditApplication Application) : ResourceDescriptor(ResourceType, Application.Id.Value)
{
    public const string ResourceType = "credit.application";
}
