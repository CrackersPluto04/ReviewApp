using ReviewApp.Api.Services;

namespace ReviewApp.Api.Tests.Unit;

public class EmailTemplatesTests
{
    [Fact]
    public void Build_PutsEveryPartIntoBothBodies()
    {
        var (html, text) = EmailTemplates.Build("Heading", ["First paragraph.", "Second paragraph."],
            new EmailTemplates.Button("Click me", "https://app.test/path?token=abc"), "123456", "A footnote.");

        foreach (var part in new[] { "Heading", "First paragraph.", "Second paragraph.", "123456", "Click me", "A footnote." })
        {
            html.ShouldContain(part);
            text.ShouldContain(part);
        }
        html.ShouldContain("href=\"https://app.test/path?token=abc\"");
        text.ShouldContain("Click me: https://app.test/path?token=abc");
    }

    [Fact]
    public void Build_HtmlEncodesEveryDynamicValue()
    {
        const string evil = "<script>alert('x')</script>";

        var (html, _) = EmailTemplates.Build(evil, [evil], new EmailTemplates.Button(evil, "https://x.test/?a=1&b=\"2\""), evil, evil);

        html.ShouldNotContain("<script>");
        html.ShouldContain("&lt;script&gt;");
        html.ShouldContain("href=\"https://x.test/?a=1&amp;b=&quot;2&quot;\"");
    }

    [Fact]
    public void Build_TextBodyStaysPlain()
    {
        var (_, text) = EmailTemplates.Build("Tom & Jerry", ["5 < 6"]);

        text.ShouldContain("Tom & Jerry");
        text.ShouldContain("5 < 6");
        text.ShouldNotContain("&amp;");
        text.ShouldNotContain("<p");
    }

    [Fact]
    public void Build_WithoutOptionalParts_OmitsThem()
    {
        var (html, text) = EmailTemplates.Build("Heading", ["Only text."]);

        html.ShouldNotContain("<a ");
        html.ShouldNotContain("letter-spacing:8px");
        text.ShouldNotContain("http");
        text.ShouldEndWith("This is an automated message from THE Review App, please don't reply to it.");
    }
}
