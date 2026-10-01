namespace PassManager.Infrastructure.Email;

public class EmailOptions
{
    public string Provider { get; set; } = "Console";
    public SmtpOptions Smtp { get; set; } = new();

    public class SmtpOptions
    {
        public string Host { get; set; } = "";
        public int Port { get; set; } = 587;
        public string User { get; set; } = "";
        public string Password { get; set; } = "";
        public string From { get; set; } = "no-reply@passmanager.local";
    }
}
