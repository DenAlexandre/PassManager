using Microsoft.Extensions.Options;
using MimeKit;
using PassManager.Domain.Abstractions;

namespace PassManager.Infrastructure.Email;

public class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    private readonly EmailOptions.SmtpOptions _smtp = options.Value.Smtp;

    public async Task SendConfirmationEmailAsync(string toEmail, string confirmationLink, CancellationToken ct = default)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_smtp.From));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = "Confirmez votre compte PassManager";
        message.Body = new TextPart("plain")
        {
            Text = $"Bienvenue sur PassManager.\n\nConfirmez votre compte en suivant ce lien :\n{confirmationLink}\n\nCe lien expire dans 24 heures."
        };

        using var client = new MailKit.Net.Smtp.SmtpClient();
        await client.ConnectAsync(_smtp.Host, _smtp.Port, MailKit.Security.SecureSocketOptions.StartTls, ct);
        if (!string.IsNullOrEmpty(_smtp.User))
        {
            await client.AuthenticateAsync(_smtp.User, _smtp.Password, ct);
        }
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
