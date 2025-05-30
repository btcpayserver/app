using BTCPayApp.Core.Contracts;
using Microsoft.JSInterop;

namespace BTCPayApp.Desktop.Services;

public class EmailService(IJSRuntime jSRuntime) : IEmailService
{
    public async Task SendAsync(string subject, string body, string recipient, string? attachFilePath = null)
    {
        await jSRuntime.InvokeVoidAsync("Interop.sendEmail", subject, body, recipient);
    }
}
