using System.Globalization;
using System.Text.Json;
using VirtualBank.Application.Customers.Queries.SummarizeCustomerBalances;

namespace VirtualBank.Application.Assistant;

/// <summary>
/// Trusted finance narrative, generated in application code from authorized use-case values, never model arithmetic.
/// The aggregation is BY currency. "Account balance" must not be misrepresented as cash or net worth.
/// </summary>
public static class AssistantBalanceFormatter
{
    public static string Format(CustomerBalanceOverviewView overview, AssistantLanguage language)
    {
        if (overview.TotalsByCurrency.Count == 0)
            return language switch
            {
                AssistantLanguage.Turkish => "Erişiminize açık seçili müşteriler için hesap bakiyesi kaydı bulunamadı. Bu, sıfır bakiyeye sahip olduklarını göstermez.",
                AssistantLanguage.German => "Für die zugänglichen Kunden wurden keine Kontostände gefunden. Das bedeutet nicht, dass die Salden null sind.",
                _ => "No account balance records were found for the accessible selection. This does not establish a zero balance.",
            };
        var totals = string.Join(", ", overview.TotalsByCurrency.Select(t =>
            $"{t.Balance.ToString("N2", CultureInfo.InvariantCulture)} {t.Currency}"));
        return language switch
        {
            AssistantLanguage.Turkish =>
                $"{overview.CustomerCount} erişilebilir müşteri için hesap bakiyeleri para birimine göre: {totals}. " +
                "Detaylar tabloda. Bu tutarlar hesap bakiyeleridir; fiziksel nakit, net servet veya tüm banka müşterileri anlamına gelmez. Farklı para birimleri toplanmadı.",
            AssistantLanguage.German =>
                $"Kontosalden für {overview.CustomerCount} zugängliche Kunden, getrennt nach Währung: {totals}. " +
                "Details stehen in der Tabelle. Es handelt sich um Kontosalden, nicht um Bargeld oder Nettovermögen. Währungen wurden nicht addiert.",
            _ =>
                $"Account balances for {overview.CustomerCount} accessible customers by currency: {totals}. " +
                "Details are in the table. These are account balances, not physical cash or net worth. Different currencies were not added together.",
        };
    }

    public static string Format(JsonElement data, AssistantLanguage language)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("totalsByCurrency", out var totals) ||
            totals.ValueKind != JsonValueKind.Array)
            return NaturalLanguageIntent.MissingCustomerSelection(language);
        var values = totals.EnumerateArray().Select(t => new CurrencyBalanceTotal(
            t.GetProperty("currency").GetString() ?? "",
            t.GetProperty("balance").GetDecimal(),
            t.GetProperty("accountCount").GetInt32())).ToArray();
        var count = data.TryGetProperty("customerCount", out var c) ? c.GetInt32() : 0;
        return Format(new CustomerBalanceOverviewView([], values, count, true), language);
    }
}
