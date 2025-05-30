﻿using BTCPayApp.Core.Contracts;

namespace BTCPayApp.Maui.Services
{
    public class EmailService : IEmailService
    {
        public async Task SendAsync(string subject, string body, string recipient, string? attachFilePath = null)
        {
            if (Email.Default.IsComposeSupported)
            {
                var message = new EmailMessage
                {
                    Subject = subject, //"App Log File",
                    Body = body, //"Attached is the log file for review.",
                    BodyFormat = EmailBodyFormat.PlainText,
                    To = [recipient]
                };

                if (!string.IsNullOrWhiteSpace(attachFilePath))
                {
                    message.Attachments?.Add(new EmailAttachment(attachFilePath));
                }

                await Email.Default.ComposeAsync(message);
            }
            else
            {
                throw new NotSupportedException("Email sending is not supported on this device.");
            }
        }
    }
}
