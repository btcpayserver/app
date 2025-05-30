namespace BTCPayApp.Core.Contracts
{
    public interface IEmailService
    {
        Task SendAsync(string subject, string body, string recipient, string? attachFilePath = null);
    }
}
