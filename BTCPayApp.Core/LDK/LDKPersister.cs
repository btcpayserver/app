using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.LDK;
using org.ldk.structs;

namespace nldksample.LDK;



public class LDKPersister: PersisterInterface
{
    private readonly CurrentWalletService _currentWalletService;
    private readonly LightningNodeService _lightningNodeService;
    private readonly IConfigProvider _configProvider;

    public LDKPersister(CurrentWalletService currentWalletService, LightningNodeService lightningNodeService, IConfigProvider configProvider)
    {
        _currentWalletService = currentWalletService;
        _lightningNodeService = lightningNodeService;
        _configProvider = configProvider;
    }
    public Result_NoneIOErrorZ persist_manager(ChannelManager channel_manager)
    {
        _currentWalletService.UpdateChannelManager(channel_manager).ConfigureAwait(false).GetAwaiter().GetResult();
        return Result_NoneIOErrorZ.ok();
    }

    public Result_NoneIOErrorZ persist_graph(NetworkGraph network_graph)
    {
        _lightningNodeService.UpdateNetworkGraph(network_graph).ConfigureAwait(false).GetAwaiter().GetResult();
        return Result_NoneIOErrorZ.ok();
    }

    public Result_NoneIOErrorZ persist_scorer(WriteableScore scorer)
    {
        _lightningNodeService.UpdateScore(scorer).ConfigureAwait(false).GetAwaiter().GetResult();
        return Result_NoneIOErrorZ.ok();
    }
}