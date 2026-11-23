namespace SimGuard.Api.Common;

// A deleted account keeps its row (audit rows point at it) with the personal
// data replaced. There is no deleted flag, so the placeholder email domain is
// the marker. It is reserved (.invalid never resolves) and POST /api/users
// refuses it, so only account deletion writes it.
public static class DeletedAccounts
{
    public const string EmailDomain = "@deleted.invalid";

    public static string NameFor(int id) => $"Deleted user {id}";

    public static string EmailFor(int id) => $"deleted-user-{id}{EmailDomain}";

    public static bool IsDeletedEmail(string? email) =>
        email is not null && email.EndsWith(EmailDomain, StringComparison.OrdinalIgnoreCase);
}
