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

        var response = await _httpClient.PostAsync(url, requestContent, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new VssClientException($"HTTP error {(int)response.StatusCode} occurred: {errorContent}");
        }

        var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var parsedResponse = (TResponse)new TResponse().Descriptor.Parser.ParseFrom(responseBytes);

        if (parsedResponse is GetObjectResponse {Value: null})
        {
            throw new VssClientException("VSS Server API Violation, expected value in GetObjectResponse but found none.");
        }

        return parsedResponse;
    }
}