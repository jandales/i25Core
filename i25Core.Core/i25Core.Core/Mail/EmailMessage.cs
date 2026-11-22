namespace i25Core.Core.Mail
{
    /// <summary>
    /// An email to send. Plain data (JSON-serializable), so it can be queued as a background job.
    /// </summary>
    public class EmailMessage
    {
        public List<EmailAddress> To { get; set; } = new();
        public List<EmailAddress> Cc { get; set; } = new();
        public List<EmailAddress> Bcc { get; set; } = new();

        /// <summary>Overrides Mail:FromAddress / Mail:FromName for this message.</summary>
        public EmailAddress? From { get; set; }

        public EmailAddress? ReplyTo { get; set; }

        public string Subject { get; set; } = string.Empty;

        /// <summary>HTML body. Send at least one of HtmlBody / TextBody; both makes a multipart message.</summary>
        public string? HtmlBody { get; set; }

        public string? TextBody { get; set; }

        /// <summary>Attachments are stored as bytes. When queued they are stored in the job table, so keep them small.</summary>
        public List<EmailAttachment> Attachments { get; set; } = new();

        /// <summary>Convenience: new EmailMessage().AddTo("a@b.com").</summary>
        public EmailMessage AddTo(string address, string? name = null)
        {
            To.Add(new EmailAddress(address, name));
            return this;
        }
    }

    public class EmailAddress
    {
        public string Address { get; set; } = string.Empty;
        public string? Name { get; set; }

        public EmailAddress()
        {
        }

        public EmailAddress(string address, string? name = null)
        {
            Address = address;
            Name = name;
        }
    }

    public class EmailAttachment
    {
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = "application/octet-stream";
        public byte[] Content { get; set; } = Array.Empty<byte>();
    }
}
