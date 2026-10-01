namespace PassManager.Domain.Abstractions;

public interface IEmailSender
{
    Task SendConfirmationEmailAsync(string toEmail, string confirmationLink, CancellationToken ct = default);
}
