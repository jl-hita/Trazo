using System.Net;
using System.Net.Mail;
using Trazo.Infrastructure.Settings;
namespace Trazo.Api.Security;

public interface ISmtpTransport
{
    Task SendAsync(EmailConfig config, string recipient, string subject, string body, CancellationToken cancellationToken);
}

public sealed class SystemNetSmtpTransport : ISmtpTransport
{
    public async Task SendAsync(EmailConfig config, string recipient, string subject, string body, CancellationToken cancellationToken)
    {
        using var message = new MailMessage(config.From, recipient, subject, body) { IsBodyHtml = false };
        using var client = new SmtpClient(config.Host, config.Port) { EnableSsl = config.EnableSsl, Timeout = 15000 };
        if (!string.IsNullOrWhiteSpace(config.Username))
            client.Credentials = new NetworkCredential(config.Username, config.Password);
        await client.SendMailAsync(message, cancellationToken);
    }
}

/// <summary>Sends transactional email using the effective (database, then environment) SMTP settings, read on every send.</summary>
public sealed class SmtpEmailSender(IRuntimeSettings settings, ISmtpTransport transport) : IEmailSender
{
    public async Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
    {
        var config = await settings.GetEmailAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(config.Host) || string.IsNullOrWhiteSpace(config.From))
            throw new InvalidOperationException("Transactional email is not configured.");
        await transport.SendAsync(config, recipient, subject, body, cancellationToken);
    }
}
