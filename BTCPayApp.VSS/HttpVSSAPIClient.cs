using Google.Protobuf;
using VSSProto;

namespace BTCPayApp.VSS;

using System.Threading.Tasks;

public class HttpVSSAPIClient : IVSSAPI
{
    private readonly Uri _endpoint;
    private readonly HttpClient _httpClient;

    public const string GET_OBJECT = "getObject";
    public const string PUT_OBJECTS = "putObjects";
    public const string DELETE_OBJECT = "deleteObject";
    public const string LIST_KEY_VERSIONS = "listKeyVersions";
    public HttpVSSAPIClient(Uri endpoint, HttpClient? httpClient = null)
    {
        _endpoint = endpoint;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<GetObjectResponse> GetObjectAsync(GetObjectRequest request, CancellationToken cancellationToken = default)
    {
        var url = new Uri(_endpoint, GET_OBJECT);
        return await SendRequestAsync<GetObjectRequest, GetObjectResponse>(request, url, cancellationToken);
    }

    public async Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, CancellationToken cancellationToken = default)
    {
        var url = new Uri(_endpoint, PUT_OBJECTS);
        return await SendRequestAsync<PutObjectRequest, PutObjectResponse>(request, url, cancellationToken);
    }

    public async Task<DeleteObjectResponse> DeleteObjectAsync(DeleteObjectRequest request, CancellationToken cancellationToken = default)
    {
        var url = new Uri(_endpoint, DELETE_OBJECT);
        return await SendRequestAsync<DeleteObjectRequest, DeleteObjectResponse>(request, url, cancellationToken);
    }

    public async Task<ListKeyVersionsResponse> ListKeyVersionsAsync(ListKeyVersionsRequest request, CancellationToken cancellationToken = default)
    {
        var url = new Uri(_endpoint, LIST_KEY_VERSIONS);
        return await SendRequestAsync<ListKeyVersionsRequest, ListKeyVersionsResponse>(request, url, cancellationToken);
    }

    private async Task<TResponse> SendRequestAsync<TRequest, TResponse>(TRequest request, Uri url, CancellationToken cancellationToken)
        where TRequest : IMessage<TRequest>
        where TResponse : IMessage<TResponse>, new()
    {
        var requestContent = new ByteArrayContent(request.ToByteArray());
        requestContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        foreach (var (key, value) in _httpClient.DefaultRequestHeaders)
        {
            requestContent.Headers.TryAddWithoutValidation(key, value);
            
        }

        var response = await _httpClient.PostAsync(url, requestContent, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            try
            {
                var error = ErrorResponse.Parser.ParseFrom(await response.Content.ReadAsStreamAsync(cancellationToken));
                throw new VssClientException( error);
            }
            catch (Exception e)
            {
                throw new VssClientException( new ErrorResponse()
                {
                    ErrorCode = ErrorCode.Unknown,
                    Message = e.Message,
                });
            }
            
        }

        var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var parsedResponse = (TResponse)new TResponse().Descriptor.Parser.ParseFrom(responseBytes);

        if (parsedResponse is GetObjectResponse {Value: null or {Value.Length: 0}})
        {
            throw new VssClientException( new ErrorResponse()
            {
                ErrorCode = ErrorCode.NoSuchKeyException,
                Message = "VSS Server API Violation, expected value in GetObjectResponse but found none.",
            });
        }

        return parsedResponse;
    }
}