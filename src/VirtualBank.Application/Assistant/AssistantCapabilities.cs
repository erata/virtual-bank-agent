using VirtualBank.Application.Assistant.Tools;
using VirtualBank.Application.Authorization.Principals;

namespace VirtualBank.Application.Assistant;

/// <summary>Suggestions are built from actual visible tool capabilities, NEVER from an unverified browser role.</summary>
public sealed record AssistantSuggestion(string Label, string Prompt, string Tool);
public sealed record AssistantCapabilities(string Mode, string Introduction,
    IReadOnlyList<string> AvailableTools, IReadOnlyList<AssistantSuggestion> Suggestions);

public static class AssistantCapabilitiesBuilder
{
    public static AssistantCapabilities Build(PrincipalContext principal, AssistantToolCatalog catalog,
        AssistantLimits limits, string mode, string? locale)
    {
        var tr = locale?.StartsWith("tr", StringComparison.OrdinalIgnoreCase) == true;
        var de = locale?.StartsWith("de", StringComparison.OrdinalIgnoreCase) == true;
        // Suggestions must not advertise forbidden tools, including when the model operates in Explain mode.
        var available = catalog.VisibleDefinitions(principal, ToolVisibilityMode.Hide)
            .Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
        var suggestions = new List<AssistantSuggestion>();
        void Add(string tool, string trLabel, string deLabel, string enLabel, string trText, string deText, string enText)
        {
            if (available.Contains(tool)) suggestions.Add(new AssistantSuggestion(
                tr ? trLabel : de ? deLabel : enLabel,
                tr ? trText : de ? deText : enText, tool));
        }
        Add("get_my_profile", "IBAN'ım", "Meine IBAN", "My IBAN", "IBAN'ım nedir?", "Wie lautet meine IBAN?", "What is my IBAN?");
        Add("get_my_balance_summary", "Bakiyem", "Mein Kontostand", "My balances", "Hesaplarımda toplam ne kadar para var?", "Wie hoch ist mein Kontostand?", "What are my account balances?");
        Add("get_my_upcoming_payments", "Yaklaşan ödemeler", "Bevorstehende Zahlungen", "Upcoming payments", "Önümüzdeki 5 gün hangi ödemeleri yapmalıyım?", "Welche Zahlungen sind in den nächsten 5 Tagen fällig?", "What payments are due in the next 5 days?");
        Add("get_my_recent_transactions", "Son işlemler", "Letzte Buchungen", "Recent transactions", "Son 30 gündeki işlemlerimi listele", "Zeige meine Buchungen der letzten 30 Tage", "Show my last 30 days of transactions");
        Add("get_my_standing_orders", "Otomatik ödemeler", "Daueraufträge", "Standing orders", "Otomatik ödeme talimatlarımı göster", "Zeige meine Daueraufträge", "Show my standing orders");
        Add("list_customers", "Yetkili müşteriler", "Zugängliche Kunden", "Accessible customers", "Görmeye yetkili olduğum müşterileri listele", "Zeige alle für mich zugänglichen Kunden", "List the customers I can access");
        Add("summarize_customer_balances", "Müşteri bakiyeleri", "Kundenkontosalden", "Customer balances", "Görmeye yetkili olduğum müşterilerin hesap bakiyelerini özetle", "Fasse die Salden meiner zugänglichen Kunden zusammen", "Summarize balances for customers I can access");
        Add("list_credit_applications", "Kredi başvuruları", "Kreditanträge", "Credit applications", "Yetkili olduğum kredi başvurularını listele", "Zeige meine zugänglichen Kreditanträge", "Show credit applications I can access");
        if (available.Contains("get_my_profile"))
            Add("search_documents", "Ücretler ve SSS", "Gebühren und FAQ", "Fees and FAQs",
                "Güncel 2026 banka ücretleri ve SEPA rehberini göster",
                "Welche aktuellen Gebühren und SEPA-Regeln gelten 2026?",
                "Find the public 2026 fees and SEPA transfer FAQ");
        else
        {
            // A listing request has no semantic content to search for: it needs list_documents, not RAG.
            Add("list_documents", "Erişilebilir belgeler", "Zugängliche Dokumente", "Accessible documents",
                "Okuyabildiğim belgeleri listele", "Welche Dokumente darf ich lesen?",
                "List the documents I am allowed to read");
            Add("search_documents", "Belgelerde ara", "In Dokumenten suchen", "Search documents",
                "SEPA havalesinde IBAN kontrolü nasıl yapılır?",
                "Wie wird die IBAN bei einer SEPA-Überweisung geprüft?",
                "How is the IBAN checked for a SEPA transfer?");
        }
        // Write requests: the assistant prepares a confirmation card; the user confirms it below the answer.
        Add("plan_transfer", "Havale talimatı", "Überweisung", "Transfer", "DE89 3704 0044 0532 0130 00 IBAN'ına Hausverwaltung Müller adına 120 EUR kira havalesi hazırla",
            "Bereite eine Überweisung über 120 EUR Miete an Hausverwaltung Müller, IBAN DE89 3704 0044 0532 0130 00 vor",
            "Prepare a 120 EUR rent transfer to Hausverwaltung Müller, IBAN DE89 3704 0044 0532 0130 00");
        Add("transfer_between_my_accounts", "Hesaplarım arası", "Umbuchung", "Between my accounts", "A-20011 hesabımdan A-20012 hesabıma 250 EUR aktar",
            "Buche 250 EUR von A-20011 auf A-20012 um", "Move 250 EUR from A-20011 to A-20012");
        Add("create_standing_order", "Yeni otomatik ödeme", "Neuer Dauerauftrag", "New standing order",
            "Her ay 50 EUR, DE89 3704 0044 0532 0130 00 IBAN'lı Sportverein Süd'e otomatik ödeme talimatı oluştur",
            "Lege einen monatlichen Dauerauftrag über 50 EUR an Sportverein Süd, IBAN DE89 3704 0044 0532 0130 00 an",
            "Create a monthly standing order of 50 EUR to Sportverein Süd, IBAN DE89 3704 0044 0532 0130 00");
        Add("request_credit", "Kredi başvurusu", "Kreditantrag", "Apply for credit", "60 ay vadeli 15.000 EUR araç kredisi başvurusu yapmak istiyorum",
            "Ich möchte einen Autokredit über 15.000 EUR mit 60 Monaten Laufzeit beantragen", "I want to apply for a 15,000 EUR car loan over 60 months");
        Add("cash_deposit", "Nakit yatırma", "Bareinzahlung", "Cash deposit", "C-1002 müşterisinin A-10021 hesabına 500 EUR nakit yatır",
            "Zahle 500 EUR bar auf Konto A-10021 von Kunde C-1002 ein", "Deposit 500 EUR cash to account A-10021 of customer C-1002");
        Add("change_standing_order", "Otomatik ödemeyi durdur", "Dauerauftrag pausieren", "Pause a standing order",
            "Otomatik ödeme talimatlarımı göster; Sparplan talimatını durdurmak istiyorum", "Zeige meine Daueraufträge; ich möchte den Sparplan pausieren",
            "Show my standing orders; I want to pause the savings plan");
        Add("approve_credit_application", "Kredi onayı", "Kredit genehmigen", "Approve a credit", "K-102 başvurusunu onaylamak için hazırla",
            "Bereite die Genehmigung von K-102 vor", "Prepare the approval of K-102");
        Add("reject_credit_application", "Kredi reddi", "Kredit ablehnen", "Reject a credit", "K-101 başvurusunu reddet, gerekçe: gelir belgelenemedi",
            "Lehne K-101 ab, Begründung: Einkommen nicht belegt", "Reject K-101, reason: income could not be verified");
        Add("add_credit_note", "Not ekle", "Notiz hinzufügen", "Add a note", "K-100 başvurusuna not ekle: Gelir son üç bordroyla doğrulandı",
            "Füge K-100 eine Notiz hinzu: Einkommen mit drei Gehaltsabrechnungen geprüft", "Add a note to K-100: income verified with three payslips");
        Add("describe_my_access", "Yetkilerim", "Meine Berechtigungen", "My permissions", "Bu asistanla hangi işlemleri yapabilirim?", "Welche Berechtigungen habe ich hier?", "What am I allowed to do in this assistant?");
        // Security: do not suggest unsupported services or a customer record to a platform administrator.
        var intro = available.Contains("get_my_profile")
            ? (tr ? "Kendi banka hesaplarınız ve kayıtlı ödemeleriniz hakkında yardımcı olabilirim." : de ? "Ich kann Ihre eigenen Bankkonten und Zahlungen anzeigen." : "I can help with your own accounts and recorded payments.")
            : available.Contains("list_customers")
                ? (tr ? "Yalnızca erişim yetkiniz bulunan müşterileri inceleyebilirim." : de ? "Ich zeige nur für Sie freigegebene Kundendaten." : "I can look up only customers you are authorized to access.")
                : available.Contains("list_credit_applications")
                    ? (tr ? "Yetkili kredi başvurularını inceleyebilirim." : de ? "Ich kann für Sie sichtbare Kreditanträge prüfen." : "I can review credit applications available to you.")
                    : (tr ? "Yetkilerinizi açıklayabilirim; yönetici rolü müşteri verilerine otomatik erişim sağlamaz." : de ? "Ich kann Ihre Berechtigungen erklären; Administratoren erhalten keinen automatischen Kundenzugriff." : "I can explain your permissions; admin rights do not grant access to customer data.");
        return new AssistantCapabilities(mode, intro,
            available.OrderBy(x => x, StringComparer.Ordinal).ToArray(), suggestions.Take(12).ToArray());
    }
}
