using System.Text.Json;
using BTCPayApp.Core.Attempt2;
using Microsoft.EntityFrameworkCore;
using VSSProto;

namespace BTCPayApp.Core.Data;

class TriggerRecord
{
    public string name { get; set; }
    public string sql { get; set; }
}
public class RemoteToLocalSyncService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly BTCPayConnectionManager _btcPayConnectionManager;

    public RemoteToLocalSyncService(IDbContextFactory<AppDbContext> dbContextFactory,
        BTCPayConnectionManager btcPayConnectionManager)
    {
        _dbContextFactory = dbContextFactory;
        _btcPayConnectionManager = btcPayConnectionManager;
    }
    
    // on connected to btcpay, sync all the data from the remote to the local
    // if we are the active node

    

}