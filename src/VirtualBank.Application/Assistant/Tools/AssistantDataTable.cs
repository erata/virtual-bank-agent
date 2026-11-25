using System.Globalization;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Credit;
using VirtualBank.Application.Documents;
using VirtualBank.Application.Customers;
using VirtualBank.Application.Customers.Abstractions;
using VirtualBank.Application.Customers.Queries.SummarizeCustomerBalances;

namespace VirtualBank.Application.Assistant.Tools;

/// <summary>
/// Deterministic, authorized structured results for UI tables. Only whitelisted fields of an already-authorized
/// use-case outcome are projected. No tool/model-generated HTML, markdown, SQL, or arbitrary object reflection.
/// </summary>
public sealed record AssistantDataTable(
    string Title, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, int TotalCount,
    string? SourceTool = null);

public static class AssistantTableProjector
{
    private const int MaxRows = 100;

    public static AssistantDataTable? Create(string toolName, object? data, AssistantLanguage language) => (toolName, data) switch
    {
        ("list_customers", IReadOnlyList<CustomerSummaryView> customers) => Build(toolName,
            Text(language, "Müşteriler", "Kunden", "Customers"),
            [Text(language, "Müşteri No", "Kundennr.", "Customer ID"),
             Text(language, "Ad Soyad", "Name", "Full name"),
             Text(language, "Şube", "Filiale", "Branch"),
             Text(language, "Segment", "Segment", "Segment")],
            customers.Select(c => new[] { c.Id, $"{c.FirstName} {c.LastName}", c.Branch, c.Segment })),

        ("list_credit_applications", IReadOnlyList<CreditApplicationView> applications) => Build(toolName,
            Text(language, "Kredi başvuruları", "Kreditanträge", "Credit applications"),
            [Text(language, "Başvuru No", "Antragsnr.", "Application ID"),
             Text(language, "Müşteri No", "Kundennr.", "Customer ID"),
             Text(language, "Tutar", "Betrag", "Amount"),
             Text(language, "Para birimi", "Währung", "Currency"),
             Text(language, "Durum", "Status", "Status")],
            applications.Select(a => new[] { a.Id, a.CustomerId,
                a.Amount.ToString("N2", CultureInfo.InvariantCulture), a.Currency, CreditStatus(a.Status.ToString(), language) })),

        ("list_customer_accounts" or "get_my_accounts", IReadOnlyList<AccountView> accounts) => Build(toolName,
            Text(language, "Hesaplar", "Konten", "Accounts"),
            [Text(language, "Hesap No", "Kontonr.", "Account ID"),
             Text(language, "Tür", "Typ", "Type"),
             Text(language, "Para birimi", "Währung", "Currency"),
             Text(language, "Bakiye", "Saldo", "Balance")],
            accounts.Select(a => new[] { a.Id, a.Type, a.Currency,
                a.Balance.ToString("N2", CultureInfo.InvariantCulture) })),

        ("summarize_customer_balances" or "get_my_balance_summary", CustomerBalanceOverviewView overview) => Build(toolName,
            toolName == "get_my_balance_summary"
                ? Text(language, "Kendi hesap bakiyelerim", "Meine Kontosalden", "My account balances")
                : Text(language, "Erişilebilir müşterilerin hesap bakiyeleri", "Kontosalden zugänglicher Kunden", "Account balances for accessible customers"),
            [Text(language, "Müşteri No", "Kundennr.", "Customer ID"),
             Text(language, "Ad Soyad", "Name", "Full name"),
             Text(language, "Para birimi", "Währung", "Currency"),
             Text(language, "Hesap adedi", "Anzahl Konten", "Account count"),
             Text(language, "Toplam hesap bakiyesi", "Kontosaldo", "Account balance")],
            overview.Rows.Select(row => new[] { row.CustomerId, row.CustomerName, row.Currency,
                row.AccountCount.ToString(CultureInfo.InvariantCulture),
                row.Balance.ToString("N2", CultureInfo.InvariantCulture) })),

        ("get_my_upcoming_payments", IReadOnlyList<ScheduledPaymentView> payments) => Build(toolName,
            Text(language,"Planlanan ödemeler","Geplante Zahlungen","Upcoming payments"),
            ["ID", Text(language,"Açıklama","Beschreibung","Description"), Text(language,"Tutar","Betrag","Amount"),
             Text(language,"Para birimi","Währung","Currency"), Text(language,"Vade","Fälligkeit","Due date")],
            payments.Select(x => new[] { x.Id, x.Description, x.Amount.ToString("N2", CultureInfo.InvariantCulture), x.Currency, x.DueDate.ToString("yyyy-MM-dd") })),
        ("get_my_recent_transactions", IReadOnlyList<BookedTransactionView> transactions) => Build(toolName,
            Text(language,"Son işlemler","Letzte Buchungen","Recent transactions"),
            ["ID", Text(language,"Açıklama","Beschreibung","Description"), Text(language,"Tutar","Betrag","Amount"),
             Text(language,"Para birimi","Währung","Currency"), Text(language,"Tarih","Datum","Date")],
            transactions.Select(x => new[] { x.Id, x.Description, x.Amount.ToString("N2", CultureInfo.InvariantCulture), x.Currency, x.BookedOn.ToString("yyyy-MM-dd") })),
        ("get_my_standing_orders", IReadOnlyList<StandingOrderView> standing) => Build(toolName,
            Text(language,"Otomatik ödeme talimatları","Daueraufträge","Standing orders"),
            ["ID", Text(language,"Açıklama","Beschreibung","Description"), Text(language,"Tutar","Betrag","Amount"),
             Text(language,"Para birimi","Währung","Currency"),Text(language,"Sıklık","Intervall","Frequency"),
             Text(language,"Sonraki tarih","Nächstes Datum","Next date")],
            standing.Select(x => new[] { x.Id, x.Description, x.Amount.ToString("N2", CultureInfo.InvariantCulture), x.Currency, x.Frequency, x.NextDueDate.ToString("yyyy-MM-dd") })),
        ("list_documents", IReadOnlyList<DocumentListItem> documents) => Build(toolName,
            Text(language, "Erişebildiğiniz belgeler", "Für Sie lesbare Dokumente", "Documents you can read"),
            [Text(language, "Belge No", "Dokument-ID", "Document ID"),
             Text(language, "Başlık", "Titel", "Title"),
             Text(language, "Dil", "Sprache", "Language"),
             Text(language, "Gizlilik", "Einstufung", "Classification")],
            documents.Select(d => new[] { d.DocumentId, d.Title, d.Language.ToUpperInvariant(), ClassificationLabel(d.Classification, language) })),

        _ => null,
    };

