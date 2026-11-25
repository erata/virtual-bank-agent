using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Credit.Abstractions;
using VirtualBank.Application.Customers.Abstractions;

namespace VirtualBank.Infrastructure.Integration;

/// <summary>
/// Integration adapter between the two peer modules (lives outside both): answers the credit module's question
/// "which customer is the caller?" from the customer module's RLS-protected data. Only the row bound to the token
/// subject counts; a broken policy that returned foreign rows still yields no match.
/// </summary>
public sealed class CustomerApplicantDirectory(ICustomerRepository customers) : ICreditApplicantDirectory
{
    public async Task<string?> FindOwnCustomerIdAsync(PrincipalContext principal, CancellationToken cancellationToken)
    {
        if (!principal.IsActive || principal.Type == PrincipalType.Machine) return null;
        var rows = await customers.ListAsync(principal, cancellationToken);
        return rows.SingleOrDefault(c => string.Equals(c.OwnerSubjectId, principal.SubjectId, StringComparison.Ordinal))?.Id;
    }
}
