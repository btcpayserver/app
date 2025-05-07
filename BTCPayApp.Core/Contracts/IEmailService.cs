namespace BTCPayApp.Core.Contracts
{
    public interface IEmailService
    {
        Task SendAsync(string subject, string body, string? recipient = null, string? attachFilePath = null);
    }
}
