using System.Net;

namespace BTCPayApp.Core;

public class BTCPayAppClientException(HttpStatusCode statusCode, string message) : Exception
{
    public HttpStatusCode StatusCode { get; init; } = statusCode;
    public override string Message => message;
}