    private static string ClassificationLabel(Clearance level, AssistantLanguage language) => level switch
    {
        Clearance.Public => Text(language, "Herkese açık", "Öffentlich", "Public"),
        Clearance.Internal => Text(language, "Dahili", "Intern", "Internal"),
        Clearance.Confidential => Text(language, "Gizli", "Vertraulich", "Confidential"),
        _ => Text(language, "Çok gizli", "Streng vertraulich", "Strictly confidential"),
    };

    private static AssistantDataTable Build(string toolName, string title, string[] columns, IEnumerable<string[]> source)
    {
        var items = source.ToArray();
        var rows = items.Take(MaxRows).Select(row => (IReadOnlyList<string>)row).ToArray();
        return new AssistantDataTable(title, columns, rows, items.Length, toolName);
    }

    private static string CreditStatus(string status, AssistantLanguage language) => (status, language) switch
    {
        ("Draft", AssistantLanguage.Turkish) => "Taslak",
        ("Submitted", AssistantLanguage.Turkish) => "Gönderildi",
        ("Approved", AssistantLanguage.Turkish) => "Onaylandı",
        ("Rejected", AssistantLanguage.Turkish) => "Reddedildi",
        ("Draft", AssistantLanguage.German) => "Entwurf",
        ("Submitted", AssistantLanguage.German) => "Eingereicht",
        ("Approved", AssistantLanguage.German) => "Genehmigt",
        ("Rejected", AssistantLanguage.German) => "Abgelehnt",
        _ => status,
    };

    private static string Text(AssistantLanguage language, string tr, string de, string en) => language switch
    {
        AssistantLanguage.Turkish => tr,
        AssistantLanguage.German => de,
        _ => en,
    };
}
