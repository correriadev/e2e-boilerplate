using System.Net.Http.Json;
using Boilerplate.E2E.Reporting;

namespace Boilerplate.E2E.Infrastructure.Http;

public sealed record ApiResponse(int Status, string Body);

public sealed class ApiClient(HttpClient client)
{
    public Task<ApiResponse> SendAsync(HttpMethod method, string path, Guid correlation,
        object? body = null, CancellationToken cancellation = default) =>
        AllureEvidence.Operation("HTTP", async () =>
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            var operationToken = deadline.Token;
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Add("X-Correlation-Id", correlation.ToString());
            // Synthetic secret goes through actual HTTP but never through the evidence projection.
            request.Headers.Add("Authorization", "Bearer " + (Environment.GetEnvironmentVariable("E2E_SYNTHETIC_SECRET") ?? "local-evidence-canary"));
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, operationToken);
            await using var stream = await response.Content.ReadAsStreamAsync(operationToken);
            var buffer = new byte[16385];
            var length = 0;
            while (length < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(length), operationToken);
                if (read == 0) break;
                length += read;
            }
            var content = length > 16384 ? "" : System.Text.Encoding.UTF8.GetString(buffer, 0, length);
            AllureEvidence.Json("Operação HTTP", new
            {
                method = method.Method, route = EvidenceSanitizer.Route(path), status = (int)response.StatusCode,
                correlation, response = EvidenceSanitizer.Project(content), truncated = length > 16384
            });
            return new ApiResponse((int)response.StatusCode, content);
        });
}
