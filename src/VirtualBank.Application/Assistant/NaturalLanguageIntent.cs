using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace VirtualBank.Application.Assistant;

public enum AssistantLanguage { English, German, Turkish }

/// <summary>
/// Conservative intent hints for a deterministic OFFLINE demo, NOT semantic/vector retrieval and NOT authorization.
/// Unicode normalization handles Turkish keyboard variants (ş/s, ı/i, ü/u) and German umlauts.
/// Only read-intent tokens are normalized; destructive commands still pass the same policy gate.
/// </summary>
public static class NaturalLanguageIntent
{
    /// <summary>
    /// Lower-cases and folds TR/DE (and common Latin) diacritics to ASCII.
    /// The fold is an explicit table on purpose: the projects build with <c>InvariantGlobalization=true</c>,
    /// where <see cref="string.Normalize(NormalizationForm)"/> may return the input unchanged
    /// (no ICU/NLS), so FormD decomposition alone silently left "müşteri" unmatched against "musteri".
    /// </summary>
    public static string Normalize(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var builder = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            // Combining marks from already-decomposed input (e.g. "ü") are dropped.
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(Fold(ch));
        }
        return builder.ToString();
    }

    private static char Fold(char ch) => ch switch
    {
        'ı' or 'İ' or 'I' or 'î' or 'Î' or 'í' or 'Í' or 'ì' or 'Ì' or 'ï' or 'Ï' => 'i',
        'ş' or 'Ş' or 'ß' => 's',
        'ç' or 'Ç' => 'c',
        'ğ' or 'Ğ' => 'g',
        'ö' or 'Ö' or 'ô' or 'Ô' or 'ó' or 'Ó' or 'ò' or 'Ò' => 'o',
        'ü' or 'Ü' or 'û' or 'Û' or 'ú' or 'Ú' or 'ù' or 'Ù' => 'u',
        'ä' or 'Ä' or 'â' or 'Â' or 'á' or 'Á' or 'à' or 'À' => 'a',
        'é' or 'É' or 'è' or 'È' or 'ê' or 'Ê' or 'ë' or 'Ë' => 'e',
        'ñ' or 'Ñ' => 'n',
        _ => char.ToLowerInvariant(ch),
    };

    /// <summary>German compound nouns put the meaningful stem at the END ("Kreditrichtlinie").</summary>
    private static bool HasCompoundSuffix(string normalizedText, params string[] fragments) =>
        fragments.Any(fragment => Regex.IsMatch(normalizedText,
            $@"\p{{L}}{Regex.Escape(fragment)}\p{{L}}*",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)));

    /// <summary>Whole word or morphological suffix match, never substring inside an unrelated word.</summary>
    public static bool HasAnyStem(string normalizedText, params string[] stems) =>
        stems.Any(stem => Regex.IsMatch(normalizedText,
            $@"(?<!\p{{L}}){Regex.Escape(stem)}\p{{L}}*",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)));

    private static readonly Regex RecordIdentifier = new(@"\b[CK]-[0-9]{3,4}\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(50));

    /// <summary>
    /// "Send / transfer / pay …": an instruction to prepare a payment, not a question about bank records. Such turns
    /// may legitimately end with a clarifying question ("which IBAN?") instead of retrieved data.
    /// </summary>
    public static bool IsPaymentInstruction(string normalized) =>
        // Verbs only (prefix match): nouns such as "payment", "credit" or "transfer" also occur in read questions.
        HasAnyStem(normalized, "gonder", "yolla", "aktar", "hazirla", "olustur", "basvur", "send", "wire", "prepare", "apply",
            "uberweise", "sende", "schick", "beantrag", "anlegen");

    public static bool IsBankRecordRequest(string question) =>
        RecordIdentifier.IsMatch(question) ||
        IsCustomerRequest(Normalize(question)) ||
        IsCreditRequest(Normalize(question)) ||
        IsAccountRequest(Normalize(question)) || IsBalanceRequest(Normalize(question)) ||
        HasAnyStem(Normalize(question), "iban", "payment", "zahlungen", "zahlung", "odeme", "odemeler",
            "transfer", "transaction", "buchung", "standing", "dauerauftrag", "automatik", "otomatik");

    public static bool IsCustomerRequest(string normalized) =>
        HasAnyStem(normalized, "customer", "customers", "client", "clients", "kunde", "kunden", "musteri", "musteriler");

    public static bool IsPluralCustomerRequest(string normalized) =>
        HasAnyStem(normalized, "customers", "clients", "kunden", "musteriler");

    public static bool IsCreditRequest(string normalized) =>
        HasAnyStem(normalized, "credit", "loan", "kredit", "basvur", "antrag", "antrage");

    /// <summary>Amounts in bank ACCOUNT records, not inferred cash or net worth. This is an intent hint only.</summary>
    public static bool IsBalanceRequest(string normalized) =>
        HasAnyStem(normalized, "balance", "balances", "money", "funds", "deposit", "deposits", "cash",
            "bakiye", "bakiy", "mevduat", "nakit", "para", "param", "varlik", "servet",
            "guthaben", "kontostand", "saldo", "gesamtsaldo", "geld", "vermog", "vermogen");

    public static bool IsAccountRequest(string normalized) =>
        HasAnyStem(normalized, "account", "accounts", "konto", "konten", "hesap", "kontostand");

    public static bool IsTotalBalanceRequest(string normalized) =>
        IsBalanceRequest(normalized) && HasAnyStem(normalized, "total", "sum", "overall", "combined",
            "toplam", "hepsi", "tum", "butun", "gesamt", "summe", "alle", "insgesamt");

    /// <summary>Conservative conversational reference hint, not a replacement for LLM interpretation.
    /// Previous USER text is context only; every banking figure still needs a new authorized tool result.</summary>
    public static bool IsContextualFollowUp(string message)
    {
        var current = Normalize(message).Trim();
        return current.Length <= 120 && Regex.IsMatch(current,
            @"^(peki\b|onun\b|bunlarin\b|what about\b|and (the |its |their )?total\b|und\b|wie viel insgesamt\b)",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    }

    public static bool IsCredentialRequest(string normalized) =>
        HasAnyStem(normalized, "password", "passwd", "passwort", "kennwort", "parola", "sifre", "credential");

    public static string AccessibleCustomerScope(AssistantLanguage language) => language switch
    {
        AssistantLanguage.Turkish => "Bu sonuçlar yalnızca erişim izniniz olan müşteri kayıtlarıdır; bankanın tüm müşterileri veya sizin mülkiyetiniz anlamına gelmez.",
        AssistantLanguage.German => "Dies sind nur Kunden, auf deren Datensätze Sie Zugriff haben – nicht sämtliche Bankkunden.",
        _ => "These are only customers you are authorized to access, not the bank's entire customer base.",
    };

    public static string UnsupportedLoanOutstandingBalance(AssistantLanguage language) => language switch
    {
        AssistantLanguage.Turkish => "Kredi borcunun kalan bakiyesi için doğrulanmış bir işlem bulunmuyor. Müşteri hesap bakiyeleriyle kredi borcunu aynı şey gibi göstermeyeceğim.",
        AssistantLanguage.German => "Für die verbleibende Kreditrestschuld ist kein verifiziertes Tool verfügbar. Kontosalden sind keine Kreditrestschuld.",
        _ => "No authorized tool provides the outstanding loan balance. Customer account balances are not loan debt.",
    };

    public static string MissingCustomerSelection(AssistantLanguage language) => language switch
    {
        AssistantLanguage.Turkish => "Hangi müşterinin hesap bakiyesini istediğinizi belirtin (ör. C-1002). Tüm erişebildiğiniz müşteriler için 'müşterilerimin toplam bakiyeleri' diyebilirsiniz. Nakit veya net servet bilgisi mevcut değil.",
        AssistantLanguage.German => "Bitte geben Sie eine Kunden-ID an (z. B. C-1002). Für alle zugänglichen Kunden fragen Sie nach 'Gesamtsalden meiner Kunden'. Bargeld oder Nettovermögen werden nicht erfasst.",
        _ => "Specify a customer ID (e.g. C-1002), or ask for 'total account balances of my customers'. Physical cash and net worth are not available.",
    };

    public static bool IsDocumentRequest(string normalized) =>
        HasAnyStem(normalized, "document", "policy", "procedure", "guideline", "regulation",
            "dokument", "datei", "richtlin", "verfahren", "regelwerk", "dokuman", "belge", "dosya", "file", "politik", "prosedur", "yonetmelik", "kosul") ||
        HasCompoundSuffix(normalized, "richtlinie", "dokument", "regelwerk");

    /// <summary>Best-effort language of the message; falls back to English when nothing is recognised.</summary>
    public static AssistantLanguage Detect(string message) => DetectRecognised(message) ?? AssistantLanguage.English;

    /// <summary>True only when a TR/DE/EN marker actually matched (not the English fallback).</summary>
    public static bool IsLanguageRecognised(string message) => DetectRecognised(message) is not null;

    private static AssistantLanguage? DetectRecognised(string message)
    {
        var text = Normalize(message);
        if (HasAnyStem(text, "musteri", "basvur", "hesap", "goster", "listele", "belge", "dokuman", "politika", "onayla", "bana", "nelerdir", "sart", "nakit", "para", "bakiye", "bakiy", "toplam", "mevduat", "sifre", "iban", "odeme", "otomatik", "islem", "gelecek",
                "hangi", "neler", "nasil", "yardim", "asistan", "yapabilir", "benim", "nedir",
                "gonder", "yolla", "havale", "para", "hesabima", "hesabimdan", "adina"))
            return AssistantLanguage.Turkish;
        if (HasAnyStem(text, "kunde", "zeig", "meine", "kredit", "richtlin", "konto", "bitte", "welche", "antrag", "antrage", "guthaben", "kontostand", "saldo", "geld", "kennwort",
                "ich", "kann", "assistent", "wie", "mochte", "habe", "nicht", "darf"))
            return AssistantLanguage.German;
        if (HasAnyStem(text, "what", "which", "show", "how", "can", "please", "list", "my", "the", "help", "assistant"))
            return AssistantLanguage.English;
        return null;
    }

    public static string NoDocumentEvidence(AssistantLanguage language) => language switch
    {
        AssistantLanguage.Turkish => "Erişiminize açık belgelerde bu soruyu doğrulayacak yeterli kaynak bulamadım. Bilgi uydurmayacağım.",
        AssistantLanguage.German => "In den für Sie zugänglichen Dokumenten habe ich keine ausreichende Quelle gefunden. Ich werde keine Antwort erfinden.",
        _ => "I could not find sufficient supporting evidence in documents you can access. I will not guess.",
    };

    /// <summary>Application-owned text for a document listing; the titles themselves are in the result table.</summary>
    public static string DocumentListSummary(int count, AssistantLanguage language) => (count, language) switch
    {
        (0, AssistantLanguage.Turkish) => "Okuma yetkiniz olan bir belge bulunmuyor.",
        (0, AssistantLanguage.German) => "Für Sie sind keine lesbaren Dokumente freigegeben.",
        (0, _) => "There are no documents you are allowed to read.",
        (_, AssistantLanguage.Turkish) => $"Okuma yetkiniz olan {count} belge tabloda listelendi. Bir belge hakkında soru sorabilir veya 'DOC-101 göster' diyebilirsiniz.",
        (_, AssistantLanguage.German) => $"{count} für Sie lesbare Dokumente sind in der Tabelle aufgeführt. Stellen Sie eine Frage dazu oder sagen Sie 'Zeige DOC-101'.",
        (_, _) => $"{count} documents you may read are listed in the table. Ask about one of them or say 'Show DOC-101'.",
    };
}
