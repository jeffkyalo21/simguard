using System.Text.RegularExpressions;

namespace SimGuard.Api.Common;

// Phone numbers are stored in E.164 form (+254712345678) so that the same
// number written two ways cannot become two SIMs, and so that usage records
// from the telecom side can be matched on an exact value.
public static class PhoneNumbers
{
    private static readonly Regex KenyaE164 = new("^\\+254[17][0-9]{8}$", RegexOptions.Compiled);
    private static readonly Regex OtherE164 = new("^\\+[1-9][0-9]{7,14}$", RegexOptions.Compiled);

    // Accepts 0712345678, 0112345678, 254712345678, +254712345678, 00254712345678
    // (spaces, dashes, dots and brackets are ignored) and any other valid
    // international number that starts with "+" or "00". Returns null if invalid.
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var compact = Compact(input);
        string candidate;

        if (compact.StartsWith("+"))
        {
            candidate = compact;
        }
        else if (compact.StartsWith("00"))
        {
            candidate = "+" + compact[2..];
        }
        else if (compact.StartsWith("254"))
        {
            candidate = "+" + compact;
        }
        else if (compact.StartsWith("0"))
        {
            candidate = "+254" + compact[1..];
        }
        else
        {
            return null;
        }

        if (candidate.StartsWith("+254"))
        {
            return KenyaE164.IsMatch(candidate) ? candidate : null;
        }

        return OtherE164.IsMatch(candidate) ? candidate : null;
    }

    // Lets a partial search such as "0712" find "+254712...".
    public static string? ToSearchTerm(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var compact = Compact(input);

        if (compact.StartsWith("00"))
        {
            return "+" + compact[2..];
        }

        if (compact.StartsWith("0"))
        {
            return "+254" + compact[1..];
        }

        if (compact.StartsWith("254"))
        {
            return "+" + compact;
        }

        return compact;
    }

    private static string Compact(string input) =>
        new(input.Where(c => c is not (' ' or '-' or '.' or '(' or ')')).ToArray());
}
