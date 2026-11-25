using System.Text;
using System.Text.RegularExpressions;
using VirtualBank.Application.Assistant.Tools;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;

namespace VirtualBank.Application.Assistant;

/// <summary>
/// Deterministic answer to "what can I do with this assistant?". It is built ONLY from the tools the caller's
/// permissions make visible (the same <see cref="AssistantToolCatalog.VisibleDefinitions"/> filter the model gets),
/// so it can neither over-promise nor reveal capabilities of other roles, and it needs no model round-trip
/// (on CPU-only local inference that round-trip took minutes for this static question).
/// </summary>
public static class AssistantCapabilityAnswer
{
    public const string Mode = "catalog";

    private static readonly Regex RecordIdentifier = new(@"\b(?:[CK]-[0-9]{3,4}|DOC-[0-9]{3})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(50));

    private static readonly Regex CapabilityQuestion = new(
        @"(neler yapabil|ne yapabil|hangi islem|hangi islemleri|yapabileceg|nasil yardim" +
        @"|was kann ich|was kannst du|welche funktion|wobei kannst du|wie kannst du mir helfen" +
        @"|what can i do|what can you do|what can this assistant|how can you help|what are your capabilities|what do you support)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(50));

    /// <summary>
    /// A pure capability question: no record identifier and no concrete customer/credit/balance/document intent.
    /// Anything more specific goes to the runtime as before.
    /// </summary>
    public static bool IsCapabilityQuestion(string message)
    {
        if (message.Length > 200 || RecordIdentifier.IsMatch(message)) return false;
        var text = NaturalLanguageIntent.Normalize(message);
        return CapabilityQuestion.IsMatch(text) &&
               !NaturalLanguageIntent.IsCustomerRequest(text) &&
               !NaturalLanguageIntent.IsCreditRequest(text) &&
               !NaturalLanguageIntent.IsBalanceRequest(text) &&
               !NaturalLanguageIntent.IsDocumentRequest(text);
    }

    public static string Describe(AssistantToolCatalog catalog, PrincipalContext principal, AssistantLanguage language)
    {
        // Always the permission-filtered (Hide) view, independent of the configured visibility mode.
        var tools = catalog.VisibleDefinitions(principal, ToolVisibilityMode.Hide);
        var answer = new StringBuilder(language switch
        {
            AssistantLanguage.Turkish => "Mevcut yetkilerinizle bu asistanda şunları yapabilirsiniz:",
            AssistantLanguage.German => "Mit Ihren aktuellen Berechtigungen kann der Assistent Folgendes:",
            _ => "With your current permissions this assistant can:",
        });
        if (tools.Count == 0)
        {
            answer.Clear().Append(language switch
            {
                AssistantLanguage.Turkish => "Mevcut yetkilerinizle kullanılabilir bir asistan aracı yok.",
                AssistantLanguage.German => "Mit Ihren aktuellen Berechtigungen ist kein Assistenten-Tool verfügbar.",
                _ => "No assistant tools are available with your current permissions.",
            });
        }
        foreach (var tool in tools)
            answer.Append("\n• ").Append(Label(tool, language));

        if (principal.HasPermission(KnownPermissions.CreditNoteWrite) || principal.HasPermission(KnownPermissions.CreditApprove) ||
            principal.HasPermission(KnownPermissions.CustomerPaymentWrite) || principal.HasPermission(KnownPermissions.AccountCash) ||
            principal.HasPermission(KnownPermissions.CreditRequest))
            answer.Append("\n\n").Append(language switch
            {
                AssistantLanguage.Turkish => "Değişiklikleri asistan kendisi yapmaz: bir onay kartı hazırlar; kartı inceleyip onayladığınızda yürütülür (ayrı bir insan onayı gerektirir).",
                AssistantLanguage.German => "Änderungen führt der Assistent nie selbst aus: Er bereitet eine Bestätigungskarte vor, die Sie prüfen und bestätigen (separate menschliche Bestätigung).",
                _ => "The assistant never makes changes itself: it prepares a confirmation card that you review and confirm (separate human confirmation).",
            });
        return answer.ToString();
    }

    private static string Label(ToolDefinition tool, AssistantLanguage language) => (tool.Name, language) switch
    {
        ("list_customers", AssistantLanguage.Turkish) => "Erişebildiğiniz müşterileri listeleme",
        ("list_customers", AssistantLanguage.German) => "Für Sie sichtbare Kunden auflisten",
        ("list_customers", _) => "List the customers you are allowed to see",
        ("get_customer", AssistantLanguage.Turkish) => "Tek bir müşterinin bilgilerini gösterme (hassas alanlar yetkiye göre maskelenir)",
        ("get_customer", AssistantLanguage.German) => "Einen Kunden anzeigen (sensible Felder je nach Berechtigung maskiert)",
        ("get_customer", _) => "Show one customer (sensitive fields masked unless permitted)",
        ("list_customer_accounts", AssistantLanguage.Turkish) => "Bir müşterinin hesaplarını ve bakiyelerini gösterme",
        ("list_customer_accounts", AssistantLanguage.German) => "Konten und Salden eines Kunden anzeigen",
        ("list_customer_accounts", _) => "Show one customer's accounts and balances",
        ("summarize_customer_balances", AssistantLanguage.Turkish) => "Erişebildiğiniz müşterilerin bakiyelerini para birimine göre özetleme",
        ("summarize_customer_balances", AssistantLanguage.German) => "Salden zugänglicher Kunden je Währung zusammenfassen",
        ("summarize_customer_balances", _) => "Summarize balances of accessible customers by currency",
        ("list_credit_applications", AssistantLanguage.Turkish) => "Görebildiğiniz kredi başvurularını listeleme",
        ("list_credit_applications", AssistantLanguage.German) => "Für Sie sichtbare Kreditanträge auflisten",
        ("list_credit_applications", _) => "List the credit applications you may see",
        ("get_credit_application", AssistantLanguage.Turkish) => "Tek bir kredi başvurusunu gösterme",
        ("get_credit_application", AssistantLanguage.German) => "Einen Kreditantrag anzeigen",
        ("get_credit_application", _) => "Show one credit application",
        ("list_documents", AssistantLanguage.Turkish) => "Okuyabildiğiniz belgeleri listeleme",
        ("list_documents", AssistantLanguage.German) => "Für Sie lesbare Dokumente auflisten",
        ("list_documents", _) => "List the documents you can read",
        ("search_documents", AssistantLanguage.Turkish) => "Erişiminize açık belgelerde (politika, ücretler, SEPA vb.) kaynaklı arama",
        ("search_documents", AssistantLanguage.German) => "Zugängliche Dokumente (Richtlinien, Gebühren, SEPA …) mit Quellen durchsuchen",
        ("search_documents", _) => "Search documents you can access (policies, fees, SEPA …) with citations",
        ("get_document_excerpt", AssistantLanguage.Turkish) => "Belirli bir belgeden (ör. DOC-101) alıntı gösterme",
        ("get_document_excerpt", AssistantLanguage.German) => "Auszüge aus einem bestimmten Dokument (z. B. DOC-101) anzeigen",
        ("get_document_excerpt", _) => "Show excerpts of a specific document (e.g. DOC-101)",
        ("describe_my_access", AssistantLanguage.Turkish) => "Yetkilerinizi ve erişim kapsamınızı açıklama",
        ("describe_my_access", AssistantLanguage.German) => "Ihre Berechtigungen und Ihren Zugriffsumfang erklären",
        ("describe_my_access", _) => "Explain your permissions and access scope",
        ("add_credit_note", AssistantLanguage.Turkish) => "Kredi başvurusuna not önerme (siz onaylayınca kaydedilir)",
        ("add_credit_note", AssistantLanguage.German) => "Notiz zu einem Kreditantrag vorschlagen (gespeichert erst nach Ihrer Bestätigung)",
        ("add_credit_note", _) => "Propose a note on a credit application (saved only after you confirm)",
        ("approve_credit_application", AssistantLanguage.Turkish) => "Kredi onayını hazırlama (limit ve dört göz kontrolüyle; siz onaylarsınız)",
        ("approve_credit_application", AssistantLanguage.German) => "Kreditgenehmigung vorbereiten (Limit- und Vier-Augen-Prüfung; Sie bestätigen)",
        ("approve_credit_application", _) => "Prepare a credit approval (limit and four-eyes checks; you confirm)",
        ("reject_credit_application", AssistantLanguage.Turkish) => "Gerekçeli kredi reddi hazırlama (siz onaylarsınız)",
        ("reject_credit_application", AssistantLanguage.German) => "Begründete Kreditablehnung vorbereiten (Sie bestätigen)",
        ("reject_credit_application", _) => "Prepare a credit rejection with a reason (you confirm)",
        ("plan_transfer", AssistantLanguage.Turkish) => "Kendi hesabınızdan havale talimatı hazırlama (siz onaylarsınız)",
        ("plan_transfer", AssistantLanguage.German) => "Überweisung von Ihrem Konto vorbereiten (Sie bestätigen)",
        ("plan_transfer", _) => "Prepare a transfer from your own account (you confirm)",
        ("cancel_planned_payment", AssistantLanguage.Turkish) => "Planlanmış bir ödemenizi iptal etme (siz onaylarsınız)",
        ("cancel_planned_payment", AssistantLanguage.German) => "Eine geplante Zahlung stornieren (Sie bestätigen)",
        ("cancel_planned_payment", _) => "Cancel one of your planned payments (you confirm)",
        ("change_standing_order", AssistantLanguage.Turkish) => "Otomatik ödeme talimatını durdurma/sürdürme (siz onaylarsınız)",
        ("change_standing_order", AssistantLanguage.German) => "Dauerauftrag pausieren/fortsetzen (Sie bestätigen)",
        ("change_standing_order", _) => "Pause or resume a standing order (you confirm)",
        ("transfer_between_my_accounts", AssistantLanguage.Turkish) => "Kendi hesaplarınız arasında para aktarma (siz onaylarsınız)",
        ("transfer_between_my_accounts", AssistantLanguage.German) => "Umbuchung zwischen Ihren eigenen Konten (Sie bestätigen)",
        ("transfer_between_my_accounts", _) => "Move money between your own accounts (you confirm)",
        ("create_standing_order", AssistantLanguage.Turkish) => "Yeni otomatik ödeme talimatı oluşturma (siz onaylarsınız)",
        ("create_standing_order", AssistantLanguage.German) => "Neuen Dauerauftrag anlegen (Sie bestätigen)",
        ("create_standing_order", _) => "Create a new standing order (you confirm)",
        ("request_credit", AssistantLanguage.Turkish) => "Kredi başvurusu yapma (onaylayıcı karar verir)",
        ("request_credit", AssistantLanguage.German) => "Kredit beantragen (ein Genehmiger entscheidet)",
        ("request_credit", _) => "Apply for a credit (an approver decides)",
        ("cash_deposit", AssistantLanguage.Turkish) => "Şube müşterisinin hesabına nakit yatırma (siz onaylarsınız)",
        ("cash_deposit", AssistantLanguage.German) => "Bareinzahlung für Kunden Ihrer Filiale (Sie bestätigen)",
        ("cash_deposit", _) => "Cash deposit for a customer of your branch (you confirm)",
        ("cash_withdrawal", AssistantLanguage.Turkish) => "Şube müşterisinin hesabından nakit çekme (siz onaylarsınız)",
        ("cash_withdrawal", AssistantLanguage.German) => "Barauszahlung für Kunden Ihrer Filiale (Sie bestätigen)",
        ("cash_withdrawal", _) => "Cash withdrawal for a customer of your branch (you confirm)",
        ("get_my_profile", AssistantLanguage.Turkish) => "Kendi profilinizi (IBAN dahil) gösterme",
        ("get_my_profile", AssistantLanguage.German) => "Ihr eigenes Profil (inkl. IBAN) anzeigen",
        ("get_my_profile", _) => "Show your own profile (including IBAN)",
        ("get_my_accounts", AssistantLanguage.Turkish) => "Kendi hesaplarınızı ve bakiyelerinizi gösterme",
        ("get_my_accounts", AssistantLanguage.German) => "Ihre eigenen Konten und Salden anzeigen",
        ("get_my_accounts", _) => "Show your own accounts and balances",
        ("get_my_balance_summary", AssistantLanguage.Turkish) => "Bakiyenizi para birimine göre özetleme",
        ("get_my_balance_summary", AssistantLanguage.German) => "Ihren Kontostand je Währung zusammenfassen",
        ("get_my_balance_summary", _) => "Summarize your balance by currency",
        ("get_my_upcoming_payments", AssistantLanguage.Turkish) => "Yaklaşan planlı ödemelerinizi gösterme",
        ("get_my_upcoming_payments", AssistantLanguage.German) => "Ihre anstehenden geplanten Zahlungen anzeigen",
        ("get_my_upcoming_payments", _) => "Show your upcoming scheduled payments",
        ("get_my_recent_transactions", AssistantLanguage.Turkish) => "Son hesap hareketlerinizi gösterme",
        ("get_my_recent_transactions", AssistantLanguage.German) => "Ihre letzten Buchungen anzeigen",
        ("get_my_recent_transactions", _) => "Show your recent transactions",
        ("get_my_standing_orders", AssistantLanguage.Turkish) => "Otomatik ödeme talimatlarınızı gösterme",
        ("get_my_standing_orders", AssistantLanguage.German) => "Ihre Daueraufträge anzeigen",
        ("get_my_standing_orders", _) => "Show your standing orders",
        // A newly added tool still appears (with its English catalog description) instead of being silently hidden.
        _ => tool.Description,
    };
}
