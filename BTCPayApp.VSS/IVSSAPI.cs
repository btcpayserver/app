using VSSProto;

namespace BTCPayApp.VSS;

/// <summary>
/// Defines the API of Versioned Storage Service (VSS).
/// The API operations are congruent to the VSS server-side API.
/// </summary>
public interface IVSSAPI
{
    /// <summary>
    /// Asynchronously fetches a value based on a specified key.
    /// </summary>
    /// <param name="request">The request details including the key to fetch.</param>
    /// <returns>A task representing the asynchronous operation, which encapsulates the response with the fetched value.</returns>
    Task<GetObjectResponse> GetObjectAsync(GetObjectRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously writes objects as part of a single transaction to the VSS.
    /// </summary>
    /// <param name="request">The details of objects to be put, encapsulated as a transaction.</param>
    /// <returns>A task representing the asynchronous operation, which encapsulates the response after putting objects.</returns>
    Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously deletes a specified object from the VSS.
    /// </summary>
    /// <param name="request">The details of the object to delete, including key and value.</param>
    /// <returns>A task representing the asynchronous operation, which encapsulates the response after deleting the object.</returns>
    Task<DeleteObjectResponse> DeleteObjectAsync(DeleteObjectRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously lists all keys and their corresponding versions based on a specified store ID.
    /// </summary>
    /// <param name="request">The request details including the store ID for which keys and versions need to be listed.</param>
    /// <returns>A task representing the asynchronous operation, which encapsulates the response with all listed keys and versions.</returns>
    Task<ListKeyVersionsResponse> ListKeyVersionsAsync(ListKeyVersionsRequest request, CancellationToken cancellationToken = default);
}