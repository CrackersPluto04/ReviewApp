using System.Net;
using System.Text;

namespace ReviewApp.Api.Services;

// One shared layout for every email: HTML with inline styles (mail clients ignore <style> blocks) plus a plain-text alternative.
// Every dynamic value is HTML-encoded, usernames and addresses are user input.
public static class EmailTemplates
{
    private const string AppName = "THE Review App";

    public record Button(string Text, string Url);

    public static (string Html, string Text) Build(string heading, IEnumerable<string> paragraphs, Button? button = null, string? code = null, string? footnote = null)
    {
        var paragraphList = paragraphs.ToList();

        var html = new StringBuilder();
        html.Append("<!doctype html><html><body style=\"margin:0;padding:0;background:#f4f4f5;font-family:Arial,Helvetica,sans-serif;color:#18181b;\">");
        html.Append("<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\"><tr><td align=\"center\" style=\"padding:24px 12px;\">");
        html.Append("<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" style=\"max-width:560px;background:#ffffff;border-radius:8px;\">");
        html.Append($"<tr><td style=\"padding:20px 28px;border-bottom:1px solid #e4e4e7;font-size:18px;font-weight:bold;\">{Encode(AppName)}</td></tr>");
        html.Append("<tr><td style=\"padding:28px;\">");
        html.Append($"<h1 style=\"margin:0 0 16px;font-size:22px;\">{Encode(heading)}</h1>");

        foreach (var paragraph in paragraphList)
            html.Append($"<p style=\"margin:0 0 14px;font-size:15px;line-height:1.5;\">{Encode(paragraph)}</p>");

        if (code != null)
            html.Append($"<p style=\"margin:20px 0;font-size:32px;font-weight:bold;letter-spacing:8px;text-align:center;\">{Encode(code)}</p>");

        if (button != null)
        {
            html.Append($"<p style=\"margin:24px 0;text-align:center;\"><a href=\"{Encode(button.Url)}\" style=\"display:inline-block;padding:12px 24px;background:#1976d2;color:#ffffff;text-decoration:none;border-radius:6px;font-weight:bold;\">{Encode(button.Text)}</a></p>");
            html.Append($"<p style=\"margin:0 0 14px;font-size:13px;color:#52525b;word-break:break-all;\">If the button doesn't work, copy this link into your browser:<br>{Encode(button.Url)}</p>");
        }

        if (footnote != null)
            html.Append($"<p style=\"margin:20px 0 0;font-size:13px;color:#52525b;\">{Encode(footnote)}</p>");

        html.Append("</td></tr>");
        html.Append($"<tr><td style=\"padding:16px 28px;border-top:1px solid #e4e4e7;font-size:12px;color:#71717a;\">This is an automated message from {Encode(AppName)}, please don't reply to it.</td></tr>");
        html.Append("</table></td></tr></table></body></html>");

        var text = new StringBuilder();
        text.AppendLine(heading).AppendLine();
        foreach (var paragraph in paragraphList)
            text.AppendLine(paragraph).AppendLine();
        if (code != null)
            text.AppendLine(code).AppendLine();
        if (button != null)
            text.AppendLine($"{button.Text}: {button.Url}").AppendLine();
        if (footnote != null)
            text.AppendLine(footnote).AppendLine();
        text.Append($"This is an automated message from {AppName}, please don't reply to it.");

        return (html.ToString(), text.ToString());
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
