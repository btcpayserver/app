using Google.Protobuf;
using Microsoft.AspNetCore.DataProtection;
using VSSProto;

namespace BTCPayApp.VSS;

using System.Threading.Tasks;




public class VSSApiEncryptorClient: IVSSAPI
{
    private readonly IVSSAPI _vssApi;
    private readonly IDataProtector _encryptor;

    public VSSApiEncryptorClient(IVSSAPI vssApi, IDataProtector encryptor)
    {
        _vssApi = vssApi;
        _encryptor = encryptor;
        _encryptor = encryptor;
    }

    public async Task<GetObjectResponse> GetObjectAsync(GetObjectRequest request)
    {
        var response = await _vssApi.GetObjectAsync(request);
        if (response.Value?.Value is null)
        {
            return response;
        }

        var decryptedValue = _encryptor.Unprotect(response.Value.Value.ToByteArray());
        response.Value.Value = ByteString.CopyFrom(decryptedValue);
        return response;
    }

    public async Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request)
    {
        var newReq = request.Clone();
      foreach (var obj in newReq.TransactionItems) 
      {
          if(obj is null)
              continue;
          var encryptedValue = _encryptor.Protect(obj.Value.ToByteArray());
          obj.Value = ByteString.CopyFrom(encryptedValue);
      }
      return await _vssApi.PutObjectAsync(newReq);
      
    }

    public Task<DeleteObjectResponse> DeleteObjectAsync(DeleteObjectRequest request)
    {
        return _vssApi.DeleteObjectAsync(request);
    }

    public async Task<ListKeyVersionsResponse> ListKeyVersionsAsync(ListKeyVersionsRequest request)
    {
        
        var x = await  _vssApi.ListKeyVersionsAsync(request);
        
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
public class HttpVSSAPIClient : IVSSAPI
{
    private readonly Uri _endpoint;
    private readonly HttpClient _httpClient;

    private const string GET_OBJECT = "getObject";
    private const string PUT_OBJECTS = "putObjects";
    private const string DELETE_OBJECT = "deleteObject";
    private const string LIST_KEY_VERSIONS = "listKeyVersions";
    public HttpVSSAPIClient(Uri endpoint, HttpClient? httpClient = null)
    {
        _endpoint = endpoint;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<GetObjectResponse> GetObjectAsync(GetObjectRequest request)
    {
        var url = new Uri(_endpoint, GET_OBJECT);
        return await SendRequestAsync<GetObjectRequest, GetObjectResponse>(request, url);
    }

    public async Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request)
    {
        var url = new Uri(_endpoint, PUT_OBJECTS);
        return await SendRequestAsync<PutObjectRequest, PutObjectResponse>(request, url);
    }

    public async Task<DeleteObjectResponse> DeleteObjectAsync(DeleteObjectRequest request)
    {
        var url = new Uri(_endpoint, DELETE_OBJECT);
        return await SendRequestAsync<DeleteObjectRequest, DeleteObjectResponse>(request, url);
    }

    public async Task<ListKeyVersionsResponse> ListKeyVersionsAsync(ListKeyVersionsRequest request)
    {
        var url = new Uri(_endpoint, LIST_KEY_VERSIONS);
        return await SendRequestAsync<ListKeyVersionsRequest, ListKeyVersionsResponse>(request, url);
    }

    private async Task<TResponse> SendRequestAsync<TRequest, TResponse>(TRequest request, Uri url)
        where TRequest : IMessage<TRequest>
        where TResponse : IMessage<TResponse>, new()
    {
        var requestContent = new ByteArrayContent(request.ToByteArray());
        requestContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

        var response = await _httpClient.PostAsync(url, requestContent);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new VssClientException($"HTTP error {(int)response.StatusCode} occurred: {errorContent}");
        }

        var responseBytes = await response.Content.ReadAsByteArrayAsync();
        var parsedResponse = (TResponse)new TResponse().Descriptor.Parser.ParseFrom(responseBytes);

        if (parsedResponse is GetObjectResponse {Value: null})
        {
            throw new VssClientException("VSS Server API Violation, expected value in GetObjectResponse but found none.");
        }

        return parsedResponse;
    }
}