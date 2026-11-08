using System.Net;

namespace SimGuard.Api.Email;

// The three emails of the access request flow. Values are HTML encoded in
// the HTML part; the text part is the same message in plain text.
public static class EmailTemplates
{
    public static EmailMessage RequestReceived(string name) => Build(
        "We received your SimGuard request",
        name,
        ["We received your request for a SimGuard account.",
         "An administrator will review it and email you when it is done."]);

    public static EmailMessage AccountReady(string name, string email, string temporaryPassword, string signInUrl) => Build(
        "Your SimGuard account is ready",
        name,
        ["Your SimGuard account is ready.",
         $"Sign in at: {signInUrl}",
         $"Email: {email}",
         $"Temporary password: {temporaryPassword}",
         "You will be asked to choose a new password and set up two-step sign-in the first time."]);

    public static EmailMessage RequestDeclined(string name) => Build(
        "About your SimGuard request",
        name,
        ["We could not approve your request for a SimGuard account.",
         "If you think this is a mistake, reply to the person who invited you or contact your administrator."]);

    private static EmailMessage Build(string subject, string name, string[] lines)
    {
        var text = $"Hello {name},\n\n{string.Join("\n\n", lines)}\n\nSimGuard";
        var html = $"<p>Hello {WebUtility.HtmlEncode(name)},</p>"
            + string.Concat(lines.Select(line => $"<p>{WebUtility.HtmlEncode(line)}</p>"))
            + "<p>SimGuard</p>";
        return new EmailMessage(subject, text, html);
    }
}
