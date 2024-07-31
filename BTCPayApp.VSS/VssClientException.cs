using VSSProto;

namespace BTCPayApp.VSS;

public class VssClientException : Exception
{
    public ErrorResponse Error { get; }

    public VssClientException(ErrorResponse error) : base($"{error.ErrorCode} {error.Message}")
    {
        Error = error;
    }
}