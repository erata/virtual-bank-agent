using VirtualBank.Application.Authorization.Decisions;
using VirtualBank.Application.Authorization.Principals;

namespace VirtualBank.Application.Authorization.Auditing;

/// <summary>
/// Port for recording authorization decisions. Implementations must not record tokens or business content.
/// Defined in the core so that application layers can audit without depending on a logging framework.
/// </summary>
public interface IAuthorizationAudit
{
    void Record(PrincipalContext principal, AuthorizationDecision decision);

    /// <summary>The application policy denied a row that RLS returned: a configuration inconsistency.</summary>
    void RecordPolicyRlsMismatch(PrincipalContext principal, AuthorizationDecision decision);
}
