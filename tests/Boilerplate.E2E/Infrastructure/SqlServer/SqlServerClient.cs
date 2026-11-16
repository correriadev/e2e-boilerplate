using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Boilerplate.E2E.Reporting;

namespace Boilerplate.E2E.Infrastructure.SqlServer;

public sealed record OrderRow(Guid Id, Guid Reference, string Sku, decimal Price, string Currency);

public sealed class SqlServerClient(string connectionString)
{
    private const string FindOrderSql = "SELECT Id, Reference, Sku, Price, Currency FROM dbo.Orders WHERE Id = @id";
    private const string DeleteOrderSql = "DELETE FROM dbo.Orders WHERE Id = @id AND Reference = @id";

    public Task<OrderRow?> FindOrderAsync(Guid id, CancellationToken cancellation) =>
        AllureEvidence.Operation<OrderRow?>("SQL Server", async () =>
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellation);
            await using var command = new SqlCommand(FindOrderSql, connection) { CommandTimeout = 5 };
            command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
            CaptureCommand(command);
            await using var reader = await command.ExecuteReaderAsync(cancellation);
            OrderRow? row = await reader.ReadAsync(cancellation)
                ? new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetDecimal(3), reader.GetString(4)) : null;
            AllureEvidence.Json("Pedido persistido", new { operation = "FindOrderById", executionState = "completed", id, rows = row is null ? 0 : 1,
                expected = new { price = 42.50m, currency = "BRL" },
                observed = EvidenceSanitizer.Project(JsonSerializer.Serialize(row, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })) });
            return row;
        });

    public Task<int> DeleteOrderAsync(Guid id, CancellationToken cancellation) =>
        AllureEvidence.Operation("SQL Server", async () =>
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellation);
            await using var command = new SqlCommand(DeleteOrderSql, connection) { CommandTimeout = 5 };
            command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
            CaptureCommand(command);
            var rows = await command.ExecuteNonQueryAsync(cancellation);
            AllureEvidence.Json("Limpeza SQL Server", new { operation = "DeleteOwnedOrder", executionState = "completed", id, rows });
            return rows;
        });

    private static void CaptureCommand(SqlCommand command)
    {
        // Exact text is publishable only for these reviewed, static templates and a safe GUID.
        // Do not generalize this to arbitrary SQL or arbitrary parameter values.
        var readOnly = command.CommandText == FindOrderSql;
        if ((!readOnly && command.CommandText != DeleteOrderSql) || command.CommandType != CommandType.Text ||
            command.Parameters.Count != 1 || command.Parameters[0].ParameterName != "@id" ||
            command.Parameters[0].SqlDbType != SqlDbType.UniqueIdentifier || command.Parameters[0].Value is not Guid id)
            throw new InvalidOperationException("Comando SQL sem política de evidência aprovada. Conteúdo omitido.");

        var operation = readOnly ? "FindOrderById" : "DeleteOwnedOrder";
        var replaySql = (readOnly ? "-- SOMENTE LEITURA: reproduz a observação do teste.\n" :
                "-- ALTERA DADOS: limpeza do recurso desta jornada; execute apenas se desejar removê-lo.\n") +
            "-- Selecione a mesma base configurada para o cenário; nenhuma credencial é incluída.\n" +
            $"DECLARE @id uniqueidentifier = '{id:D}';\n{command.CommandText};\n";
        AllureEvidence.Json("Comando SQL Server", new
        {
            operation, executionState = "prepared", commandType = "Text", commandText = command.CommandText,
            parameters = new[] { new { name = "@id", sqlType = "uniqueidentifier", value = id } },
            commandTimeoutSeconds = command.CommandTimeout, readOnly, reproducible = true, replaySql
        });
        AllureEvidence.Text(readOnly ? "Reproduzir consulta SQL Server (somente leitura)" :
            "Reproduzir limpeza SQL Server (ALTERA DADOS)", replaySql, ".sql");
    }
}
