namespace BTCPayApp.Core;

public class BTCPayAppClientException(int statusCode, string message) : Exception
{
    public int StatusCode { get; init; } = statusCode;
    public override string Message => message;
}
