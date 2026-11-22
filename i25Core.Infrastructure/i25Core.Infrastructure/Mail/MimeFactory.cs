using i25Core.Core.Mail;
using MimeKit;

namespace i25Core.Infrastructure.Mail
{
    /// <summary>Turns an EmailMessage into a MimeKit message (shared by the SMTP and Log drivers).</summary>
    internal static class MimeFactory
    {
        public static MimeMessage Create(EmailMessage message, MailOptions options)
        {
            if (message.To.Count == 0 && message.Cc.Count == 0 && message.Bcc.Count == 0)
                throw new ArgumentException("An email needs at least one recipient (To, Cc or Bcc).");

            if (string.IsNullOrWhiteSpace(message.HtmlBody) && string.IsNullOrWhiteSpace(message.TextBody))
                throw new ArgumentException("An email needs an HtmlBody or a TextBody.");

            var from = message.From ?? new EmailAddress(options.FromAddress, options.FromName);

            var mime = new MimeMessage();
            mime.From.Add(Convert(from));
            mime.To.AddRange(message.To.Select(Convert));
            mime.Cc.AddRange(message.Cc.Select(Convert));
            mime.Bcc.AddRange(message.Bcc.Select(Convert));

            if (message.ReplyTo != null)
                mime.ReplyTo.Add(Convert(message.ReplyTo));

            mime.Subject = message.Subject;

            var body = new BodyBuilder
            {
                HtmlBody = message.HtmlBody,
                TextBody = message.TextBody
            };

            foreach (var attachment in message.Attachments)
                body.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));

            mime.Body = body.ToMessageBody();

            return mime;
        }

        private static MailboxAddress Convert(EmailAddress address)
        {
            if (string.IsNullOrWhiteSpace(address.Address))
                throw new ArgumentException("An email address is empty.");

            // MailboxAddress.Parse rejects malformed addresses with a clear FormatException.
            var mailbox = MailboxAddress.Parse(address.Address.Trim());

            if (!string.IsNullOrWhiteSpace(address.Name))
                mailbox.Name = address.Name;

            return mailbox;
        }
    }
}
