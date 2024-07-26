using Google.Protobuf;
using Microsoft.AspNetCore.DataProtection;
using VSSProto;

namespace BTCPayApp.VSS;


public class VSSApiEncryptorClient: IVSSAPI
{
    private readonly IVSSAPI _vssApi;
    private readonly IDataProtector _encryptor;

    public VSSApiEncryptorClient(IVSSAPI vssApi, IDataProtector encryptor)
    {
        _vssApi = vssApi;
        _encryptor = encryptor;
    }

    public async Task<GetObjectResponse> GetObjectAsync(GetObjectRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _vssApi.GetObjectAsync(request, cancellationToken);
        if (response.Value?.Value is null)
        {
            return response;
        }

        var decryptedValue = _encryptor.Unprotect(response.Value.Value.ToByteArray());
        response.Value.Value = ByteString.CopyFrom(decryptedValue);
        return response;
    }

    public async Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, CancellationToken cancellationToken = default)
    {
        var newReq = request.Clone();
        foreach (var obj in newReq.TransactionItems) 
        {
            if(obj is null)
                continue;
            var encryptedValue = _encryptor.Protect(obj.Value.ToByteArray());
            obj.Value = ByteString.CopyFrom(encryptedValue);
        }
        return await _vssApi.PutObjectAsync(newReq, cancellationToken);
      
    }

    public Task<DeleteObjectResponse> DeleteObjectAsync(DeleteObjectRequest request, CancellationToken cancellationToken = default)
    {
        return _vssApi.DeleteObjectAsync(request, cancellationToken);
    }

    public async Task<ListKeyVersionsResponse> ListKeyVersionsAsync(ListKeyVersionsRequest request, CancellationToken cancellationToken = default)
    {
        
        var x = await  _vssApi.ListKeyVersionsAsync(request, cancellationToken);
        
        foreach (var keyVersion in x.KeyVersions)
        {
            if (keyVersion.Value is null)
            {
                continue;
            }

            var decryptedValue = _encryptor.Unprotect(keyVersion.Value.ToByteArray());
            keyVersion.Value = ByteString.CopyFrom(decryptedValue);
        }
        return x;
    }
}