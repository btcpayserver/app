using System.Net;
using BTCPayApp.Core.Auth;
using VSS;
using VSSProto;

namespace BTCPayApp.Core.Backup;

public class AccountAwareVssClient : IVSSAPI
{
    private readonly IVSSAPI _inner;
    private readonly IAccountManager _accountManager;

    public AccountAwareVssClient(IVSSAPI inner, IAccountManager accountManager)
    {
        _inner = inner;
        _accountManager = accountManager;
    }

    private async Task<T> Wrap<T>(Func<Task<T>> func)
    {
        var retry = false;
        attemptAgain:
        try
        {
            return await func();
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized && !retry)
        {
            await _accountManager.RefreshAccess();
            retry = true;
            goto attemptAgain;
        }
    }

    public async Task<GetObjectResponse> GetObjectAsync(GetObjectRequest request,
        CancellationToken cancellationToken = default)
    {
        return await Wrap(async () => await _inner.GetObjectAsync(request, cancellationToken));
    }

    public async Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request,
        CancellationToken cancellationToken = default)
    {
        return await Wrap(async () => await _inner.PutObjectAsync(request, cancellationToken));
    }

    public async Task<DeleteObjectResponse> DeleteObjectAsync(DeleteObjectRequest request,
        CancellationToken cancellationToken = default)
    {
        return await Wrap(async () => await _inner.DeleteObjectAsync(request, cancellationToken));
    }

    public async Task<ListKeyVersionsResponse> ListKeyVersionsAsync(ListKeyVersionsRequest request,
        CancellationToken cancellationToken = default)
    {
        return await Wrap(async () => await _inner.ListKeyVersionsAsync(request, cancellationToken));
    }
}