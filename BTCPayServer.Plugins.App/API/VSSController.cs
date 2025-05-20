using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Plugins.App.Data;
using BTCPayServer.Plugins.App.Data.Models;
using Google.Protobuf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VSS;
using VSSProto;

namespace BTCPayServer.Plugins.App.API;

[ApiController]
[ResultOverrideFilter]
[ProtobufFormatter]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.GreenfieldAPIKeys)]
[Route("vss")]
public class VSSController(
    AppPluginDbContextFactory dbContextFactory,
    UserManager<ApplicationUser> userManager,
    BTCPayAppState appState,
    ILogger<VSSController> logger)
    : Controller, IVSSAPI
{
    [HttpPost(HttpVSSAPIClient.GET_OBJECT)]
    [MediaTypeConstraint("application/octet-stream")]
    public async Task<GetObjectResponse> GetObjectAsync(GetObjectRequest request, CancellationToken cancellationToken = default)
    {
        var userId = userManager.GetUserId(User);
        await using var dbContext = dbContextFactory.CreateContext();
        var store = await dbContext.AppStorageItems.SingleOrDefaultAsync(data =>
            data.Key == request.Key && data.UserId == userId, cancellationToken: cancellationToken);
        if (store == null)
        {
            return SetResult<GetObjectResponse>(
                new NotFoundObjectResult(new ErrorResponse
                {
                    ErrorCode = ErrorCode.NoSuchKeyException,
                    Message = "Key not found"
                }));
        }

        return new GetObjectResponse
        {
            Value = new KeyValue
            {
                Key = store.Key,
                Value = ByteString.CopyFrom(store.Value),
                Version = store.Version
            }
        };
    }

    [HttpPost(HttpVSSAPIClient.PUT_OBJECTS)]
    [MediaTypeConstraint("application/octet-stream")]
    public async Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, CancellationToken cancellationToken = default)
    {
        var deviceId = request.GlobalVersion;
        if (!await VerifyMaster(deviceId))
            return SetResult<PutObjectResponse>(BadRequest(new ErrorResponse
            {
                ErrorCode = ErrorCode.ConflictException,
                Message = "Global version mismatch"
            }));

        var userId = userManager.GetUserId(User)!;

        // TODO: Log with debug level once we solve the issue #208
        logger.LogInformation("VSS backup request for user {UserId} ({DeviceId}): {TransactionItems} / {DeleteItems}", userId, deviceId,
            request.TransactionItems.Count != 0 ? string.Join(", ", request.TransactionItems.Select(data => data.Key)) : "no transaction items",
            request.DeleteItems.Count != 0 ? string.Join(", ", request.DeleteItems.Select(data => data.Key)) : "no delete items");

        await using var dbContext = dbContextFactory.CreateContext();
        return await dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async _ =>
        {
            await using var dbContextTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                if (request.TransactionItems.Count != 0)
                {
                    var items = request.TransactionItems.Select(data => new AppStorageItemData
                    {
                        Key = data.Key,
                        Value = data.Value.ToByteArray(),
                        UserId = userId,
                        Version = data.Version
                    });
                    await dbContext.AppStorageItems.AddRangeAsync(items, cancellationToken);
                }

                var deleted = 0;
                if (request.DeleteItems.Count != 0)
                {
                    var deleteQuery = request.DeleteItems.Aggregate(
                        dbContext.AppStorageItems.Where(data => data.UserId == userId),
                        (current, key) => current.Where(data => data.Key == key.Key && data.Version == key.Version));
                    deleted = await deleteQuery.ExecuteDeleteAsync(cancellationToken: cancellationToken);
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                await dbContextTransaction.CommitAsync(cancellationToken);
                logger.LogInformation("VSS backup request for user {UserId} ({DeviceId}) processed: {TCount} items added, {DCount} items deleted", userId, deviceId, request.TransactionItems.Count, deleted);
                await appState.GracefulDisconnect(userId);
                return new PutObjectResponse();
            }
            catch (Exception e)
            {
                logger.LogError(e, "VSS backup request for user {UserId} ({DeviceId}) failed: {Message}", userId, deviceId, e.Message);
                await dbContextTransaction.RollbackAsync(cancellationToken);
                return SetResult<PutObjectResponse>(BadRequest(new ErrorResponse
                {
                    ErrorCode = ErrorCode.ConflictException,
                    Message = e.Message
                }));
            }
        }, cancellationToken);
    }

    [HttpPost(HttpVSSAPIClient.DELETE_OBJECT)]
    [MediaTypeConstraint("application/octet-stream")]
    public async Task<DeleteObjectResponse> DeleteObjectAsync(DeleteObjectRequest request, CancellationToken cancellationToken = default)
    {
        var userId = userManager.GetUserId(User);
        var key = request.KeyValue.Key;
        var ver = request.KeyValue.Version;
        logger.LogInformation("VSS delete request from user {UserId} with key {Key} and version {Version}", userId, key, ver);
        await using var dbContext = dbContextFactory.CreateContext();
        var store = await dbContext.AppStorageItems
            .Where(data => data.Key == key && data.UserId == userId && data.Version == ver)
            .ExecuteDeleteAsync(cancellationToken: cancellationToken);
        logger.LogInformation("VSS delete request from user {UserId} with key {Key} and version {Version} processed: {Count} items deleted", userId, key, ver, store);
        return store == 0
            ? SetResult<DeleteObjectResponse>(
                new NotFoundObjectResult(new ErrorResponse
                {
                    ErrorCode = ErrorCode.NoSuchKeyException,
                    Message = "Key not found"
                }))
            : new DeleteObjectResponse();
    }

    [HttpPost(HttpVSSAPIClient.LIST_KEY_VERSIONS)]
    public async Task<ListKeyVersionsResponse> ListKeyVersionsAsync(ListKeyVersionsRequest? request = null, CancellationToken cancellationToken = default)
    {
        var userId = userManager.GetUserId(User);
        await using var dbContext = dbContextFactory.CreateContext();
        var items = await dbContext.AppStorageItems
            .Where(data => data.UserId == userId && data.Key != "masterDevice")
            .Select(data => new KeyValue { Key = data.Key, Version = data.Version })
            .ToListAsync(cancellationToken: cancellationToken);
        return new ListKeyVersionsResponse {KeyVersions = {items}};
    }

    private T SetResult<T>(IActionResult result)
    {
        HttpContext.Items["Result"] = result;
        return default;
    }

    private async Task<bool> VerifyMaster(long deviceIdentifier)
    {
        var userId = userManager.GetUserId(User);
        return userId != null && await appState.IsMaster(userId, deviceIdentifier);
    }
}
