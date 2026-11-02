using System.Net.Mail;

namespace SimGuard.Api.Common;

public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = new();

    public bool HasErrors => _errors.Count > 0;

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var messages))
        {
            messages = [];
            _errors[field] = messages;
        }

        messages.Add(message);
    }

    public string RequiredString(string field, string? value, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            Add(field, $"{field} is required.");
        }
        else if (trimmed.Length > maxLength)
        {
            Add(field, $"{field} must be at most {maxLength} characters.");
        }

        return trimmed;
    }

    // Returns the number in E.164 form (+254712345678) when valid.
    public string RequiredPhoneNumber(string field, string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            Add(field, $"{field} is required.");
            return trimmed;
        }

        var normalized = PhoneNumbers.Normalize(trimmed);

        if (normalized is null)
        {
            Add(field, $"{field} must be a valid phone number, for example +254712345678 or 0712345678.");
            return trimmed;
        }

        return normalized;
    }

    public string RequiredEmail(string field, string? value, int maxLength)
    {
        var email = RequiredString(field, value, maxLength);

        if (!_errors.ContainsKey(field) && !IsValidEmail(email))
        {
            Add(field, $"{field} must be a valid email address.");
        }

        return email;
    }

    public int RequiredId(string field, int? value)
    {
        if (value is null)
        {
            Add(field, $"{field} is required.");
        }
        else if (value <= 0)
        {
            Add(field, $"{field} must be a positive integer.");
        }

        return value ?? 0;
    }

    public IResult ToProblem() =>
        TypedResults.ValidationProblem(
            _errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));

    private static bool IsValidEmail(string email) =>
        !email.Any(char.IsWhiteSpace)
        && MailAddress.TryCreate(email, out var address)
        && address.Address == email;
}
