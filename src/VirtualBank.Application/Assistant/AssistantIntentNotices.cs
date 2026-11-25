using System.Text.RegularExpressions;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;

namespace VirtualBank.Application.Assistant;

/// <summary>
/// Generic capability hint for an explicit credit-data request when Hide mode removes the tool from the model.
/// It must not disclose record existence. A request solely for document policy is NOT a credit-record request.
/// </summary>
public static class AssistantIntentNotices
{
    private static readonly Regex ApplicationIdentifier = new(@"\bK-[0-9]{3}\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(50));

    public static bool NeedsCreditReadHint(string question, PrincipalContext principal)
    {
        if (principal.HasPermission(KnownPermissions.CreditRead) || principal.HasPermission(KnownPermissions.CreditApprove))
            return false;
        var text = NaturalLanguageIntent.Normalize(question);
        // Policy questions use document.read; do not claim the user requested an account/credit record.
        if (NaturalLanguageIntent.IsDocumentRequest(text) && !ApplicationIdentifier.IsMatch(question)) return false;
        return ApplicationIdentifier.IsMatch(question) || NaturalLanguageIntent.IsCreditRequest(text);
    }
}
