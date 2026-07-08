using System;
using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using Microsoft.Extensions.Logging;

namespace EEMOCantilanSDS.Infrastructure.Services
{
    /// <summary>
    /// SMTP email sender (System.Net.Mail). Safe no-op when unconfigured, and best-effort otherwise: a send
    /// failure is logged and returns false rather than throwing, so callers (e.g. approval) never fail on email.
    /// </summary>
    public class SmtpEmailSender(EmailOptions options, ILogger<SmtpEmailSender> logger) : IEmailSender
    {
        public async Task<bool> SendAsync(string toEmail, string? toName, string subject, string body, CancellationToken ct = default)
        {
            if (!options.IsConfigured)
            {
                logger.LogWarning("Email is not configured; skipping send to {To}.", toEmail);
                return false;
            }

            try
            {
                using var client = new SmtpClient(options.Host, options.Port)
                {
                    EnableSsl = options.UseStartTls,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Credentials = new NetworkCredential(options.Username, options.Password),
                };

                using var message = new MailMessage
                {
                    From = new MailAddress(options.FromEmail, options.FromName),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = false,
                };
                message.To.Add(new MailAddress(toEmail, string.IsNullOrWhiteSpace(toName) ? toEmail : toName));

                await client.SendMailAsync(message, ct);
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send email to {To}.", toEmail);
                return false;
            }
        }
    }
}
