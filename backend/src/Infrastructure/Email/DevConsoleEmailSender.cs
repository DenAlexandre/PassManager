using Microsoft.Extensions.Logging;
using PassManager.Domain.Abstractions;

namespace PassManager.Infrastructure.Email;

public class DevConsoleEmailSender(ILogger<DevConsoleEmailSender> logger) : IEmailSender
{
    public Task SendConfirmationEmailAsync(string toEmail, string confirmationLink, CancellationToken ct = default)
    {
        logger.LogInformation(
            "[DEV EMAIL] Confirmation pour {Email} : {Link}", toEmail, confirmationLink);
        return Task.CompletedTask;
    }
}
