using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace SimGuard.Api.Email;

// Configuration section "Brevo". ApiKey comes from user-secrets or the
// environment (Brevo__ApiKey) and is never committed. Without an ApiKey no
// email is sent: the sender logs a warning and reports "not sent".
public sealed class BrevoOptions
{
    public const string Section = "Brevo";

    public string? ApiKey { get; set; }

    // Must be a sender on a domain verified in Brevo.
    public string SenderEmail { get; set; } = "no-reply@sim-guard.app";

    public string SenderName { get; set; } = "SimGuard";

    public string BaseUrl { get; set; } = "https://api.brevo.com";

    // Where the emails send people to sign in.
    public string SignInUrl { get; set; } = "http://localhost:3000/login";

    public double TimeoutSeconds { get; set; } = 10;
}

public interface IEmailSender
{
    // True when the provider accepted the message. Never throws: a failed
    // email must not fail the request that triggered it.
    Task<bool> SendAsync(string toEmail, string toName, EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed record EmailMessage(string Subject, string Text, string Html);

public sealed class BrevoEmailSender(HttpClient http, IOptions<BrevoOptions> options, ILogger<BrevoEmailSender> logger)
    : IEmailSender
{
    private readonly BrevoOptions _options = options.Value;

    public async Task<bool> SendAsync(
        string toEmail, string toName, EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            logger.LogWarning("Brevo:ApiKey is not set. An email was not sent.");
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/v3/smtp/email")
            {
                Content = JsonContent.Create(new BrevoPayload(
                    new BrevoAddress(_options.SenderEmail, _options.SenderName),
                    [new BrevoAddress(toEmail, toName)],
                    message.Subject,
                    message.Html,
                    message.Text))
            };
            request.Headers.Add("api-key", _options.ApiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            using var response = await http.SendAsync(request, timeout.Token);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            // The body is not logged: it can echo the recipient.
            logger.LogWarning("Brevo refused an email with status {Status}.", (int)response.StatusCode);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            logger.LogWarning("An email could not be sent: {Reason}.", ex.GetType().Name);
            return false;
        }
    }

    private sealed record BrevoAddress(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("name")] string Name);

    private sealed record BrevoPayload(
        [property: JsonPropertyName("sender")] BrevoAddress Sender,
        [property: JsonPropertyName("to")] BrevoAddress[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("htmlContent")] string HtmlContent,
        [property: JsonPropertyName("textContent")] string TextContent);
}

public static class EmailRegistration
{
    public static IServiceCollection AddEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BrevoOptions>(configuration.GetSection(BrevoOptions.Section));
        services.AddHttpClient<IEmailSender, BrevoEmailSender>(client => client.Timeout = Timeout.InfiniteTimeSpan);
        return services;
    }
}
