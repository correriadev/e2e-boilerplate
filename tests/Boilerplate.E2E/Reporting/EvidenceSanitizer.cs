using System.Text.Json;
using System.Text.RegularExpressions;

namespace Boilerplate.E2E.Reporting;

// Deny by default: no headers, cookies, URL/query, SQL text/parameters or raw bodies.
public static partial class EvidenceSanitizer
{
    public static object Project(string? body)
    {
        try
        {
            if (body is null || body.Length > 16384) return new { omitted = true, reason = "body absent or exceeds 16 KiB" };
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            var result = new Dictionary<string, object>();
            if (root.ValueKind != JsonValueKind.Object) return new { omitted = true, reason = "unexpected JSON shape" };
            foreach (var field in root.EnumerateObject())
            {
                if ((field.Name is "id" or "reference") && field.Value.ValueKind == JsonValueKind.String && Guid.TryParse(field.Value.GetString(), out var id))
                    result[field.Name] = id;
                if (field.Name == "price" && field.Value.TryGetDecimal(out var price) && price is >= 0 and <= 1000000)
                    result[field.Name] = price;
                if (field.Name == "currency" && field.Value.ValueKind == JsonValueKind.String && field.Value.GetString() is "BRL" or "USD" or "EUR")
                    result[field.Name] = field.Value.GetString()!;
                if (field.Name == "sku" && field.Value.ValueKind == JsonValueKind.String && field.Value.GetString() is "SKU-E2E" or "SKU-UNAVAILABLE")
                    result[field.Name] = field.Value.GetString()!;
                if (field.Name == "code" && field.Value.ValueKind == JsonValueKind.String && field.Value.GetString() is "catalog_unavailable" or "not_found" or "ready")
                    result[field.Name] = field.Value.GetString()!;
            }
            return result;
        }
        catch { return new { omitted = true, reason = "unreadable response; raw content discarded" }; }
    }

    public static string Route(string path)
    {
        var route = path.Split('?', '#')[0];
        return SafeRoute().IsMatch(route) ? route : "[route omitted]";
    }

    [GeneratedRegex(@"^/(health/ready|orders(?:/[0-9a-fA-F-]{36})?)$")]
    private static partial Regex SafeRoute();
}
