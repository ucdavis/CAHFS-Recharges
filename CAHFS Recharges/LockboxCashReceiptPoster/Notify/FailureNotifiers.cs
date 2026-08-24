using LockboxCashReceiptPoster.Abstractions;
using LockboxCashReceiptPoster.Options;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LockboxCashReceiptPoster.Notify
{
    /// v1 default — logging only. Flip Email:Enabled later to send SMTP mail.
    public sealed class NoOpFailureNotifier : IFailureNotifier
    {
        private readonly ILogger<NoOpFailureNotifier> _logger;

        public NoOpFailureNotifier(ILogger<NoOpFailureNotifier> logger)
        {
            _logger = logger;
        }

        public Task NotifyAsync(
            string company,
            int postedCount,
            int failedCount,
            IReadOnlyList<string> errors,
            CancellationToken cancellationToken = default)
        {
            if (failedCount <= 0)
                return Task.CompletedTask;

            _logger.LogWarning(
                "Cash receipt run company={Company} posted={Posted} failed={Failed}. Email disabled (Email:Enabled=false).",
                company, postedCount, failedCount);

            foreach (var error in errors)
                _logger.LogWarning("Failure detail: {Error}", error);

            return Task.CompletedTask;
        }
    }

    /// Optional SMTP notifier (same recipients as IM After Integration). Used when Email:Enabled=true.
    public sealed class SmtpFailureNotifier : IFailureNotifier
    {
        private readonly EmailOptions _email;
        private readonly ILogger<SmtpFailureNotifier> _logger;

        public SmtpFailureNotifier(CashReceiptPosterOptions options, ILogger<SmtpFailureNotifier> logger)
        {
            _email = options.Email;
            _logger = logger;
        }

        public Task NotifyAsync(
            string company,
            int postedCount,
            int failedCount,
            IReadOnlyList<string> errors,
            CancellationToken cancellationToken = default)
        {
            if (failedCount <= 0 || !_email.Enabled)
                return Task.CompletedTask;

            try
            {
                using (var client = new System.Net.Mail.SmtpClient(_email.SmtpHost, _email.SmtpPort))
                using (var message = new System.Net.Mail.MailMessage())
                {
                    message.From = new System.Net.Mail.MailAddress(_email.From);
                    foreach (var to in _email.To.Split(new[] { ';', ',' }, System.StringSplitOptions.RemoveEmptyEntries))
                        message.To.Add(to.Trim());

                    message.Subject = $"LockboxCashReceiptPoster {company} Errors";
                    message.Body =
                        $"Company: {company}\r\nPosted: {postedCount}\r\nFailed: {failedCount}\r\n\r\n" +
                        string.Join("\r\n", errors);

                    client.Send(message);
                }
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Failed to send failure email for company={Company}", company);
            }

            return Task.CompletedTask;
        }
    }
}
