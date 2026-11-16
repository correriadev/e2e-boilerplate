using System.Text;
using System.Text.Json;
using Allure.Net.Commons;

namespace Boilerplate.E2E.Reporting;

public static class AllureEvidence
{
    // Internal callers supply only explicitly projected structures, never arbitrary payloads.
    internal static void Json(string name, object projection)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(projection, new JsonSerializerOptions { WriteIndented = true });
        if (bytes.Length > 8192)
            bytes = Encoding.UTF8.GetBytes("{\"truncated\":true,\"reason\":\"evidence exceeds 8 KiB\"}");
        AllureApi.AddAttachment(name, "application/json", bytes, ".json");
    }

    // Only already-reviewed projections: never raw SQL, payloads or exception messages.
    internal static void Text(string name, string safeText, string extension = ".txt")
    {
        var bytes = Encoding.UTF8.GetBytes(safeText);
        if (bytes.Length > 8192)
            bytes = Encoding.UTF8.GetBytes("Evidence omitted: exceeds 8 KiB; incomplete script must not be executed.");
        AllureApi.AddAttachment(name, "text/plain", bytes, extension);
    }

    public static async Task<T> Operation<T>(string component, Func<Task<T>> action)
    {
        try { return await action(); }
        catch (OperationCanceledException)
        {
            Json("Falha de operação", new { component, kind = "timeout or cancellation" });
            throw new InvalidOperationException($"{component}: operação cancelada ou prazo excedido. Verifique a disponibilidade do serviço.");
        }
        catch (Exception error)
        {
            Json("Falha de operação", new { component, kind = "connection, authentication or contract",
                errorType = error.GetType().Name,
                sqlErrorNumber = (error as Microsoft.Data.SqlClient.SqlException)?.Number,
                sqlErrorState = (error as Microsoft.Data.SqlClient.SqlException)?.State,
                phase = error.Message.Contains("pre-login", StringComparison.OrdinalIgnoreCase) ? "pre-login handshake" :
                    error.Message.Contains("login", StringComparison.OrdinalIgnoreCase) ? "login" : "operation",
                transport = error.Message.Contains("TCP", StringComparison.OrdinalIgnoreCase) ? "TCP" :
                    error.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase) ? "TLS" : "unspecified" });
            // No original message/inner exception: drivers can include endpoints and credentials.
            throw new InvalidOperationException($"{component}: falha de conexão, autenticação ou contrato. Verifique configuração e prontidão do serviço.");
        }
    }
}
