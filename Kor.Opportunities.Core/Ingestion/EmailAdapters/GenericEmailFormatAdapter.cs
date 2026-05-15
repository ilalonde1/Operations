#nullable enable
using System;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kor.Opportunities.Core.Ingestion.EmailAdapters;

public sealed partial class GenericEmailFormatAdapter : IEmailFormatAdapter
{
    public string AdapterName => "Generic";

    public bool CanHandle(string senderAddress) => false;

    public OpportunityCandidate? Parse(EmailMessage message)
    {
        var bodyText = CleanBodyText(message.BodyHtmlOrPlain);
        var lineBodyText = CleanBodyTextPreservingLineBreaks(message.BodyHtmlOrPlain);
        var url = ExtractFirstHttpsUrl(message.BodyHtmlOrPlain) ?? ExtractFirstHttpsUrl(bodyText);

        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var title = ResolveTitle(message.Subject, lineBodyText);
        var company = ParseCompany(message.Subject, lineBodyText);

        return new OpportunityCandidate
        {
            Title = title,
            Buyer = string.IsNullOrWhiteSpace(company) ? "Unknown" : company.Trim(),
            Url = url,
            Description = CreateSnippet(bodyText, 500),
            PostedDateUtc = message.ReceivedUtc,
            ExternalReference = message.MessageId,
            RawJson = JsonSerializer.Serialize(message),
        };
    }

    private static string ResolveTitle(string? subject, string lineBodyText)
    {
        if (!string.IsNullOrWhiteSpace(subject))
        {
            return subject.Trim();
        }

        var firstLine = FirstLineRegex().Match(lineBodyText);
        if (firstLine.Success && !string.IsNullOrWhiteSpace(firstLine.Value))
        {
            return firstLine.Value.Trim();
        }

        return "(No Subject)";
    }

    private static string? ParseCompany(string? subject, string bodyText)
    {
        var bodyMatch = CompanyRegex().Match(bodyText);
        if (bodyMatch.Success)
        {
            var company = bodyMatch.Groups["company"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(company))
            {
                return company;
            }
        }

        if (!string.IsNullOrWhiteSpace(subject))
        {
            var subjectMatch = CompanyRegex().Match(subject);
            if (subjectMatch.Success)
            {
                var company = subjectMatch.Groups["company"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(company))
                {
                    return company;
                }
            }
        }

        return ParseCompanyFromSubjectFallback(subject);
    }

    private static string? ParseCompanyFromSubjectFallback(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        var normalized = subject.Trim();

        var separators = new[] { " - ", " | ", " @ ", ":" };
        foreach (var separator in separators)
        {
            var index = normalized.IndexOf(separator, StringComparison.Ordinal);
            if (index <= 0)
            {
                continue;
            }

            var candidate = normalized[..index].Trim();
            if (!string.IsNullOrWhiteSpace(candidate) && candidate.Length <= 120)
            {
                return candidate;
            }
        }

        return null;
    }

    private static string CreateSnippet(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string CleanBodyText(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        var withoutTags = HtmlTagRegex().Replace(body, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return MultiWhitespaceRegex().Replace(decoded, " ").Trim();
    }

    private static string CleanBodyTextPreservingLineBreaks(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        var withoutTags = HtmlTagRegex().Replace(body, "\n");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        var lines = decoded
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return string.Join("\n", lines);
    }

    private static string? ExtractFirstHttpsUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (Match match in HttpsUrlRegex().Matches(text))
        {
            if (!match.Success)
            {
                continue;
            }

            var candidate = match.Value.Trim().TrimEnd('.', ',', ';', ':', ')', ']', '}');
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            {
                continue;
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return uri.AbsoluteUri;
        }

        return null;
    }

    [GeneratedRegex("https://[^\\s\"'<>\\)]+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex HttpsUrlRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("\\s+", RegexOptions.Compiled)]
    private static partial Regex MultiWhitespaceRegex();

    [GeneratedRegex("(?im)\\bCompany\\s*:\\s*(?<company>[^\\r\\n]+)", RegexOptions.Compiled)]
    private static partial Regex CompanyRegex();

    [GeneratedRegex("^[^\\r\\n]+", RegexOptions.Compiled)]
    private static partial Regex FirstLineRegex();
}
