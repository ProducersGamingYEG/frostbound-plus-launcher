using System.Net;
using System.Net.Mail;
public interface ICodeMailer { Task Send(string email, string code, string purpose); }
public sealed class SmtpCodeMailer(IConfiguration config) : ICodeMailer
{
    public async Task Send(string email, string code, string purpose)
    {
        using var smtp = new SmtpClient(config["SMTP_HOST"] ?? "smtp.gmail.com", int.Parse(config["SMTP_PORT"] ?? "587")) { EnableSsl = true, Credentials = new NetworkCredential(config["SMTP_USERNAME"], config["SMTP_PASSWORD"]), Timeout = 15000 };
        using var message = new MailMessage(config["SMTP_FROM"] ?? config["SMTP_USERNAME"]!, email) { Subject = "Frostbound Plus verification code", Body = $"Your Frostbound Plus {purpose} code is {code}. It expires in 10 minutes. If you did not request this, ignore this email." };
        await smtp.SendMailAsync(message);
    }
}
