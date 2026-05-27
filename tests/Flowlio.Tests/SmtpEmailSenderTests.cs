using Flowlio.Application.Abstractions;
using Flowlio.Infrastructure.Email;
using MimeKit;
using MimeKit.Text;
using Xunit;

namespace Flowlio.Tests;

public class SmtpEmailSenderTests
{
    private static readonly SmtpOptions Options = new()
    {
        FromAddress = "no-reply@flowlio.local",
        FromName = "Flowlio",
    };

    [Fact]
    public void BuildMimeMessage_sets_sender_recipient_and_subject()
    {
        var mime = SmtpEmailSender.BuildMimeMessage(Options, new EmailMessage
        {
            ToEmail = "clen@example.com",
            ToName = "Jan Novák",
            Subject = "Pozvánka",
            HtmlBody = "<p>Ahoj</p>",
        });

        var from = Assert.IsType<MailboxAddress>(Assert.Single(mime.From));
        Assert.Equal("no-reply@flowlio.local", from.Address);
        Assert.Equal("Flowlio", from.Name);

        var to = Assert.IsType<MailboxAddress>(Assert.Single(mime.To));
        Assert.Equal("clen@example.com", to.Address);
        Assert.Equal("Jan Novák", to.Name);

        Assert.Equal("Pozvánka", mime.Subject);
        Assert.Equal("<p>Ahoj</p>", mime.HtmlBody);
    }

    [Fact]
    public void BuildMimeMessage_falls_back_to_address_when_name_missing()
    {
        var mime = SmtpEmailSender.BuildMimeMessage(Options, new EmailMessage
        {
            ToEmail = "clen@example.com",
            Subject = "Test",
            HtmlBody = "<p>x</p>",
        });

        var to = Assert.IsType<MailboxAddress>(Assert.Single(mime.To));
        Assert.Equal("clen@example.com", to.Name);
    }

    [Fact]
    public void BuildMimeMessage_includes_plain_text_alternative_when_provided()
    {
        var mime = SmtpEmailSender.BuildMimeMessage(Options, new EmailMessage
        {
            ToEmail = "clen@example.com",
            Subject = "Test",
            HtmlBody = "<p>x</p>",
            TextBody = "x",
        });

        Assert.Equal("x", mime.GetTextBody(TextFormat.Plain));
    }
}
