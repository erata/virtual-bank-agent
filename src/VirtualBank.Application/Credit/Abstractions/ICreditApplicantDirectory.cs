using VirtualBank.Application.Authorization.Principals;

namespace VirtualBank.Application.Credit.Abstractions;

/// <summary>
/// The credit module's view of "which bank customer is the caller". Implemented outside both modules (integration
/// adapter) because Credit must not reference the Customers module; the answer comes from the authoritative
/// customer row bound to the token subject, never from user input.
/// </summary>
public interface ICreditApplicantDirectory
{
    Task<string?> FindOwnCustomerIdAsync(PrincipalContext principal, CancellationToken cancellationToken);
}
