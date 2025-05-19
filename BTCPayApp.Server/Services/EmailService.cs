using BTCPayApp.Core.Contracts;
using Microsoft.JSInterop;

namespace BTCPayApp.Server.Services
{
    public class EmailService : IEmailService
    {
        private readonly IJSRuntime jSRuntime;

        public EmailService(IJSRuntime jSRuntime)
        {
            this.jSRuntime = jSRuntime;
        }
        public async Task SendAsync(string subject, string body, string? recipient = null, string? attachFilePath = null)
        {
            await jSRuntime.InvokeVoidAsync("Interop.sendEmail", subject, body, recipient);
        }
    }
}
