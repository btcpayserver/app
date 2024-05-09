using System.Threading.Channels;
using BTCPayApp.Core.Data;
using NBitcoin;
using NBitcoin.Scripting;
using org.ldk.structs;
using Channel = System.Threading.Channels.Channel;

namespace BTCPayApp.Core.Helpers;

public static class ChannelExtensions
{
    
    public static IDisposable SubscribeToEventWithChannelQueue<TEvent>(
        Action<AsyncEventHandler<TEvent>> add,
        Action<AsyncEventHandler<TEvent>> remove, 
        Func<TEvent, CancellationToken, Task> processor,
        CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<TEvent>();

        async Task OnEvent(object? sender, TEvent evt)
        {
            await channel.Writer.WriteAsync(evt, cancellationToken);
        }

        add(new AsyncEventHandler<TEvent>(OnEvent));
        _ = ProcessChannel(channel, processor, cancellationToken);

        return new DisposableWrapper(async () =>
        {
            remove(new AsyncEventHandler<TEvent>(OnEvent));
            channel.Writer.Complete();
        });
    }

    private static async Task ProcessChannel<TEvent>(Channel<TEvent> channel, Func<TEvent, CancellationToken, Task> processor, CancellationToken cancellationToken)
    {
        while (await channel.Reader.WaitToReadAsync(cancellationToken))
        {
            while (channel.Reader.TryRead(out TEvent item))
            {
                await processor(item, cancellationToken);
            }
        }
    }


    public static (BitcoinExtPubKey, RootedKeyPath, ScriptPubKeyType)? ExtractFromDescriptor(this string descriptor, Network network)
    {
        var od = OutputDescriptor.Parse(descriptor, network);

        (BitcoinExtPubKey, RootedKeyPath) ExtractFromPkProvider(PubKeyProvider pubKeyProvider)
        {
            switch (pubKeyProvider)
            {
                case PubKeyProvider.Const _:
                    throw new FormatException("Only HD output descriptors are supported.");
                case PubKeyProvider.HD hd:
                    if (hd.Path != null && hd.Path.ToString() != "0")
                    {
                        throw new FormatException("Custom change paths are not supported.");
                    }
                    return (hd.Extkey, null);
                case PubKeyProvider.Origin origin:
                    var innerResult = ExtractFromPkProvider(origin.Inner);
                    return (innerResult.Item1,  origin.KeyOriginInfo );
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        switch (od)
        {
            case OutputDescriptor.WPKH wpkh:
                var res=  ExtractFromPkProvider(wpkh.PkProvider);
                return (res.Item1, res.Item2, ScriptPubKeyType.Segwit);
        }

        return null;
    }

    public static UserConfig AsLDKUserConfig(this LightningConfig config)
    {
        var result = UserConfig.with_default();
        result.set_accept_intercept_htlcs(true);
        result.set_accept_mpp_keysend(true);
        result.set_manually_accept_inbound_channels(true);
        var channelHandshakeConfig = ChannelHandshakeConfig.with_default();
        channelHandshakeConfig.set_announced_channel(false);
        channelHandshakeConfig.set_negotiate_anchors_zero_fee_htlc_tx(true);
        channelHandshakeConfig.set_minimum_depth(1);
        result.set_channel_handshake_config(channelHandshakeConfig);
        var channelHandshakeLimits = ChannelHandshakeLimits.with_default();
        channelHandshakeLimits.set_force_announced_channel_preference(true);
        result.set_channel_handshake_limits(channelHandshakeLimits);
        return result;
    }
    
    // public static async Task Process<T>(this Channel<T> channel, Func<T, CancellationToken, Task> processor,
    //     CancellationToken cancellationToken)
    // {
    //     while (await channel.Reader.WaitToReadAsync(cancellationToken))
    //     {
    //         while (channel.Reader.TryRead(out var item))
    //         {
    //             await processor(item, cancellationToken);
    //         }
    //     }
    // }
    //
    // public static IDisposable SubscribeToEventWithChannelQueue<TEvent, TEventHandler>(Action<TEventHandler> add,
    //     Action<TEventHandler> remove, Func<TEvent, CancellationToken, Task> processor,
    //     CancellationToken cancellationToken) where TEventHandler : Delegate
    // {
    //     var channel = Channel.CreateUnbounded<TEvent>();
    //
    //     // Assuming TEventHandler is a delegate type compatible with this handler signature
    //     async void OnEvent(object? sender, TEvent evt)
    //     {
    //         await channel.Writer.WriteAsync(evt, cancellationToken);
    //     }
    //
    //     // Cast OnEvent to TEventHandler. This cast needs to be valid according to TEventHandler's definition
    //     add((TEventHandler)(object)OnEvent);
    //     _ = channel.Process(processor, cancellationToken);
    //
    //     return new DisposableWrapper(async () =>
    //     {
    //         remove((TEventHandler)(object)OnEvent);
    //         channel.Writer.Complete();
    //     });
    // }


    public static void Dispose(this IEnumerable<IDisposable> disposables)
    {
        foreach (var disposable in disposables)
        {
            disposable.Dispose();
        }
    }


    public class DisposableWrapper : IDisposable, IAsyncDisposable
    {
        private readonly Func<Task> _disposeAsync;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1);
        private readonly TaskCompletionSource _tcs = new();

        public DisposableWrapper(Func<Task> disposeAsync)
        {
            _disposeAsync = disposeAsync;
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().Wait();
        }

        public async ValueTask DisposeAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                if (_tcs.Task.IsCompleted)
                    return;
                await _disposeAsync();
                _tcs.TrySetResult();
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}