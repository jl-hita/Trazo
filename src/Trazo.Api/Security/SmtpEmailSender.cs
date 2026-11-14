using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Trazo.Infrastructure.Settings;
namespace Trazo.Api.Security;

public interface ISmtpTransport
{
    Task SendAsync(EmailConfig config, string recipient, string subject, string body, CancellationToken cancellationToken);
}

/// <summary>
/// MailKit transport. Unlike System.Net.Mail.SmtpClient it reports authentication failures
/// (for example "535 Authentication failed") instead of continuing unauthenticated, and it
/// supports PLAIN, LOGIN, CRAM-MD5, STARTTLS and implicit TLS (port 465).
/// </summary>
public sealed class SystemNetSmtpTransport : ISmtpTransport
{
    public async Task SendAsync(EmailConfig config, string recipient, string subject, string body, CancellationToken cancellationToken)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(config.From));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        // Port 465 uses implicit TLS; other ports upgrade with STARTTLS when SSL is enabled.
        var security = !config.EnableSsl ? SecureSocketOptions.None
            : config.Port == 465 ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTls;

        using var client = new SmtpClient { Timeout = 15000 };
        await client.ConnectAsync(config.Host, config.Port, security, cancellationToken);
        if (!string.IsNullOrWhiteSpace(config.Username))
            await client.AuthenticateAsync(config.Username, config.Password ?? "", cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}

/// <summary>Sends transactional email using the effective (database, then environment) SMTP settings, read on every send.</summary>
public sealed class SmtpEmailSender(
    IRuntimeSettings settings, ISmtpTransport transport, IEmailDeliveryRecorder? recorder = null) : IEmailSender
{
    public async Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
    {
        var config = await settings.GetEmailAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(config.Host) || string.IsNullOrWhiteSpace(config.From))
            throw new InvalidOperationException("Transactional email is not configured.");
        try
        {
            await transport.SendAsync(config, recipient, subject, body, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (recorder is not null) await recorder.RecordAsync(exception, CancellationToken.None);
            throw;
        }
        if (recorder is not null) await recorder.RecordAsync(null, CancellationToken.None);
    }
}
