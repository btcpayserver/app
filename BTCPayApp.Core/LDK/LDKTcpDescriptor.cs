using System.Net.Sockets;
using BTCPayApp.Core.Helpers;
using Microsoft.Extensions.Logging;
using NBitcoin;
using org.ldk.structs;

namespace BTCPayApp.Core.LDK;

public class LDKTcpDescriptor : SocketDescriptorInterface
{
    private readonly PeerManager _peerManager;
    private readonly TcpClient _tcpClient;
    private readonly ILogger _logger;
    private readonly Action<string> _onDisconnect;
    private readonly NetworkStream _stream;
    private readonly CancellationTokenSource _cts;

    private SocketDescriptor SocketDescriptor { get; set; }
    public string Id { get; set; }
    readonly SemaphoreSlim _readSemaphore = new(1, 1);
    private readonly TaskCompletionSource _tcs;

    public static LDKTcpDescriptor? Inbound(PeerManager peerManager, TcpClient tcpClient, ILogger logger, ObservableConcurrentDictionary<string, LDKTcpDescriptor> descriptors)
    {
        var descriptor = new LDKTcpDescriptor(peerManager, tcpClient, logger,s => descriptors.TryRemove(s, out _));
        var result = peerManager.new_inbound_connection(descriptor.SocketDescriptor, tcpClient.Client.GetSocketAddress());
        if (result.is_ok())
        {
            logger.LogInformation("New inbound connection accepted");
            descriptor.Start();
            return descriptor;
        }
        if (result is Result_NonePeerHandleErrorZ.Result_NonePeerHandleErrorZ_Err)
        {
            logger.LogError("Failed to create inbound connection");
            tcpClient.Dispose();
            return null;
        }

        descriptor.disconnect_socket();
        return null;
    }

    public static LDKTcpDescriptor? Outbound(PeerManager peerManager, TcpClient tcpClient, ILogger logger,
        PubKey pubKey, ObservableConcurrentDictionary<string, LDKTcpDescriptor> descriptors)
    {
        var descriptor = new LDKTcpDescriptor(peerManager, tcpClient, logger, s => descriptors.TryRemove(s, out _));
        var saSocketAddress = tcpClient.Client?.GetSocketAddress();
        if (saSocketAddress is null)
        {
            logger.LogWarning("Failed to get tcp client or socket address so cannot create outbound connection");
            descriptor.disconnect_socket();
            return null;
        }

        logger.LogInformation("Connected to {PubKey} at {Str}", pubKey, ((Option_SocketAddressZ.Option_SocketAddressZ_Some)saSocketAddress).some.to_str());
        descriptor.Start();
        var result = peerManager.new_outbound_connection(pubKey.ToBytes(), descriptor.SocketDescriptor,saSocketAddress);
        if (result is Result_CVec_u8ZPeerHandleErrorZ.Result_CVec_u8ZPeerHandleErrorZ_OK ok)
        {
            descriptor.send_data(ok.res, true);
        }

        if (result.is_ok())
        {
            logger.LogInformation("New outbound connection accepted");

            return descriptor;
        }
        if (result is Result_CVec_u8ZPeerHandleErrorZ.Result_CVec_u8ZPeerHandleErrorZ_Err errResult)
        {
            logger.LogError("Failed to create outbound connection: {Error}",
                errResult.err is { } peerError ? peerError.ToString() : errResult.ToString());
            tcpClient.Dispose();
            return null;
        }
        descriptor.disconnect_socket();
        return null;
    }

    private LDKTcpDescriptor(PeerManager peerManager, TcpClient tcpClient, ILogger logger, Action<string> onDisconnect)
    {
        _peerManager = peerManager;
        _tcpClient = tcpClient;
        _logger = logger;
        _onDisconnect = onDisconnect;
        _stream = tcpClient.GetStream();
        Id = Guid.NewGuid().ToString();
        SocketDescriptor = SocketDescriptor.new_impl(this);

        _cts = new CancellationTokenSource();
        _tcs = new TaskCompletionSource();
        _ = CheckConnection(_cts.Token);
        _ = ReadEvents(_cts.Token);
    }

    private void Start()
    {
        _tcs.TrySetResult();
    }

    private async Task CheckConnection(CancellationToken cancellationToken)
    {
        await _tcs.Task.WaitAsync(cancellationToken);
        while (!cancellationToken.IsCancellationRequested && _tcpClient.Connected)
        {
            try
            {
                await Task.Delay(1000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }
        disconnect_socket();
    }

    private async Task ReadEvents(CancellationToken cancellationToken)
    {
        await _tcs.Task.WaitAsync(cancellationToken);
        //max 4kib
        var bufSz = 4096;
        var buffer = new byte[bufSz];
        while (_tcpClient.Connected && !_cts.IsCancellationRequested)
        {
            var read = await _stream.ReadAsync(buffer,cancellationToken);

            if (read == 0)
            {
                _logger.LogWarning("Read 0 bytes of data from peer");
                disconnect_socket();
                return;
            }

            var data = buffer[..read];
            _logger.LogTrace("Read {Read} bytes of data from peer", read);
            switch ( _peerManager.read_event(SocketDescriptor, data))
            {
                case Result_boolPeerHandleErrorZ.Result_boolPeerHandleErrorZ_OK ok:
                    if (ok.res)
                    {
                        _logger.LogInformation("Pausing as per instruction from read_event");
                        await _readSemaphore.WaitAsync(cancellationToken);
                    }
                    break;
                case Result_boolPeerHandleErrorZ.Result_boolPeerHandleErrorZ_Err err:
                    _logger.LogWarning("Failed to read event from peer: {Error}", err.err.GetType());
                    disconnect_socket();
                    break;
            }

            _peerManager.process_events();
        }
    }

    private void Resume()
    {
        try
        {
            _readSemaphore.Release();

            _logger.LogInformation("resuming read");
        }
        catch (Exception)
        {
            // ignored
        }
    }

    public long send_data(byte[] data, bool resumeRead)
    {
        try
        {
            _logger.LogTrace("Sending {Bytes} bytes of data to peer", data.Length);
            var result = _tcpClient.Client.Send(data);
            _logger.LogTrace("Sent {Bytes} bytes of data to peer", result);
            if (resumeRead) Resume();
            return result;
        }
        catch (Exception e)
        {
            _logger.LogError("Failed to send {Bytes} bytes of data to peer: {Error} - disconnecting socket", data.Length, e.Message);
            disconnect_socket();
            return 0;
        }
    }

    public void disconnect_socket()
    {
        if (_cts.IsCancellationRequested) return;

        _logger.LogInformation("Disconnecting socket");
        _cts.Cancel();
        _stream.Dispose();
        _tcpClient.Dispose();
        _onDisconnect(Id);
    }

    public bool eq(SocketDescriptor otherArg)
    {
        return hash() == otherArg.hash();
    }

    public long hash()
    {
        return Id.GetHashCode();
    }
}
