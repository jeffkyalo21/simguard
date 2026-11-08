using System.Security.Cryptography;

namespace SimGuard.Api.Email;

// Passwords that the API issues itself for a new or recovered account. The
// Admin never types or sees them: they go to the person by email only.
public static class SignInCredentials
{
    // No look-alike characters (0 O 1 l I). 20 characters from 56 is far above
    // the Identity rules (12 characters, 4 distinct).
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
    private const int Length = 20;

    public static string GeneratePassword()
    {
        var chars = new char[Length];

        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(chars);
    }

    // True when the provider accepted the email. Never throws.
    public static Task<bool> SendAsync(
        IEmailSender sender,
        BrevoOptions options,
        string name,
        string email,
        string password,
        CancellationToken cancellationToken) =>
        sender.SendAsync(
            email,
            name,
            EmailTemplates.AccountReady(name, email, password, options.SignInUrl),
            cancellationToken);
}
