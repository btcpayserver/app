using System.Runtime.Serialization;
using org.ldk.structs;
using org.ldk.util;

namespace BTCPayApp.Core.LDK;

public static class ChannelManagerHelper
{
    public static ChannelMonitor[] GetInitialMonitors(IEnumerable<byte[]> channelMonitorsSerialized,
        EntropySource entropySource, SignerProvider signerProvider)
    {
        var monitorFundingSet = new HashSet<OutPoint>();
        return channelMonitorsSerialized.Select(bytes =>
        {
            if (UtilMethods.C2Tuple_ThirtyTwoBytesChannelMonitorZ_read(bytes, entropySource,
                    signerProvider) is not Result_C2Tuple_ThirtyTwoBytesChannelMonitorZDecodeErrorZ.
                Result_C2Tuple_ThirtyTwoBytesChannelMonitorZDecodeErrorZ_OK res)
            {
                throw new SerializationException("Serialized ChannelMonitor was corrupt");
            }

            var monitor = res.res.get_b();

            if (!monitorFundingSet.Add(monitor.get_funding_txo().get_a()))
            {
                throw new SerializationException(
                    "Set of ChannelMonitors contained duplicates (ie the same funding_txo was set on multiple monitors)");
            }

            return monitor;
        }).ToArray();
    }


    public static ChannelManager? Load(ChannelMonitor[] channelMonitors, byte[] channelManagerSerialized,
        EntropySource entropySource, SignerProvider signerProvider,
        NodeSigner nodeSigner, FeeEstimator feeEstimator,
        Watch watch, BroadcasterInterface txBroadcaster,
        Router router, Logger logger, UserConfig config, Filter filter)
    {
        var resManager = UtilMethods.C2Tuple_ThirtyTwoBytesChannelManagerZ_read(channelManagerSerialized, entropySource,
            nodeSigner, signerProvider, feeEstimator,
            watch, txBroadcaster,
            router, logger, config, channelMonitors);
        if (!resManager.is_ok())
        {
            throw new SerializationException("Serialized ChannelManager was corrupt");
        }

        foreach (var monitor in channelMonitors)
        {
            monitor.load_outputs_to_watch(filter, logger);
        }

        return (resManager as Result_C2Tuple_ThirtyTwoBytesChannelManagerZDecodeErrorZ.
            Result_C2Tuple_ThirtyTwoBytesChannelManagerZDecodeErrorZ_OK)?.res.get_b();
    }
}