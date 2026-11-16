using System.Text.Json;
using Microsoft.Data.SqlClient;
using StackExchange.Redis;

namespace Boilerplate.E2E.Configuration;

public sealed record E2eSettings(Uri BaseUrl, string SqlServerConnectionString, string RedisConnectionString)
{
    public static E2eSettings Load(string? directory = null, Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        try
        {
            var values = new Dictionary<string, string>();
            foreach (var file in new[] { "appsettings.json", "appsettings.local.json" })
            {
                var path = Path.Combine(directory ?? AppContext.BaseDirectory, file);
                if (!File.Exists(path) && file.Contains("local")) continue;
                using var json = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var item in json.RootElement.EnumerateObject())
                    values[item.Name] = item.Value.GetString() ?? "";
            }
            string Get(string key, string variable) => environment(variable) ?? values.GetValueOrDefault(key, "");
            var url = Get("baseurl", "E2E_BASEURL");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https") ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment))
                throw new InvalidOperationException("Preencha baseurl / E2E_BASEURL com URL HTTP(S) absoluta, sem credenciais, query ou fragmento.");
            return new(uri, Get("sqlserver-connectionstring", "E2E_SQLSERVER_CONNECTIONSTRING"),
                Get("redis-connectionstring", "E2E_REDIS_CONNECTIONSTRING"));
        }
        catch (InvalidOperationException) { throw; }
        catch { throw new InvalidOperationException("Não foi possível ler appsettings.json/local no diretório de saída. Confira JSON com valores textuais e cópia dos arquivos."); }
    }

    public void RequireSqlServer()
    {
        const string hint = "Preencha sqlserver-connectionstring / E2E_SQLSERVER_CONNECTIONSTRING com connection string SQL Server válida.";
        if (string.IsNullOrWhiteSpace(SqlServerConnectionString)) throw new InvalidOperationException(hint);
        try
        {
            var builder = new SqlConnectionStringBuilder(SqlServerConnectionString);
            if (string.IsNullOrWhiteSpace(builder.DataSource) || string.IsNullOrWhiteSpace(builder.InitialCatalog))
                throw new FormatException();
        }
        catch { throw new InvalidOperationException(hint); }
    }

    public ConfigurationOptions RedisOptions()
    {
        const string hint = "Preencha redis-connectionstring / E2E_REDIS_CONNECTIONSTRING com configuração Redis válida.";
        if (string.IsNullOrWhiteSpace(RedisConnectionString)) throw new InvalidOperationException(hint);
        try
        {
            var options = ConfigurationOptions.Parse(RedisConnectionString);
            if (options.EndPoints.Count == 0) throw new FormatException();
            options.ConnectTimeout = 5000;
            options.AsyncTimeout = 5000;
            options.SyncTimeout = 5000;
            options.ConnectRetry = 1;
            options.AbortOnConnectFail = true;
            options.IncludeDetailInExceptions = false;
            return options;
        }
        catch { throw new InvalidOperationException(hint); }
    }
}
