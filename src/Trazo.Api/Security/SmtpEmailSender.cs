using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Trazo.Api.Support;
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
    IRuntimeSettings settings, ISmtpTransport transport, IEmailDeliveryRecorder? recorder = null,
    ILogger<SmtpEmailSender>? logger = null) : IEmailSender
{
    public async Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
    {
        var config = await settings.GetEmailAsync(cancellationToken);
        if (!config.Enabled)
        {
            // Email switched off in the settings (test installations): drop the message without touching the SMTP server.
            logger?.LogInformation("Email is disabled in the settings; a message was not sent.");
            return;
        }
        if (string.IsNullOrWhiteSpace(config.Host) || string.IsNullOrWhiteSpace(config.From))
            throw new InvalidOperationException("Transactional email is not configured.").AsExpected(); // shown by the overview; not a defect
        try
        {
            await transport.SendAsync(config, recipient, subject, body, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (recorder is not null) await recorder.RecordAsync(exception, CancellationToken.None);
            // The mail server did not answer or refused us: an operational condition (the overview shows it), not a defect to ticket.
            if (IsDeliveryFailure(exception)) exception.AsExpected();
            throw;
        }
        if (recorder is not null) await recorder.RecordAsync(null, CancellationToken.None);
    }

    /// <summary>
    /// Failures of reaching or being accepted by the mail server (connection, TLS, authentication, SMTP refusals, timeouts). Anything else
    /// that goes wrong while sending (a malformed address, a bug) is not in this list and stays an unexpected error.
    /// </summary>
    internal static bool IsDeliveryFailure(Exception exception) => exception is
        MailKit.ProtocolException or MailKit.CommandException or MailKit.ServiceNotConnectedException or MailKit.ServiceNotAuthenticatedException
        or MailKit.Security.AuthenticationException or MailKit.Security.SslHandshakeException
        or System.Security.Authentication.AuthenticationException
        or System.Net.Sockets.SocketException or IOException or TimeoutException;
}
