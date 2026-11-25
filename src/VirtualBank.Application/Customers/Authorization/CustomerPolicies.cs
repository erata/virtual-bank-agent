using VirtualBank.Application.Authorization.Decisions;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Policies;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Authorization.Resources;
using VirtualBank.Model.Customers;

namespace VirtualBank.Application.Customers.Authorization;

/// <summary>Customer as seen by the authorization policies. Attribute values come from the customer database.</summary>
public sealed record CustomerResource(string Id, string Branch, IReadOnlySet<string> AssignedAnalystIds, string? OwnerSubjectId = null)
    : ResourceDescriptor(ResourceType, Id)
{
    public const string ResourceType = "customer";

    public static CustomerResource From(Customer customer) => new(customer.Id, customer.Branch, customer.AssignedAnalystIds, customer.OwnerSubjectId);
}

/// <summary>The customer module's policies. Mirrored by the Customer RLS policies in customer_db.</summary>
public static class CustomerPolicies
{
    public static IReadOnlyList<IResourcePolicy> Create() => [new CustomerReadPolicy(), new CustomerSensitiveReadPolicy()];
}

/// <summary>
/// Reading a customer has two distinct paths: branch advisors (customer.read + same branch) and
/// credit analysts (customer.read.assigned + customer linked to an application assigned to them).
/// </summary>
public sealed class CustomerReadPolicy : IResourcePolicy
{
    public string PolicyId => "customer.read.v1";

    public string ResourceType => CustomerResource.ResourceType;

    public Permission Action => KnownPermissions.CustomerRead;

    // Customers of other branches are hidden entirely (404), not merely forbidden.
    public DisclosureMode Disclosure => DisclosureMode.None;

    public PolicyResult Evaluate(PrincipalContext principal, ResourceDescriptor resource) =>
        resource is CustomerResource customer ? Evaluate(principal, customer) : PolicyResult.Deny(ReasonCodes.ResourceTypeMismatch);

    internal static PolicyResult Evaluate(PrincipalContext principal, CustomerResource customer)
    {
        // Customer self-service uses the verified Keycloak subject, NEVER a request-supplied customer ID.
        if (principal.HasPermission(KnownPermissions.CustomerReadSelf) &&
            customer.OwnerSubjectId is not null &&
            string.Equals(principal.SubjectId, customer.OwnerSubjectId, StringComparison.Ordinal))
            return PolicyResult.Allow();

        var branchPath = principal.HasPermission(KnownPermissions.CustomerRead);
        var assignmentPath = principal.HasPermission(KnownPermissions.CustomerReadAssigned);

        if (branchPath && principal.Branch is not null &&
            string.Equals(principal.Branch, customer.Branch, StringComparison.Ordinal))
        {
            return PolicyResult.Allow();
        }

        if (assignmentPath && customer.AssignedAnalystIds.Contains(principal.SubjectId))
        {
            return PolicyResult.Allow();
        }

        if (assignmentPath)
        {
            return PolicyResult.Deny(ReasonCodes.ResourceNotAssigned);
        }

        return branchPath ? PolicyResult.Deny(ReasonCodes.BranchMismatch) : PolicyResult.Deny(ReasonCodes.MissingPermission);
    }
}

/// <summary>Unmasked sensitive fields require the extra permission AND ordinary read access to the customer.</summary>
public sealed class CustomerSensitiveReadPolicy : IResourcePolicy
{
    public string PolicyId => "customer.read-sensitive.v1";

    public string ResourceType => CustomerResource.ResourceType;

    public Permission Action => KnownPermissions.CustomerReadSensitive;

    public DisclosureMode Disclosure => DisclosureMode.Generic;

    public PolicyResult Evaluate(PrincipalContext principal, ResourceDescriptor resource)
    {
        if (resource is not CustomerResource customer)
        {
            return PolicyResult.Deny(ReasonCodes.ResourceTypeMismatch);
        }

        var read = CustomerReadPolicy.Evaluate(principal, customer);
        if (!read.IsAllowed)
        {
            return read;
        }

        return principal.HasPermission(KnownPermissions.CustomerReadSensitive)
            ? PolicyResult.Allow()
            : PolicyResult.Deny(ReasonCodes.MissingPermission);
    }
}
