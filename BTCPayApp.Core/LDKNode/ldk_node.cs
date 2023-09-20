

using System.IO;
using System.Runtime.InteropServices;
using System;
namespace uniffi.ldk_node;
using Address = String;
using Bolt11Invoice = String;
using ChannelId = String;
using FfiConverterTypeAddress = FfiConverterString;
using FfiConverterTypeBolt11Invoice = FfiConverterString;
using FfiConverterTypeChannelId = FfiConverterString;
using FfiConverterTypeMnemonic = FfiConverterString;
using FfiConverterTypeNetAddress = FfiConverterString;
using FfiConverterTypePaymentHash = FfiConverterString;
using FfiConverterTypePaymentPreimage = FfiConverterString;
using FfiConverterTypePaymentSecret = FfiConverterString;
using FfiConverterTypePublicKey = FfiConverterString;
using FfiConverterTypeTxid = FfiConverterString;
using FfiConverterTypeUserChannelId = FfiConverterString;
using Mnemonic = String;
using NetAddress = String;
using PaymentHash = String;
using PaymentPreimage = String;
using PaymentSecret = String;
using PublicKey = String;
using Txid = String;
using UserChannelId = String;



// This is a helper for safely working with byte buffers returned from the Rust code.
// A rust-owned buffer is represented by its capacity, its current length, and a
// pointer to the underlying data.

[StructLayout(LayoutKind.Sequential)]
internal struct RustBuffer {
    public int capacity;
    public int len;
    public IntPtr data;

    public static RustBuffer Alloc(int size) {
        return _UniffiHelpers.RustCall((ref RustCallStatus status) => {
            var buffer = _UniFFILib.ffi_ldk_node_f89f_rustbuffer_alloc(size, ref status);
            if (buffer.data == IntPtr.Zero) {
                throw new AllocationException($"RustBuffer.Alloc() returned null data pointer (size={size})");
            }
            return buffer;
        });
    }

    public static void Free(RustBuffer buffer) {
        _UniffiHelpers.RustCall((ref RustCallStatus status) => {
            _UniFFILib.ffi_ldk_node_f89f_rustbuffer_free(buffer, ref status);
        });
    }

    public BigEndianStream AsStream() {
        unsafe {
            return new BigEndianStream(new UnmanagedMemoryStream((byte*)data.ToPointer(), len));
        }
    }

    public BigEndianStream AsWriteableStream() {
        unsafe {
            return new BigEndianStream(new UnmanagedMemoryStream((byte*)data.ToPointer(), capacity, capacity, FileAccess.Write));
        }
    }
}

// This is a helper for safely passing byte references into the rust code.
// It's not actually used at the moment, because there aren't many things that you
// can take a direct pointer to managed memory, and if we're going to copy something
// then we might as well copy it into a `RustBuffer`. But it's here for API
// completeness.

[StructLayout(LayoutKind.Sequential)]
internal struct ForeignBytes {
    public int length;
    public IntPtr data;
}


// The FfiConverter interface handles converter types to and from the FFI
//
// All implementing objects should be public to support external types.  When a
// type is external we need to import it's FfiConverter.
internal abstract class FfiConverter<CsType, FfiType> {
    // Convert an FFI type to a C# type
    public abstract CsType Lift(FfiType value);

    // Convert C# type to an FFI type
    public abstract FfiType Lower(CsType value);

    // Read a C# type from a `ByteBuffer`
    public abstract CsType Read(BigEndianStream stream);

    // Calculate bytes to allocate when creating a `RustBuffer`
    //
    // This must return at least as many bytes as the write() function will
    // write. It can return more bytes than needed, for example when writing
    // Strings we can't know the exact bytes needed until we the UTF-8
    // encoding, so we pessimistically allocate the largest size possible (3
    // bytes per codepoint).  Allocating extra bytes is not really a big deal
    // because the `RustBuffer` is short-lived.
    public abstract int AllocationSize(CsType value);

    // Write a C# type to a `ByteBuffer`
    public abstract void Write(CsType value, BigEndianStream stream);

    // Lower a value into a `RustBuffer`
    //
    // This method lowers a value into a `RustBuffer` rather than the normal
    // FfiType.  It's used by the callback interface code.  Callback interface
    // returns are always serialized into a `RustBuffer` regardless of their
    // normal FFI type.
    public RustBuffer LowerIntoRustBuffer(CsType value) {
        var rbuf = RustBuffer.Alloc(AllocationSize(value));
        try {
            var stream = rbuf.AsWriteableStream();
            Write(value, stream);
            rbuf.len = Convert.ToInt32(stream.Position);
            return rbuf;
        } catch {
            RustBuffer.Free(rbuf);
            throw;
        }
    }

    // Lift a value from a `RustBuffer`.
    //
    // This here mostly because of the symmetry with `lowerIntoRustBuffer()`.
    // It's currently only used by the `FfiConverterRustBuffer` class below.
    protected CsType LiftFromRustBuffer(RustBuffer rbuf) {
        var stream = rbuf.AsStream();
        try {
           var item = Read(stream);
           if (stream.HasRemaining()) {
               throw new InternalException("junk remaining in buffer after lifting, something is very wrong!!");
           }
           return item;
        } finally {
            RustBuffer.Free(rbuf);
        }
    }
}

// FfiConverter that uses `RustBuffer` as the FfiType
internal abstract class FfiConverterRustBuffer<CsType>: FfiConverter<CsType, RustBuffer> {
    public override CsType Lift(RustBuffer value) {
        return LiftFromRustBuffer(value);
    }
    public override RustBuffer Lower(CsType value) {
        return LowerIntoRustBuffer(value);
    }
}


// A handful of classes and functions to support the generated data structures.
// This would be a good candidate for isolating in its own ffi-support lib.
// Error runtime.
[StructLayout(LayoutKind.Sequential)]
struct RustCallStatus {
    public int code;
    public RustBuffer error_buf;

    public bool IsSuccess() {
        return code == 0;
    }

    public bool IsError() {
        return code == 1;
    }

    public bool IsPanic() {
        return code == 2;
    }
}

// Base class for all uniffi exceptions
public class UniffiException: Exception {
    public UniffiException(): base() {}
    public UniffiException(string message): base(message) {}
}

public class UndeclaredErrorException: UniffiException {
    public UndeclaredErrorException(string message): base(message) {}
}

public class PanicException: UniffiException {
    public PanicException(string message): base(message) {}
}

public class AllocationException: UniffiException {
    public AllocationException(string message): base(message) {}
}

public class InternalException: UniffiException {
    public InternalException(string message): base(message) {}
}

public class InvalidEnumException: InternalException {
    public InvalidEnumException(string message): base(message) {
    }
}

// Each top-level error class has a companion object that can lift the error from the call status's rust buffer
interface CallStatusErrorHandler<E> where E: Exception {
    E Lift(RustBuffer error_buf);
}

// CallStatusErrorHandler implementation for times when we don't expect a CALL_ERROR
class NullCallStatusErrorHandler: CallStatusErrorHandler<UniffiException> {
    public static NullCallStatusErrorHandler INSTANCE = new NullCallStatusErrorHandler();

    public UniffiException Lift(RustBuffer error_buf) {
        RustBuffer.Free(error_buf);
        return new UndeclaredErrorException("library has returned an error not declared in UNIFFI interface file");
    }
}

// Helpers for calling Rust
// In practice we usually need to be synchronized to call this safely, so it doesn't
// synchronize itself
class _UniffiHelpers {
    public delegate void RustCallAction(ref RustCallStatus status);
    public delegate U RustCallFunc<out U>(ref RustCallStatus status);

    // Call a rust function that returns a Result<>.  Pass in the Error class companion that corresponds to the Err
    public static U RustCallWithError<U, E>(CallStatusErrorHandler<E> errorHandler, RustCallFunc<U> callback)
        where E: UniffiException
    {
        var status = new RustCallStatus();
        var return_value = callback(ref status);
        if (status.IsSuccess()) {
            return return_value;
        } else if (status.IsError()) {
            throw errorHandler.Lift(status.error_buf);
        } else if (status.IsPanic()) {
            // when the rust code sees a panic, it tries to construct a rustbuffer
            // with the message.  but if that code panics, then it just sends back
            // an empty buffer.
            if (status.error_buf.len > 0) {
                throw new PanicException(FfiConverterString.INSTANCE.Lift(status.error_buf));
            } else {
                throw new PanicException("Rust panic");
            }
        } else {
            throw new InternalException($"Unknown rust call status: {status.code}");
        }
    }

    // Call a rust function that returns a Result<>.  Pass in the Error class companion that corresponds to the Err
    public static void RustCallWithError<E>(CallStatusErrorHandler<E> errorHandler, RustCallAction callback)
        where E: UniffiException
    {
        _UniffiHelpers.RustCallWithError(errorHandler, (ref RustCallStatus status) => {
            callback(ref status);
            return 0;
        });
    }

    // Call a rust function that returns a plain value
    public static U RustCall<U>(RustCallFunc<U> callback) {
        return _UniffiHelpers.RustCallWithError(NullCallStatusErrorHandler.INSTANCE, callback);
    }

    // Call a rust function that returns a plain value
    public static void RustCall(RustCallAction callback) {
        _UniffiHelpers.RustCall((ref RustCallStatus status) => {
            callback(ref status);
            return 0;
        });
    }
}


// Big endian streams are not yet available in dotnet :'(
// https://github.com/dotnet/runtime/issues/26904

class StreamUnderflowException: Exception {
    public StreamUnderflowException() {
    }
}

class BigEndianStream {
    Stream stream;
    public BigEndianStream(Stream stream) {
        this.stream = stream;
    }

    public bool HasRemaining() {
        return (stream.Length - stream.Position) > 0;
    }

    public long Position {
        get => stream.Position;
        set => stream.Position = value;
    }

    public void WriteBytes(byte[] value) {
        stream.Write(value, 0, value.Length);
    }

    public void WriteByte(byte value) {
        stream.WriteByte(value);
    }

    public void WriteUShort(ushort value) {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    public void WriteUInt(uint value) {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    public void WriteULong(ulong value) {
        WriteUInt((uint)(value >> 32));
        WriteUInt((uint)value);
    }

    public void WriteSByte(sbyte value) {
        stream.WriteByte((byte)value);
    }

    public void WriteShort(short value) {
        WriteUShort((ushort)value);
    }

    public void WriteInt(int value) {
        WriteUInt((uint)value);
    }

    public void WriteFloat(float value) {
        WriteInt(BitConverter.SingleToInt32Bits(value));
    }

    public void WriteLong(long value) {
        WriteULong((ulong)value);
    }

    public void WriteDouble(double value) {
        WriteLong(BitConverter.DoubleToInt64Bits(value));
    }

    public byte[] ReadBytes(int length) {
        CheckRemaining(length);
        byte[] result = new byte[length];
        stream.Read(result, 0, length);
        return result;
    }

    public byte ReadByte() {
        CheckRemaining(1);
        return Convert.ToByte(stream.ReadByte());
    }

    public ushort ReadUShort() {
        CheckRemaining(2);
        return (ushort)(stream.ReadByte() << 8 | stream.ReadByte());
    }

    public uint ReadUInt() {
        CheckRemaining(4);
        return (uint)(stream.ReadByte() << 24
            | stream.ReadByte() << 16
            | stream.ReadByte() << 8
            | stream.ReadByte());
    }

    public ulong ReadULong() {
        return (ulong)ReadUInt() << 32 | (ulong)ReadUInt();
    }

    public sbyte ReadSByte() {
        return (sbyte)ReadByte();
    }

    public short ReadShort() {
        return (short)ReadUShort();
    }

    public int ReadInt() {
        return (int)ReadUInt();
    }

    public float ReadFloat() {
        return BitConverter.Int32BitsToSingle(ReadInt());
    }

    public long ReadLong() {
        return (long)ReadULong();
    }

    public double ReadDouble() {
        return BitConverter.Int64BitsToDouble(ReadLong());
    }

    private void CheckRemaining(int length) {
        if (stream.Length - stream.Position < length) {
            throw new StreamUnderflowException();
        }
    }
}

// Contains loading, initialization code,
// and the FFI Function declarations in a com.sun.jna.Library.


// This is an implementation detail which will be called internally by the public API.
static class _UniFFILib {
    static _UniFFILib() {
        
        }

    [DllImport("ldk_node")]
    public static extern void ffi_ldk_node_f89f_Builder_object_free(IntPtr @ptr,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern BuilderSafeHandle ldk_node_f89f_Builder_new(
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern BuilderSafeHandle ldk_node_f89f_Builder_from_config(RustBuffer @config,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ldk_node_f89f_Builder_set_entropy_seed_path(BuilderSafeHandle @ptr,RustBuffer @seedPath,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ldk_node_f89f_Builder_set_entropy_seed_bytes(BuilderSafeHandle @ptr,RustBuffer @seedBytes,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ldk_node_f89f_Builder_set_entropy_bip39_mnemonic(BuilderSafeHandle @ptr,RustBuffer @mnemonic,RustBuffer @passphrase,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ldk_node_f89f_Builder_set_esplora_server(BuilderSafeHandle @ptr,RustBuffer @esploraServerUrl,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ldk_node_f89f_Builder_set_gossip_source_p2p(BuilderSafeHandle @ptr,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ldk_node_f89f_Builder_set_gossip_source_rgs(BuilderSafeHandle @ptr,RustBuffer @rgsServerUrl,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ldk_node_f89f_Builder_set_storage_dir_path(BuilderSafeHandle @ptr,RustBuffer @storageDirPath,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ldk_node_f89f_Builder_set_network(BuilderSafeHandle @ptr,RustBuffer @network,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ldk_node_f89f_Builder_set_listening_address(BuilderSafeHandle @ptr,RustBuffer @listeningAddress,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern LdkNodeSafeHandle ldk_node_f89f_Builder_build(BuilderSafeHandle @ptr,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ffi_ldk_node_f89f_LdkNode_object_free(IntPtr @ptr,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ldk_node_f89f_LdkNode_start(LdkNodeSafeHandle @ptr,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ldk_node_f89f_LdkNode_stop(LdkNodeSafeHandle @ptr,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern RustBuffer ldk_node_f89f_LdkNode_next_event(LdkNodeSafeHandle @ptr,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern RustBuffer ldk_node_f89f_LdkNode_wait_next_event(LdkNodeSafeHandle @ptr,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ldk_node_f89f_LdkNode_event_handled(LdkNodeSafeHandle @ptr,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern RustBuffer ldk_node_f89f_LdkNode_node_id(LdkNodeSafeHandle @ptr,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern RustBuffer ldk_node_f89f_LdkNode_listening_address(LdkNodeSafeHandle @ptr,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern RustBuffer ldk_node_f89f_LdkNode_new_onchain_address(LdkNodeSafeHandle @ptr,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern RustBuffer ldk_node_f89f_LdkNode_send_to_onchain_address(LdkNodeSafeHandle @ptr,RustBuffer @address,ulong @amountMsat,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern RustBuffer ldk_node_f89f_LdkNode_send_all_to_onchain_address(LdkNodeSafeHandle @ptr,RustBuffer @address,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern ulong ldk_node_f89f_LdkNode_spendable_onchain_balance_sats(LdkNodeSafeHandle @ptr,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern ulong ldk_node_f89f_LdkNode_total_onchain_balance_sats(LdkNodeSafeHandle @ptr,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ldk_node_f89f_LdkNode_connect(LdkNodeSafeHandle @ptr,RustBuffer @nodeId,RustBuffer @address,sbyte @persist,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ldk_node_f89f_LdkNode_disconnect(LdkNodeSafeHandle @ptr,RustBuffer @nodeId,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ldk_node_f89f_LdkNode_connect_open_channel(LdkNodeSafeHandle @ptr,RustBuffer @nodeId,RustBuffer @address,ulong @channelAmountSats,RustBuffer @pushToCounterpartyMsat,RustBuffer @channelConfig,sbyte @announceChannel,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ldk_node_f89f_LdkNode_close_channel(LdkNodeSafeHandle @ptr,RustBuffer @channelId,RustBuffer @counterpartyNodeId,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ldk_node_f89f_LdkNode_update_channel_config(LdkNodeSafeHandle @ptr,RustBuffer @channelId,RustBuffer @counterpartyNodeId,ChannelConfigSafeHandle @channelConfig,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ldk_node_f89f_LdkNode_sync_wallets(LdkNodeSafeHandle @ptr,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern RustBuffer ldk_node_f89f_LdkNode_send_payment(LdkNodeSafeHandle @ptr,RustBuffer @invoice,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern RustBuffer ldk_node_f89f_LdkNode_send_payment_using_amount(LdkNodeSafeHandle @ptr,RustBuffer @invoice,ulong @amountMsat,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern RustBuffer ldk_node_f89f_LdkNode_send_spontaneous_payment(LdkNodeSafeHandle @ptr,ulong @amountMsat,RustBuffer @nodeId,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ldk_node_f89f_LdkNode_send_payment_probe(LdkNodeSafeHandle @ptr,RustBuffer @invoice,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ldk_node_f89f_LdkNode_send_spontaneous_payment_probe(LdkNodeSafeHandle @ptr,ulong @amountMsat,RustBuffer @nodeId,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern RustBuffer ldk_node_f89f_LdkNode_receive_payment(LdkNodeSafeHandle @ptr,ulong @amountMsat,RustBuffer @description,uint @expirySecs,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern RustBuffer ldk_node_f89f_LdkNode_receive_variable_amount_payment(LdkNodeSafeHandle @ptr,RustBuffer @description,uint @expirySecs,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern RustBuffer ldk_node_f89f_LdkNode_payment(LdkNodeSafeHandle @ptr,RustBuffer @paymentHash,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ldk_node_f89f_LdkNode_remove_payment(LdkNodeSafeHandle @ptr,RustBuffer @paymentHash,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern RustBuffer ldk_node_f89f_LdkNode_list_payments(LdkNodeSafeHandle @ptr,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern RustBuffer ldk_node_f89f_LdkNode_list_peers(LdkNodeSafeHandle @ptr,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern RustBuffer ldk_node_f89f_LdkNode_list_channels(LdkNodeSafeHandle @ptr,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern RustBuffer ldk_node_f89f_LdkNode_sign_message(LdkNodeSafeHandle @ptr,RustBuffer @msg,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern sbyte ldk_node_f89f_LdkNode_verify_signature(LdkNodeSafeHandle @ptr,RustBuffer @msg,RustBuffer @sig,RustBuffer @pkey,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ffi_ldk_node_f89f_ChannelConfig_object_free(IntPtr @ptr,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern ChannelConfigSafeHandle ldk_node_f89f_ChannelConfig_new(
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern uint ldk_node_f89f_ChannelConfig_forwarding_fee_proportional_millionths(ChannelConfigSafeHandle @ptr,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ldk_node_f89f_ChannelConfig_set_forwarding_fee_proportional_millionths(ChannelConfigSafeHandle @ptr,uint @value,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern uint ldk_node_f89f_ChannelConfig_forwarding_fee_base_msat(ChannelConfigSafeHandle @ptr,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ldk_node_f89f_ChannelConfig_set_forwarding_fee_base_msat(ChannelConfigSafeHandle @ptr,uint @feeMsat,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern ushort ldk_node_f89f_ChannelConfig_cltv_expiry_delta(ChannelConfigSafeHandle @ptr,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ldk_node_f89f_ChannelConfig_set_cltv_expiry_delta(ChannelConfigSafeHandle @ptr,ushort @value,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern ulong ldk_node_f89f_ChannelConfig_force_close_avoidance_max_fee_satoshis(ChannelConfigSafeHandle @ptr,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ldk_node_f89f_ChannelConfig_set_force_close_avoidance_max_fee_satoshis(ChannelConfigSafeHandle @ptr,ulong @valueSat,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern sbyte ldk_node_f89f_ChannelConfig_accept_underpaying_htlcs(ChannelConfigSafeHandle @ptr,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ldk_node_f89f_ChannelConfig_set_accept_underpaying_htlcs(ChannelConfigSafeHandle @ptr,sbyte @value,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ldk_node_f89f_ChannelConfig_set_max_dust_htlc_exposure_from_fixed_limit(ChannelConfigSafeHandle @ptr,ulong @limitMsat,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ldk_node_f89f_ChannelConfig_set_max_dust_htlc_exposure_from_fee_rate_multiplier(ChannelConfigSafeHandle @ptr,ulong @multiplier,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern RustBuffer ldk_node_f89f_generate_entropy_mnemonic(
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern RustBuffer ffi_ldk_node_f89f_rustbuffer_alloc(int @size,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern RustBuffer ffi_ldk_node_f89f_rustbuffer_from_bytes(ForeignBytes @bytes,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern void ffi_ldk_node_f89f_rustbuffer_free(RustBuffer @buf,
    ref RustCallStatus _uniffi_out_err
    );

    [DllImport("ldk_node")]
    public static extern RustBuffer ffi_ldk_node_f89f_rustbuffer_reserve(RustBuffer @buf,int @additional,
    ref RustCallStatus _uniffi_out_err
    );

    
}

// Public interface members begin here.

#pragma warning disable 8625




class FfiConverterByte: FfiConverter<byte, byte> {
    public static FfiConverterByte INSTANCE = new FfiConverterByte();

    public override byte Lift(byte value) {
        return value;
    }

    public override byte Read(BigEndianStream stream) {
        return stream.ReadByte();
    }

    public override byte Lower(byte value) {
        return value;
    }

    public override int AllocationSize(byte value) {
        return 1;
    }

    public override void Write(byte value, BigEndianStream stream) {
        stream.WriteByte(value);
    }
}



class FfiConverterUShort: FfiConverter<ushort, ushort> {
    public static FfiConverterUShort INSTANCE = new FfiConverterUShort();

    public override ushort Lift(ushort value) {
        return value;
    }

    public override ushort Read(BigEndianStream stream) {
        return stream.ReadUShort();
    }

    public override ushort Lower(ushort value) {
        return value;
    }

    public override int AllocationSize(ushort value) {
        return 2;
    }

    public override void Write(ushort value, BigEndianStream stream) {
        stream.WriteUShort(value);
    }
}



class FfiConverterUInt: FfiConverter<uint, uint> {
    public static FfiConverterUInt INSTANCE = new FfiConverterUInt();

    public override uint Lift(uint value) {
        return value;
    }

    public override uint Read(BigEndianStream stream) {
        return stream.ReadUInt();
    }

    public override uint Lower(uint value) {
        return value;
    }

    public override int AllocationSize(uint value) {
        return 4;
    }

    public override void Write(uint value, BigEndianStream stream) {
        stream.WriteUInt(value);
    }
}



class FfiConverterULong: FfiConverter<ulong, ulong> {
    public static FfiConverterULong INSTANCE = new FfiConverterULong();

    public override ulong Lift(ulong value) {
        return value;
    }

    public override ulong Read(BigEndianStream stream) {
        return stream.ReadULong();
    }

    public override ulong Lower(ulong value) {
        return value;
    }

    public override int AllocationSize(ulong value) {
        return 8;
    }

    public override void Write(ulong value, BigEndianStream stream) {
        stream.WriteULong(value);
    }
}



class FfiConverterBoolean: FfiConverter<bool, sbyte> {
    public static FfiConverterBoolean INSTANCE = new FfiConverterBoolean();

    public override bool Lift(sbyte value) {
        return value != 0;
    }

    public override bool Read(BigEndianStream stream) {
        return Lift(stream.ReadSByte());
    }

    public override sbyte Lower(bool value) {
        return value ? (sbyte)1 : (sbyte)0;
    }

    public override int AllocationSize(bool value) {
        return (sbyte)1;
    }

    public override void Write(bool value, BigEndianStream stream) {
        stream.WriteSByte(Lower(value));
    }
}



class FfiConverterString: FfiConverter<string, RustBuffer> {
    public static FfiConverterString INSTANCE = new FfiConverterString();

    // Note: we don't inherit from FfiConverterRustBuffer, because we use a
    // special encoding when lowering/lifting.  We can use `RustBuffer.len` to
    // store our length and avoid writing it out to the buffer.
    public override string Lift(RustBuffer value) {
        try {
            var bytes = value.AsStream().ReadBytes(value.len);
            return System.Text.Encoding.UTF8.GetString(bytes);
        } finally {
            RustBuffer.Free(value);
        }
    }

    public override string Read(BigEndianStream stream) {
        var length = stream.ReadInt();
        var bytes = stream.ReadBytes(length);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    public override RustBuffer Lower(string value) {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        var rbuf = RustBuffer.Alloc(bytes.Length);
        rbuf.AsWriteableStream().WriteBytes(bytes);
        return rbuf;
    }

    // TODO(CS)
    // We aren't sure exactly how many bytes our string will be once it's UTF-8
    // encoded.  Allocate 3 bytes per unicode codepoint which will always be
    // enough.
    public override int AllocationSize(string value) {
        const int sizeForLength = 4;
        var sizeForString = value.Length * 3;
        return sizeForLength + sizeForString;
    }

    public override void Write(string value, BigEndianStream stream) {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        stream.WriteInt(bytes.Length);
        stream.WriteBytes(bytes);
    }
}




// `SafeHandle` implements the semantics outlined below, i.e. its thread safe, and the dispose
// method will only be called once, once all outstanding native calls have completed.
// https://github.com/mozilla/uniffi-rs/blob/0dc031132d9493ca812c3af6e7dd60ad2ea95bf0/uniffi_bindgen/src/bindings/kotlin/templates/ObjectRuntime.kt#L31
// https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.criticalhandle

public abstract class FFIObject<THandle>: IDisposable where THandle : FFISafeHandle {
    private THandle handle;

    public FFIObject(THandle handle) {
        this.handle = handle;
    }

    public THandle GetHandle() {
        return handle;
    }

    public void Dispose() {
        handle.Dispose();
    }
}

public abstract class FFISafeHandle: SafeHandle {
    public FFISafeHandle(): base(new IntPtr(0), true) {
    }

    public FFISafeHandle(IntPtr pointer): this() {
        this.SetHandle(pointer);
    }

    public override bool IsInvalid {
        get {
            return handle.ToInt64() == 0;
        }
    }

    // TODO(CS) this completely breaks any guarantees offered by SafeHandle.. Extracting
    // raw value from SafeHandle puts responsiblity on the consumer of this function to
    // ensure that SafeHandle outlives the stream, and anyone who might have read the raw
    // value from the stream and are holding onto it. Otherwise, the result might be a use
    // after free, or free while method calls are still in flight.
    //
    // This is also relevant for Kotlin.
    //
    public IntPtr DangerousGetRawFfiValue() {
        return handle;
    }
}

static class FFIObjectUtil {
    public static void DisposeAll(params Object?[] list) {
        foreach (var obj in list) {
            Dispose(obj);
        }
    }

    // Dispose is implemented by recursive type inspection at runtime. This is because
    // generating correct Dispose calls for recursive complex types, e.g. List<List<int>>
    // is quite cumbersome.
    private static void Dispose(dynamic? obj) {
        if (obj == null) {
            return;
        }

        if (obj is IDisposable disposable) {
            disposable.Dispose();
            return;
        }

        var type = obj.GetType();
        if (type != null) {
            if (type.IsGenericType) {
                if (type.GetGenericTypeDefinition().IsAssignableFrom(typeof(List<>))) {
                    foreach (var value in obj) {
                        Dispose(value);
                    }
                } else if (type.GetGenericTypeDefinition().IsAssignableFrom(typeof(Dictionary<,>))) {
                    foreach (var value in obj.Values) {
                        Dispose(value);
                    }
                }
            }
        }
    }
}
public interface IBuilder {
    
    void SetEntropySeedPath(String @seedPath);
    
    /// <exception cref="BuildException"></exception>
    void SetEntropySeedBytes(List<Byte> @seedBytes);
    
    void SetEntropyBip39Mnemonic(Mnemonic @mnemonic, String? @passphrase);
    
    void SetEsploraServer(String @esploraServerUrl);
    
    void SetGossipSourceP2p();
    
    void SetGossipSourceRgs(String @rgsServerUrl);
    
    void SetStorageDirPath(String @storageDirPath);
    
    void SetNetwork(Network @network);
    
    void SetListeningAddress(NetAddress @listeningAddress);
    
    /// <exception cref="BuildException"></exception>
    LdkNode Build();
    
}

public class BuilderSafeHandle: FFISafeHandle {
    public BuilderSafeHandle(): base() {
    }
    public BuilderSafeHandle(IntPtr pointer): base(pointer) {
    }
    override protected bool ReleaseHandle() {
        _UniffiHelpers.RustCall((ref RustCallStatus status) => {
            _UniFFILib.ffi_ldk_node_f89f_Builder_object_free(this.handle, ref status);
        });
        return true;
    }
}
public class Builder: FFIObject<BuilderSafeHandle>, IBuilder {
    public Builder(BuilderSafeHandle pointer): base(pointer) {}
    public Builder() :
        this(
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_Builder_new( ref _status)
)) {}

    
    public void SetEntropySeedPath(String @seedPath) {
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_Builder_set_entropy_seed_path(this.GetHandle(), FfiConverterString.INSTANCE.Lower(@seedPath), ref _status)
);
    }
    
    
    /// <exception cref="BuildException"></exception>
    public void SetEntropySeedBytes(List<Byte> @seedBytes) {
    _UniffiHelpers.RustCallWithError(FfiConverterTypeBuildError.INSTANCE, (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_Builder_set_entropy_seed_bytes(this.GetHandle(), FfiConverterSequenceByte.INSTANCE.Lower(@seedBytes), ref _status)
);
    }
    
    
    public void SetEntropyBip39Mnemonic(Mnemonic @mnemonic, String? @passphrase) {
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_Builder_set_entropy_bip39_mnemonic(this.GetHandle(), FfiConverterTypeMnemonic.INSTANCE.Lower(@mnemonic), FfiConverterOptionalString.INSTANCE.Lower(@passphrase), ref _status)
);
    }
    
    
    public void SetEsploraServer(String @esploraServerUrl) {
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_Builder_set_esplora_server(this.GetHandle(), FfiConverterString.INSTANCE.Lower(@esploraServerUrl), ref _status)
);
    }
    
    
    public void SetGossipSourceP2p() {
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_Builder_set_gossip_source_p2p(this.GetHandle(),  ref _status)
);
    }
    
    
    public void SetGossipSourceRgs(String @rgsServerUrl) {
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_Builder_set_gossip_source_rgs(this.GetHandle(), FfiConverterString.INSTANCE.Lower(@rgsServerUrl), ref _status)
);
    }
    
    
    public void SetStorageDirPath(String @storageDirPath) {
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_Builder_set_storage_dir_path(this.GetHandle(), FfiConverterString.INSTANCE.Lower(@storageDirPath), ref _status)
);
    }
    
    
    public void SetNetwork(Network @network) {
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_Builder_set_network(this.GetHandle(), FfiConverterTypeNetwork.INSTANCE.Lower(@network), ref _status)
);
    }
    
    
    public void SetListeningAddress(NetAddress @listeningAddress) {
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_Builder_set_listening_address(this.GetHandle(), FfiConverterTypeNetAddress.INSTANCE.Lower(@listeningAddress), ref _status)
);
    }
    
    
    /// <exception cref="BuildException"></exception>
    public LdkNode Build() {
        return FfiConverterTypeLdkNode.INSTANCE.Lift(
    _UniffiHelpers.RustCallWithError(FfiConverterTypeBuildError.INSTANCE, (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_Builder_build(this.GetHandle(),  ref _status)
));
    }
    

    
    public static Builder FromConfig(Config @config) {
        return new Builder(
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_Builder_from_config(FfiConverterTypeConfig.INSTANCE.Lower(@config), ref _status)
));
    }
    
    
}

class FfiConverterTypeBuilder: FfiConverter<Builder, BuilderSafeHandle> {
    public static FfiConverterTypeBuilder INSTANCE = new FfiConverterTypeBuilder();

    public override BuilderSafeHandle Lower(Builder value) {
        return value.GetHandle();
    }

    public override Builder Lift(BuilderSafeHandle value) {
        return new Builder(value);
    }

    public override Builder Read(BigEndianStream stream) {
        return Lift(new BuilderSafeHandle(new IntPtr(stream.ReadLong())));
    }

    public override int AllocationSize(Builder value) {
        return 8;
    }

    public override void Write(Builder value, BigEndianStream stream) {
        stream.WriteLong(Lower(value).DangerousGetRawFfiValue().ToInt64());
    }
}



public interface IChannelConfig {
    
    UInt32 ForwardingFeeProportionalMillionths();
    
    void SetForwardingFeeProportionalMillionths(UInt32 @value);
    
    UInt32 ForwardingFeeBaseMsat();
    
    void SetForwardingFeeBaseMsat(UInt32 @feeMsat);
    
    UInt16 CltvExpiryDelta();
    
    void SetCltvExpiryDelta(UInt16 @value);
    
    UInt64 ForceCloseAvoidanceMaxFeeSatoshis();
    
    void SetForceCloseAvoidanceMaxFeeSatoshis(UInt64 @valueSat);
    
    Boolean AcceptUnderpayingHtlcs();
    
    void SetAcceptUnderpayingHtlcs(Boolean @value);
    
    void SetMaxDustHtlcExposureFromFixedLimit(UInt64 @limitMsat);
    
    void SetMaxDustHtlcExposureFromFeeRateMultiplier(UInt64 @multiplier);
    
}

public class ChannelConfigSafeHandle: FFISafeHandle {
    public ChannelConfigSafeHandle(): base() {
    }
    public ChannelConfigSafeHandle(IntPtr pointer): base(pointer) {
    }
    override protected bool ReleaseHandle() {
        _UniffiHelpers.RustCall((ref RustCallStatus status) => {
            _UniFFILib.ffi_ldk_node_f89f_ChannelConfig_object_free(this.handle, ref status);
        });
        return true;
    }
}
public class ChannelConfig: FFIObject<ChannelConfigSafeHandle>, IChannelConfig {
    public ChannelConfig(ChannelConfigSafeHandle pointer): base(pointer) {}
    public ChannelConfig() :
        this(
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_ChannelConfig_new( ref _status)
)) {}

    
    public UInt32 ForwardingFeeProportionalMillionths() {
        return FfiConverterUInt.INSTANCE.Lift(
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_ChannelConfig_forwarding_fee_proportional_millionths(this.GetHandle(),  ref _status)
));
    }
    
    public void SetForwardingFeeProportionalMillionths(UInt32 @value) {
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_ChannelConfig_set_forwarding_fee_proportional_millionths(this.GetHandle(), FfiConverterUInt.INSTANCE.Lower(@value), ref _status)
);
    }
    
    
    public UInt32 ForwardingFeeBaseMsat() {
        return FfiConverterUInt.INSTANCE.Lift(
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_ChannelConfig_forwarding_fee_base_msat(this.GetHandle(),  ref _status)
));
    }
    
    public void SetForwardingFeeBaseMsat(UInt32 @feeMsat) {
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_ChannelConfig_set_forwarding_fee_base_msat(this.GetHandle(), FfiConverterUInt.INSTANCE.Lower(@feeMsat), ref _status)
);
    }
    
    
    public UInt16 CltvExpiryDelta() {
        return FfiConverterUShort.INSTANCE.Lift(
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_ChannelConfig_cltv_expiry_delta(this.GetHandle(),  ref _status)
));
    }
    
    public void SetCltvExpiryDelta(UInt16 @value) {
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_ChannelConfig_set_cltv_expiry_delta(this.GetHandle(), FfiConverterUShort.INSTANCE.Lower(@value), ref _status)
);
    }
    
    
    public UInt64 ForceCloseAvoidanceMaxFeeSatoshis() {
        return FfiConverterULong.INSTANCE.Lift(
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_ChannelConfig_force_close_avoidance_max_fee_satoshis(this.GetHandle(),  ref _status)
));
    }
    
    public void SetForceCloseAvoidanceMaxFeeSatoshis(UInt64 @valueSat) {
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_ChannelConfig_set_force_close_avoidance_max_fee_satoshis(this.GetHandle(), FfiConverterULong.INSTANCE.Lower(@valueSat), ref _status)
);
    }
    
    
    public Boolean AcceptUnderpayingHtlcs() {
        return FfiConverterBoolean.INSTANCE.Lift(
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_ChannelConfig_accept_underpaying_htlcs(this.GetHandle(),  ref _status)
));
    }
    
    public void SetAcceptUnderpayingHtlcs(Boolean @value) {
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_ChannelConfig_set_accept_underpaying_htlcs(this.GetHandle(), FfiConverterBoolean.INSTANCE.Lower(@value), ref _status)
);
    }
    
    
    public void SetMaxDustHtlcExposureFromFixedLimit(UInt64 @limitMsat) {
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_ChannelConfig_set_max_dust_htlc_exposure_from_fixed_limit(this.GetHandle(), FfiConverterULong.INSTANCE.Lower(@limitMsat), ref _status)
);
    }
    
    
    public void SetMaxDustHtlcExposureFromFeeRateMultiplier(UInt64 @multiplier) {
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_ChannelConfig_set_max_dust_htlc_exposure_from_fee_rate_multiplier(this.GetHandle(), FfiConverterULong.INSTANCE.Lower(@multiplier), ref _status)
);
    }
    
    

    
}

class FfiConverterTypeChannelConfig: FfiConverter<ChannelConfig, ChannelConfigSafeHandle> {
    public static FfiConverterTypeChannelConfig INSTANCE = new FfiConverterTypeChannelConfig();

    public override ChannelConfigSafeHandle Lower(ChannelConfig value) {
        return value.GetHandle();
    }

    public override ChannelConfig Lift(ChannelConfigSafeHandle value) {
        return new ChannelConfig(value);
    }

    public override ChannelConfig Read(BigEndianStream stream) {
        return Lift(new ChannelConfigSafeHandle(new IntPtr(stream.ReadLong())));
    }

    public override int AllocationSize(ChannelConfig value) {
        return 8;
    }

    public override void Write(ChannelConfig value, BigEndianStream stream) {
        stream.WriteLong(Lower(value).DangerousGetRawFfiValue().ToInt64());
    }
}



public interface ILdkNode {
    
    /// <exception cref="NodeException"></exception>
    void Start();
    
    /// <exception cref="NodeException"></exception>
    void Stop();
    
    Event? NextEvent();
    
    Event WaitNextEvent();
    
    void EventHandled();
    
    PublicKey NodeId();
    
    NetAddress? ListeningAddress();
    
    /// <exception cref="NodeException"></exception>
    Address NewOnchainAddress();
    
    /// <exception cref="NodeException"></exception>
    Txid SendToOnchainAddress(Address @address, UInt64 @amountMsat);
    
    /// <exception cref="NodeException"></exception>
    Txid SendAllToOnchainAddress(Address @address);
    
    /// <exception cref="NodeException"></exception>
    UInt64 SpendableOnchainBalanceSats();
    
    /// <exception cref="NodeException"></exception>
    UInt64 TotalOnchainBalanceSats();
    
    /// <exception cref="NodeException"></exception>
    void Connect(PublicKey @nodeId, NetAddress @address, Boolean @persist);
    
    /// <exception cref="NodeException"></exception>
    void Disconnect(PublicKey @nodeId);
    
    /// <exception cref="NodeException"></exception>
    void ConnectOpenChannel(PublicKey @nodeId, NetAddress @address, UInt64 @channelAmountSats, UInt64? @pushToCounterpartyMsat, ChannelConfig? @channelConfig, Boolean @announceChannel);
    
    /// <exception cref="NodeException"></exception>
    void CloseChannel(ChannelId @channelId, PublicKey @counterpartyNodeId);
    
    /// <exception cref="NodeException"></exception>
    void UpdateChannelConfig(ChannelId @channelId, PublicKey @counterpartyNodeId, ChannelConfig @channelConfig);
    
    /// <exception cref="NodeException"></exception>
    void SyncWallets();
    
    /// <exception cref="NodeException"></exception>
    PaymentHash SendPayment(Bolt11Invoice @invoice);
    
    /// <exception cref="NodeException"></exception>
    PaymentHash SendPaymentUsingAmount(Bolt11Invoice @invoice, UInt64 @amountMsat);
    
    /// <exception cref="NodeException"></exception>
    PaymentHash SendSpontaneousPayment(UInt64 @amountMsat, PublicKey @nodeId);
    
    /// <exception cref="NodeException"></exception>
    void SendPaymentProbe(Bolt11Invoice @invoice);
    
    /// <exception cref="NodeException"></exception>
    void SendSpontaneousPaymentProbe(UInt64 @amountMsat, PublicKey @nodeId);
    
    /// <exception cref="NodeException"></exception>
    Bolt11Invoice ReceivePayment(UInt64 @amountMsat, String @description, UInt32 @expirySecs);
    
    /// <exception cref="NodeException"></exception>
    Bolt11Invoice ReceiveVariableAmountPayment(String @description, UInt32 @expirySecs);
    
    PaymentDetails? Payment(PaymentHash @paymentHash);
    
    /// <exception cref="NodeException"></exception>
    void RemovePayment(PaymentHash @paymentHash);
    
    List<PaymentDetails> ListPayments();
    
    List<PeerDetails> ListPeers();
    
    List<ChannelDetails> ListChannels();
    
    /// <exception cref="NodeException"></exception>
    String SignMessage(List<Byte> @msg);
    
    Boolean VerifySignature(List<Byte> @msg, String @sig, PublicKey @pkey);
    
}

public class LdkNodeSafeHandle: FFISafeHandle {
    public LdkNodeSafeHandle(): base() {
    }
    public LdkNodeSafeHandle(IntPtr pointer): base(pointer) {
    }
    override protected bool ReleaseHandle() {
        _UniffiHelpers.RustCall((ref RustCallStatus status) => {
            _UniFFILib.ffi_ldk_node_f89f_LdkNode_object_free(this.handle, ref status);
        });
        return true;
    }
}
public class LdkNode: FFIObject<LdkNodeSafeHandle>, ILdkNode {
    public LdkNode(LdkNodeSafeHandle pointer): base(pointer) {}

    
    /// <exception cref="NodeException"></exception>
    public void Start() {
    _UniffiHelpers.RustCallWithError(FfiConverterTypeNodeError.INSTANCE, (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_start(this.GetHandle(),  ref _status)
);
    }
    
    
    /// <exception cref="NodeException"></exception>
    public void Stop() {
    _UniffiHelpers.RustCallWithError(FfiConverterTypeNodeError.INSTANCE, (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_stop(this.GetHandle(),  ref _status)
);
    }
    
    
    public Event? NextEvent() {
        return FfiConverterOptionalTypeEvent.INSTANCE.Lift(
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_next_event(this.GetHandle(),  ref _status)
));
    }
    
    public Event WaitNextEvent() {
        return FfiConverterTypeEvent.INSTANCE.Lift(
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_wait_next_event(this.GetHandle(),  ref _status)
));
    }
    
    public void EventHandled() {
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_event_handled(this.GetHandle(),  ref _status)
);
    }
    
    
    public PublicKey NodeId() {
        return FfiConverterTypePublicKey.INSTANCE.Lift(
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_node_id(this.GetHandle(),  ref _status)
));
    }
    
    public NetAddress? ListeningAddress() {
        return FfiConverterOptionalTypeNetAddress.INSTANCE.Lift(
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_listening_address(this.GetHandle(),  ref _status)
));
    }
    
    /// <exception cref="NodeException"></exception>
    public Address NewOnchainAddress() {
        return FfiConverterTypeAddress.INSTANCE.Lift(
    _UniffiHelpers.RustCallWithError(FfiConverterTypeNodeError.INSTANCE, (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_new_onchain_address(this.GetHandle(),  ref _status)
));
    }
    
    /// <exception cref="NodeException"></exception>
    public Txid SendToOnchainAddress(Address @address, UInt64 @amountMsat) {
        return FfiConverterTypeTxid.INSTANCE.Lift(
    _UniffiHelpers.RustCallWithError(FfiConverterTypeNodeError.INSTANCE, (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_send_to_onchain_address(this.GetHandle(), FfiConverterTypeAddress.INSTANCE.Lower(@address), FfiConverterULong.INSTANCE.Lower(@amountMsat), ref _status)
));
    }
    
    /// <exception cref="NodeException"></exception>
    public Txid SendAllToOnchainAddress(Address @address) {
        return FfiConverterTypeTxid.INSTANCE.Lift(
    _UniffiHelpers.RustCallWithError(FfiConverterTypeNodeError.INSTANCE, (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_send_all_to_onchain_address(this.GetHandle(), FfiConverterTypeAddress.INSTANCE.Lower(@address), ref _status)
));
    }
    
    /// <exception cref="NodeException"></exception>
    public UInt64 SpendableOnchainBalanceSats() {
        return FfiConverterULong.INSTANCE.Lift(
    _UniffiHelpers.RustCallWithError(FfiConverterTypeNodeError.INSTANCE, (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_spendable_onchain_balance_sats(this.GetHandle(),  ref _status)
));
    }
    
    /// <exception cref="NodeException"></exception>
    public UInt64 TotalOnchainBalanceSats() {
        return FfiConverterULong.INSTANCE.Lift(
    _UniffiHelpers.RustCallWithError(FfiConverterTypeNodeError.INSTANCE, (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_total_onchain_balance_sats(this.GetHandle(),  ref _status)
));
    }
    
    /// <exception cref="NodeException"></exception>
    public void Connect(PublicKey @nodeId, NetAddress @address, Boolean @persist) {
    _UniffiHelpers.RustCallWithError(FfiConverterTypeNodeError.INSTANCE, (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_connect(this.GetHandle(), FfiConverterTypePublicKey.INSTANCE.Lower(@nodeId), FfiConverterTypeNetAddress.INSTANCE.Lower(@address), FfiConverterBoolean.INSTANCE.Lower(@persist), ref _status)
);
    }
    
    
    /// <exception cref="NodeException"></exception>
    public void Disconnect(PublicKey @nodeId) {
    _UniffiHelpers.RustCallWithError(FfiConverterTypeNodeError.INSTANCE, (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_disconnect(this.GetHandle(), FfiConverterTypePublicKey.INSTANCE.Lower(@nodeId), ref _status)
);
    }
    
    
    /// <exception cref="NodeException"></exception>
    public void ConnectOpenChannel(PublicKey @nodeId, NetAddress @address, UInt64 @channelAmountSats, UInt64? @pushToCounterpartyMsat, ChannelConfig? @channelConfig, Boolean @announceChannel) {
    _UniffiHelpers.RustCallWithError(FfiConverterTypeNodeError.INSTANCE, (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_connect_open_channel(this.GetHandle(), FfiConverterTypePublicKey.INSTANCE.Lower(@nodeId), FfiConverterTypeNetAddress.INSTANCE.Lower(@address), FfiConverterULong.INSTANCE.Lower(@channelAmountSats), FfiConverterOptionalULong.INSTANCE.Lower(@pushToCounterpartyMsat), FfiConverterOptionalTypeChannelConfig.INSTANCE.Lower(@channelConfig), FfiConverterBoolean.INSTANCE.Lower(@announceChannel), ref _status)
);
    }
    
    
    /// <exception cref="NodeException"></exception>
    public void CloseChannel(ChannelId @channelId, PublicKey @counterpartyNodeId) {
    _UniffiHelpers.RustCallWithError(FfiConverterTypeNodeError.INSTANCE, (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_close_channel(this.GetHandle(), FfiConverterTypeChannelId.INSTANCE.Lower(@channelId), FfiConverterTypePublicKey.INSTANCE.Lower(@counterpartyNodeId), ref _status)
);
    }
    
    
    /// <exception cref="NodeException"></exception>
    public void UpdateChannelConfig(ChannelId @channelId, PublicKey @counterpartyNodeId, ChannelConfig @channelConfig) {
    _UniffiHelpers.RustCallWithError(FfiConverterTypeNodeError.INSTANCE, (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_update_channel_config(this.GetHandle(), FfiConverterTypeChannelId.INSTANCE.Lower(@channelId), FfiConverterTypePublicKey.INSTANCE.Lower(@counterpartyNodeId), FfiConverterTypeChannelConfig.INSTANCE.Lower(@channelConfig), ref _status)
);
    }
    
    
    /// <exception cref="NodeException"></exception>
    public void SyncWallets() {
    _UniffiHelpers.RustCallWithError(FfiConverterTypeNodeError.INSTANCE, (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_sync_wallets(this.GetHandle(),  ref _status)
);
    }
    
    
    /// <exception cref="NodeException"></exception>
    public PaymentHash SendPayment(Bolt11Invoice @invoice) {
        return FfiConverterTypePaymentHash.INSTANCE.Lift(
    _UniffiHelpers.RustCallWithError(FfiConverterTypeNodeError.INSTANCE, (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_send_payment(this.GetHandle(), FfiConverterTypeBolt11Invoice.INSTANCE.Lower(@invoice), ref _status)
));
    }
    
    /// <exception cref="NodeException"></exception>
    public PaymentHash SendPaymentUsingAmount(Bolt11Invoice @invoice, UInt64 @amountMsat) {
        return FfiConverterTypePaymentHash.INSTANCE.Lift(
    _UniffiHelpers.RustCallWithError(FfiConverterTypeNodeError.INSTANCE, (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_send_payment_using_amount(this.GetHandle(), FfiConverterTypeBolt11Invoice.INSTANCE.Lower(@invoice), FfiConverterULong.INSTANCE.Lower(@amountMsat), ref _status)
));
    }
    
    /// <exception cref="NodeException"></exception>
    public PaymentHash SendSpontaneousPayment(UInt64 @amountMsat, PublicKey @nodeId) {
        return FfiConverterTypePaymentHash.INSTANCE.Lift(
    _UniffiHelpers.RustCallWithError(FfiConverterTypeNodeError.INSTANCE, (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_send_spontaneous_payment(this.GetHandle(), FfiConverterULong.INSTANCE.Lower(@amountMsat), FfiConverterTypePublicKey.INSTANCE.Lower(@nodeId), ref _status)
));
    }
    
    /// <exception cref="NodeException"></exception>
    public void SendPaymentProbe(Bolt11Invoice @invoice) {
    _UniffiHelpers.RustCallWithError(FfiConverterTypeNodeError.INSTANCE, (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_send_payment_probe(this.GetHandle(), FfiConverterTypeBolt11Invoice.INSTANCE.Lower(@invoice), ref _status)
);
    }
    
    
    /// <exception cref="NodeException"></exception>
    public void SendSpontaneousPaymentProbe(UInt64 @amountMsat, PublicKey @nodeId) {
    _UniffiHelpers.RustCallWithError(FfiConverterTypeNodeError.INSTANCE, (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_send_spontaneous_payment_probe(this.GetHandle(), FfiConverterULong.INSTANCE.Lower(@amountMsat), FfiConverterTypePublicKey.INSTANCE.Lower(@nodeId), ref _status)
);
    }
    
    
    /// <exception cref="NodeException"></exception>
    public Bolt11Invoice ReceivePayment(UInt64 @amountMsat, String @description, UInt32 @expirySecs) {
        return FfiConverterTypeBolt11Invoice.INSTANCE.Lift(
    _UniffiHelpers.RustCallWithError(FfiConverterTypeNodeError.INSTANCE, (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_receive_payment(this.GetHandle(), FfiConverterULong.INSTANCE.Lower(@amountMsat), FfiConverterString.INSTANCE.Lower(@description), FfiConverterUInt.INSTANCE.Lower(@expirySecs), ref _status)
));
    }
    
    /// <exception cref="NodeException"></exception>
    public Bolt11Invoice ReceiveVariableAmountPayment(String @description, UInt32 @expirySecs) {
        return FfiConverterTypeBolt11Invoice.INSTANCE.Lift(
    _UniffiHelpers.RustCallWithError(FfiConverterTypeNodeError.INSTANCE, (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_receive_variable_amount_payment(this.GetHandle(), FfiConverterString.INSTANCE.Lower(@description), FfiConverterUInt.INSTANCE.Lower(@expirySecs), ref _status)
));
    }
    
    public PaymentDetails? Payment(PaymentHash @paymentHash) {
        return FfiConverterOptionalTypePaymentDetails.INSTANCE.Lift(
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_payment(this.GetHandle(), FfiConverterTypePaymentHash.INSTANCE.Lower(@paymentHash), ref _status)
));
    }
    
    /// <exception cref="NodeException"></exception>
    public void RemovePayment(PaymentHash @paymentHash) {
    _UniffiHelpers.RustCallWithError(FfiConverterTypeNodeError.INSTANCE, (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_remove_payment(this.GetHandle(), FfiConverterTypePaymentHash.INSTANCE.Lower(@paymentHash), ref _status)
);
    }
    
    
    public List<PaymentDetails> ListPayments() {
        return FfiConverterSequenceTypePaymentDetails.INSTANCE.Lift(
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_list_payments(this.GetHandle(),  ref _status)
));
    }
    
    public List<PeerDetails> ListPeers() {
        return FfiConverterSequenceTypePeerDetails.INSTANCE.Lift(
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_list_peers(this.GetHandle(),  ref _status)
));
    }
    
    public List<ChannelDetails> ListChannels() {
        return FfiConverterSequenceTypeChannelDetails.INSTANCE.Lift(
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_list_channels(this.GetHandle(),  ref _status)
));
    }
    
    /// <exception cref="NodeException"></exception>
    public String SignMessage(List<Byte> @msg) {
        return FfiConverterString.INSTANCE.Lift(
    _UniffiHelpers.RustCallWithError(FfiConverterTypeNodeError.INSTANCE, (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_sign_message(this.GetHandle(), FfiConverterSequenceByte.INSTANCE.Lower(@msg), ref _status)
));
    }
    
    public Boolean VerifySignature(List<Byte> @msg, String @sig, PublicKey @pkey) {
        return FfiConverterBoolean.INSTANCE.Lift(
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_LdkNode_verify_signature(this.GetHandle(), FfiConverterSequenceByte.INSTANCE.Lower(@msg), FfiConverterString.INSTANCE.Lower(@sig), FfiConverterTypePublicKey.INSTANCE.Lower(@pkey), ref _status)
));
    }
    

    
}

class FfiConverterTypeLdkNode: FfiConverter<LdkNode, LdkNodeSafeHandle> {
    public static FfiConverterTypeLdkNode INSTANCE = new FfiConverterTypeLdkNode();

    public override LdkNodeSafeHandle Lower(LdkNode value) {
        return value.GetHandle();
    }

    public override LdkNode Lift(LdkNodeSafeHandle value) {
        return new LdkNode(value);
    }

    public override LdkNode Read(BigEndianStream stream) {
        return Lift(new LdkNodeSafeHandle(new IntPtr(stream.ReadLong())));
    }

    public override int AllocationSize(LdkNode value) {
        return 8;
    }

    public override void Write(LdkNode value, BigEndianStream stream) {
        stream.WriteLong(Lower(value).DangerousGetRawFfiValue().ToInt64());
    }
}



public record ChannelDetails (
    ChannelId @channelId, 
    PublicKey @counterpartyNodeId, 
    OutPoint? @fundingTxo, 
    UInt64 @channelValueSats, 
    UInt64? @unspendablePunishmentReserve, 
    UserChannelId @userChannelId, 
    UInt32 @feerateSatPer1000Weight, 
    UInt64 @balanceMsat, 
    UInt64 @outboundCapacityMsat, 
    UInt64 @inboundCapacityMsat, 
    UInt32? @confirmationsRequired, 
    UInt32? @confirmations, 
    Boolean @isOutbound, 
    Boolean @isChannelReady, 
    Boolean @isUsable, 
    Boolean @isPublic, 
    UInt16? @cltvExpiryDelta
) {
}

class FfiConverterTypeChannelDetails: FfiConverterRustBuffer<ChannelDetails> {
    public static FfiConverterTypeChannelDetails INSTANCE = new FfiConverterTypeChannelDetails();

    public override ChannelDetails Read(BigEndianStream stream) {
        return new ChannelDetails(
            FfiConverterTypeChannelId.INSTANCE.Read(stream),
            FfiConverterTypePublicKey.INSTANCE.Read(stream),
            FfiConverterOptionalTypeOutPoint.INSTANCE.Read(stream),
            FfiConverterULong.INSTANCE.Read(stream),
            FfiConverterOptionalULong.INSTANCE.Read(stream),
            FfiConverterTypeUserChannelId.INSTANCE.Read(stream),
            FfiConverterUInt.INSTANCE.Read(stream),
            FfiConverterULong.INSTANCE.Read(stream),
            FfiConverterULong.INSTANCE.Read(stream),
            FfiConverterULong.INSTANCE.Read(stream),
            FfiConverterOptionalUInt.INSTANCE.Read(stream),
            FfiConverterOptionalUInt.INSTANCE.Read(stream),
            FfiConverterBoolean.INSTANCE.Read(stream),
            FfiConverterBoolean.INSTANCE.Read(stream),
            FfiConverterBoolean.INSTANCE.Read(stream),
            FfiConverterBoolean.INSTANCE.Read(stream),
            FfiConverterOptionalUShort.INSTANCE.Read(stream)
        );
    }

    public override int AllocationSize(ChannelDetails value) {
        return
            FfiConverterTypeChannelId.INSTANCE.AllocationSize(value.@channelId) +
            FfiConverterTypePublicKey.INSTANCE.AllocationSize(value.@counterpartyNodeId) +
            FfiConverterOptionalTypeOutPoint.INSTANCE.AllocationSize(value.@fundingTxo) +
            FfiConverterULong.INSTANCE.AllocationSize(value.@channelValueSats) +
            FfiConverterOptionalULong.INSTANCE.AllocationSize(value.@unspendablePunishmentReserve) +
            FfiConverterTypeUserChannelId.INSTANCE.AllocationSize(value.@userChannelId) +
            FfiConverterUInt.INSTANCE.AllocationSize(value.@feerateSatPer1000Weight) +
            FfiConverterULong.INSTANCE.AllocationSize(value.@balanceMsat) +
            FfiConverterULong.INSTANCE.AllocationSize(value.@outboundCapacityMsat) +
            FfiConverterULong.INSTANCE.AllocationSize(value.@inboundCapacityMsat) +
            FfiConverterOptionalUInt.INSTANCE.AllocationSize(value.@confirmationsRequired) +
            FfiConverterOptionalUInt.INSTANCE.AllocationSize(value.@confirmations) +
            FfiConverterBoolean.INSTANCE.AllocationSize(value.@isOutbound) +
            FfiConverterBoolean.INSTANCE.AllocationSize(value.@isChannelReady) +
            FfiConverterBoolean.INSTANCE.AllocationSize(value.@isUsable) +
            FfiConverterBoolean.INSTANCE.AllocationSize(value.@isPublic) +
            FfiConverterOptionalUShort.INSTANCE.AllocationSize(value.@cltvExpiryDelta);
    }

    public override void Write(ChannelDetails value, BigEndianStream stream) {
            FfiConverterTypeChannelId.INSTANCE.Write(value.@channelId, stream);
            FfiConverterTypePublicKey.INSTANCE.Write(value.@counterpartyNodeId, stream);
            FfiConverterOptionalTypeOutPoint.INSTANCE.Write(value.@fundingTxo, stream);
            FfiConverterULong.INSTANCE.Write(value.@channelValueSats, stream);
            FfiConverterOptionalULong.INSTANCE.Write(value.@unspendablePunishmentReserve, stream);
            FfiConverterTypeUserChannelId.INSTANCE.Write(value.@userChannelId, stream);
            FfiConverterUInt.INSTANCE.Write(value.@feerateSatPer1000Weight, stream);
            FfiConverterULong.INSTANCE.Write(value.@balanceMsat, stream);
            FfiConverterULong.INSTANCE.Write(value.@outboundCapacityMsat, stream);
            FfiConverterULong.INSTANCE.Write(value.@inboundCapacityMsat, stream);
            FfiConverterOptionalUInt.INSTANCE.Write(value.@confirmationsRequired, stream);
            FfiConverterOptionalUInt.INSTANCE.Write(value.@confirmations, stream);
            FfiConverterBoolean.INSTANCE.Write(value.@isOutbound, stream);
            FfiConverterBoolean.INSTANCE.Write(value.@isChannelReady, stream);
            FfiConverterBoolean.INSTANCE.Write(value.@isUsable, stream);
            FfiConverterBoolean.INSTANCE.Write(value.@isPublic, stream);
            FfiConverterOptionalUShort.INSTANCE.Write(value.@cltvExpiryDelta, stream);
    }
}



public record Config (
    String @storageDirPath = "/tmp/ldk_node/", 
    String? @logDirPath = null, 
    Network @network = Network.BITCOIN, 
    NetAddress? @listeningAddress = null, 
    UInt32 @defaultCltvExpiryDelta = 144, 
    UInt64 @onchainWalletSyncIntervalSecs = 80, 
    UInt64 @walletSyncIntervalSecs = 30, 
    UInt64 @feeRateCacheUpdateIntervalSecs = 600, 
    List<PublicKey> @trustedPeers0conf = null, 
    UInt64 @probingLiquidityLimitMultiplier = 3, 
    LogLevel @logLevel = LogLevel.DEBUG
) {
}

class FfiConverterTypeConfig: FfiConverterRustBuffer<Config> {
    public static FfiConverterTypeConfig INSTANCE = new FfiConverterTypeConfig();

    public override Config Read(BigEndianStream stream) {
        return new Config(
            FfiConverterString.INSTANCE.Read(stream),
            FfiConverterOptionalString.INSTANCE.Read(stream),
            FfiConverterTypeNetwork.INSTANCE.Read(stream),
            FfiConverterOptionalTypeNetAddress.INSTANCE.Read(stream),
            FfiConverterUInt.INSTANCE.Read(stream),
            FfiConverterULong.INSTANCE.Read(stream),
            FfiConverterULong.INSTANCE.Read(stream),
            FfiConverterULong.INSTANCE.Read(stream),
            FfiConverterSequenceTypePublicKey.INSTANCE.Read(stream),
            FfiConverterULong.INSTANCE.Read(stream),
            FfiConverterTypeLogLevel.INSTANCE.Read(stream)
        );
    }

    public override int AllocationSize(Config value) {
        return
            FfiConverterString.INSTANCE.AllocationSize(value.@storageDirPath) +
            FfiConverterOptionalString.INSTANCE.AllocationSize(value.@logDirPath) +
            FfiConverterTypeNetwork.INSTANCE.AllocationSize(value.@network) +
            FfiConverterOptionalTypeNetAddress.INSTANCE.AllocationSize(value.@listeningAddress) +
            FfiConverterUInt.INSTANCE.AllocationSize(value.@defaultCltvExpiryDelta) +
            FfiConverterULong.INSTANCE.AllocationSize(value.@onchainWalletSyncIntervalSecs) +
            FfiConverterULong.INSTANCE.AllocationSize(value.@walletSyncIntervalSecs) +
            FfiConverterULong.INSTANCE.AllocationSize(value.@feeRateCacheUpdateIntervalSecs) +
            FfiConverterSequenceTypePublicKey.INSTANCE.AllocationSize(value.@trustedPeers0conf) +
            FfiConverterULong.INSTANCE.AllocationSize(value.@probingLiquidityLimitMultiplier) +
            FfiConverterTypeLogLevel.INSTANCE.AllocationSize(value.@logLevel);
    }

    public override void Write(Config value, BigEndianStream stream) {
            FfiConverterString.INSTANCE.Write(value.@storageDirPath, stream);
            FfiConverterOptionalString.INSTANCE.Write(value.@logDirPath, stream);
            FfiConverterTypeNetwork.INSTANCE.Write(value.@network, stream);
            FfiConverterOptionalTypeNetAddress.INSTANCE.Write(value.@listeningAddress, stream);
            FfiConverterUInt.INSTANCE.Write(value.@defaultCltvExpiryDelta, stream);
            FfiConverterULong.INSTANCE.Write(value.@onchainWalletSyncIntervalSecs, stream);
            FfiConverterULong.INSTANCE.Write(value.@walletSyncIntervalSecs, stream);
            FfiConverterULong.INSTANCE.Write(value.@feeRateCacheUpdateIntervalSecs, stream);
            FfiConverterSequenceTypePublicKey.INSTANCE.Write(value.@trustedPeers0conf, stream);
            FfiConverterULong.INSTANCE.Write(value.@probingLiquidityLimitMultiplier, stream);
            FfiConverterTypeLogLevel.INSTANCE.Write(value.@logLevel, stream);
    }
}



public record OutPoint (
    Txid @txid, 
    UInt32 @vout
) {
}

class FfiConverterTypeOutPoint: FfiConverterRustBuffer<OutPoint> {
    public static FfiConverterTypeOutPoint INSTANCE = new FfiConverterTypeOutPoint();

    public override OutPoint Read(BigEndianStream stream) {
        return new OutPoint(
            FfiConverterTypeTxid.INSTANCE.Read(stream),
            FfiConverterUInt.INSTANCE.Read(stream)
        );
    }

    public override int AllocationSize(OutPoint value) {
        return
            FfiConverterTypeTxid.INSTANCE.AllocationSize(value.@txid) +
            FfiConverterUInt.INSTANCE.AllocationSize(value.@vout);
    }

    public override void Write(OutPoint value, BigEndianStream stream) {
            FfiConverterTypeTxid.INSTANCE.Write(value.@txid, stream);
            FfiConverterUInt.INSTANCE.Write(value.@vout, stream);
    }
}



public record PaymentDetails (
    PaymentHash @hash, 
    PaymentPreimage? @preimage, 
    PaymentSecret? @secret, 
    UInt64? @amountMsat, 
    PaymentDirection @direction, 
    PaymentStatus @status
) {
}

class FfiConverterTypePaymentDetails: FfiConverterRustBuffer<PaymentDetails> {
    public static FfiConverterTypePaymentDetails INSTANCE = new FfiConverterTypePaymentDetails();

    public override PaymentDetails Read(BigEndianStream stream) {
        return new PaymentDetails(
            FfiConverterTypePaymentHash.INSTANCE.Read(stream),
            FfiConverterOptionalTypePaymentPreimage.INSTANCE.Read(stream),
            FfiConverterOptionalTypePaymentSecret.INSTANCE.Read(stream),
            FfiConverterOptionalULong.INSTANCE.Read(stream),
            FfiConverterTypePaymentDirection.INSTANCE.Read(stream),
            FfiConverterTypePaymentStatus.INSTANCE.Read(stream)
        );
    }

    public override int AllocationSize(PaymentDetails value) {
        return
            FfiConverterTypePaymentHash.INSTANCE.AllocationSize(value.@hash) +
            FfiConverterOptionalTypePaymentPreimage.INSTANCE.AllocationSize(value.@preimage) +
            FfiConverterOptionalTypePaymentSecret.INSTANCE.AllocationSize(value.@secret) +
            FfiConverterOptionalULong.INSTANCE.AllocationSize(value.@amountMsat) +
            FfiConverterTypePaymentDirection.INSTANCE.AllocationSize(value.@direction) +
            FfiConverterTypePaymentStatus.INSTANCE.AllocationSize(value.@status);
    }

    public override void Write(PaymentDetails value, BigEndianStream stream) {
            FfiConverterTypePaymentHash.INSTANCE.Write(value.@hash, stream);
            FfiConverterOptionalTypePaymentPreimage.INSTANCE.Write(value.@preimage, stream);
            FfiConverterOptionalTypePaymentSecret.INSTANCE.Write(value.@secret, stream);
            FfiConverterOptionalULong.INSTANCE.Write(value.@amountMsat, stream);
            FfiConverterTypePaymentDirection.INSTANCE.Write(value.@direction, stream);
            FfiConverterTypePaymentStatus.INSTANCE.Write(value.@status, stream);
    }
}



public record PeerDetails (
    PublicKey @nodeId, 
    NetAddress @address, 
    Boolean @isPersisted, 
    Boolean @isConnected
) {
}

class FfiConverterTypePeerDetails: FfiConverterRustBuffer<PeerDetails> {
    public static FfiConverterTypePeerDetails INSTANCE = new FfiConverterTypePeerDetails();

    public override PeerDetails Read(BigEndianStream stream) {
        return new PeerDetails(
            FfiConverterTypePublicKey.INSTANCE.Read(stream),
            FfiConverterTypeNetAddress.INSTANCE.Read(stream),
            FfiConverterBoolean.INSTANCE.Read(stream),
            FfiConverterBoolean.INSTANCE.Read(stream)
        );
    }

    public override int AllocationSize(PeerDetails value) {
        return
            FfiConverterTypePublicKey.INSTANCE.AllocationSize(value.@nodeId) +
            FfiConverterTypeNetAddress.INSTANCE.AllocationSize(value.@address) +
            FfiConverterBoolean.INSTANCE.AllocationSize(value.@isPersisted) +
            FfiConverterBoolean.INSTANCE.AllocationSize(value.@isConnected);
    }

    public override void Write(PeerDetails value, BigEndianStream stream) {
            FfiConverterTypePublicKey.INSTANCE.Write(value.@nodeId, stream);
            FfiConverterTypeNetAddress.INSTANCE.Write(value.@address, stream);
            FfiConverterBoolean.INSTANCE.Write(value.@isPersisted, stream);
            FfiConverterBoolean.INSTANCE.Write(value.@isConnected, stream);
    }
}





public record Event {
    
    public record PaymentSuccessful (
        PaymentHash @paymentHash
    ) : Event {}
    
    public record PaymentFailed (
        PaymentHash @paymentHash
    ) : Event {}
    
    public record PaymentReceived (
        PaymentHash @paymentHash,UInt64 @amountMsat
    ) : Event {}
    
    public record ChannelPending (
        ChannelId @channelId,UserChannelId @userChannelId,ChannelId @formerTemporaryChannelId,PublicKey @counterpartyNodeId,OutPoint @fundingTxo
    ) : Event {}
    
    public record ChannelReady (
        ChannelId @channelId,UserChannelId @userChannelId
    ) : Event {}
    
    public record ChannelClosed (
        ChannelId @channelId,UserChannelId @userChannelId
    ) : Event {}
    

    
}

class FfiConverterTypeEvent : FfiConverterRustBuffer<Event>{
    public static FfiConverterRustBuffer<Event> INSTANCE = new FfiConverterTypeEvent();

    public override Event Read(BigEndianStream stream) {
        var value = stream.ReadInt();
        switch (value) {
            case 1:
                return new Event.PaymentSuccessful(
                    FfiConverterTypePaymentHash.INSTANCE.Read(stream)
                );
            case 2:
                return new Event.PaymentFailed(
                    FfiConverterTypePaymentHash.INSTANCE.Read(stream)
                );
            case 3:
                return new Event.PaymentReceived(
                    FfiConverterTypePaymentHash.INSTANCE.Read(stream),
                    FfiConverterULong.INSTANCE.Read(stream)
                );
            case 4:
                return new Event.ChannelPending(
                    FfiConverterTypeChannelId.INSTANCE.Read(stream),
                    FfiConverterTypeUserChannelId.INSTANCE.Read(stream),
                    FfiConverterTypeChannelId.INSTANCE.Read(stream),
                    FfiConverterTypePublicKey.INSTANCE.Read(stream),
                    FfiConverterTypeOutPoint.INSTANCE.Read(stream)
                );
            case 5:
                return new Event.ChannelReady(
                    FfiConverterTypeChannelId.INSTANCE.Read(stream),
                    FfiConverterTypeUserChannelId.INSTANCE.Read(stream)
                );
            case 6:
                return new Event.ChannelClosed(
                    FfiConverterTypeChannelId.INSTANCE.Read(stream),
                    FfiConverterTypeUserChannelId.INSTANCE.Read(stream)
                );
            default:
                throw new InternalException(String.Format("invalid enum value '{}' in FfiConverterTypeEvent.Read()", value));
        }
    }

    public override int AllocationSize(Event value) {
        switch (value) {
            case Event.PaymentSuccessful variant_value:
                return 4
                    + FfiConverterTypePaymentHash.INSTANCE.AllocationSize(variant_value.@paymentHash);
            case Event.PaymentFailed variant_value:
                return 4
                    + FfiConverterTypePaymentHash.INSTANCE.AllocationSize(variant_value.@paymentHash);
            case Event.PaymentReceived variant_value:
                return 4
                    + FfiConverterTypePaymentHash.INSTANCE.AllocationSize(variant_value.@paymentHash)
                    + FfiConverterULong.INSTANCE.AllocationSize(variant_value.@amountMsat);
            case Event.ChannelPending variant_value:
                return 4
                    + FfiConverterTypeChannelId.INSTANCE.AllocationSize(variant_value.@channelId)
                    + FfiConverterTypeUserChannelId.INSTANCE.AllocationSize(variant_value.@userChannelId)
                    + FfiConverterTypeChannelId.INSTANCE.AllocationSize(variant_value.@formerTemporaryChannelId)
                    + FfiConverterTypePublicKey.INSTANCE.AllocationSize(variant_value.@counterpartyNodeId)
                    + FfiConverterTypeOutPoint.INSTANCE.AllocationSize(variant_value.@fundingTxo);
            case Event.ChannelReady variant_value:
                return 4
                    + FfiConverterTypeChannelId.INSTANCE.AllocationSize(variant_value.@channelId)
                    + FfiConverterTypeUserChannelId.INSTANCE.AllocationSize(variant_value.@userChannelId);
            case Event.ChannelClosed variant_value:
                return 4
                    + FfiConverterTypeChannelId.INSTANCE.AllocationSize(variant_value.@channelId)
                    + FfiConverterTypeUserChannelId.INSTANCE.AllocationSize(variant_value.@userChannelId);
            default:
                throw new InternalException(String.Format("invalid enum value '{}' in FfiConverterTypeEvent.AllocationSize()", value));
        }
    }

    public override void Write(Event value, BigEndianStream stream) {
        switch (value) {
            case Event.PaymentSuccessful variant_value:
                stream.WriteInt(1);
                FfiConverterTypePaymentHash.INSTANCE.Write(variant_value.@paymentHash, stream);
                break;
            case Event.PaymentFailed variant_value:
                stream.WriteInt(2);
                FfiConverterTypePaymentHash.INSTANCE.Write(variant_value.@paymentHash, stream);
                break;
            case Event.PaymentReceived variant_value:
                stream.WriteInt(3);
                FfiConverterTypePaymentHash.INSTANCE.Write(variant_value.@paymentHash, stream);
                FfiConverterULong.INSTANCE.Write(variant_value.@amountMsat, stream);
                break;
            case Event.ChannelPending variant_value:
                stream.WriteInt(4);
                FfiConverterTypeChannelId.INSTANCE.Write(variant_value.@channelId, stream);
                FfiConverterTypeUserChannelId.INSTANCE.Write(variant_value.@userChannelId, stream);
                FfiConverterTypeChannelId.INSTANCE.Write(variant_value.@formerTemporaryChannelId, stream);
                FfiConverterTypePublicKey.INSTANCE.Write(variant_value.@counterpartyNodeId, stream);
                FfiConverterTypeOutPoint.INSTANCE.Write(variant_value.@fundingTxo, stream);
                break;
            case Event.ChannelReady variant_value:
                stream.WriteInt(5);
                FfiConverterTypeChannelId.INSTANCE.Write(variant_value.@channelId, stream);
                FfiConverterTypeUserChannelId.INSTANCE.Write(variant_value.@userChannelId, stream);
                break;
            case Event.ChannelClosed variant_value:
                stream.WriteInt(6);
                FfiConverterTypeChannelId.INSTANCE.Write(variant_value.@channelId, stream);
                FfiConverterTypeUserChannelId.INSTANCE.Write(variant_value.@userChannelId, stream);
                break;
            default:
                throw new InternalException(String.Format("invalid enum value '{}' in FfiConverterTypeEvent.Write()", value));
        }
    }
}







public enum LogLevel: int {
    
    GOSSIP,
    TRACE,
    DEBUG,
    INFO,
    WARN,
    ERROR
}

class FfiConverterTypeLogLevel: FfiConverterRustBuffer<LogLevel> {
    public static FfiConverterTypeLogLevel INSTANCE = new FfiConverterTypeLogLevel();

    public override LogLevel Read(BigEndianStream stream) {
        var value = stream.ReadInt() - 1;
        if (Enum.IsDefined(typeof(LogLevel), value)) {
            return (LogLevel)value;
        } else {
            throw new InternalException(String.Format("invalid enum value '{}' in FfiConverterTypeLogLevel.Read()", value));
        }
    }

    public override int AllocationSize(LogLevel value) {
        return 4;
    }

    public override void Write(LogLevel value, BigEndianStream stream) {
        stream.WriteInt((int)value + 1);
    }
}







public enum Network: int {
    
    BITCOIN,
    TESTNET,
    SIGNET,
    REGTEST
}

class FfiConverterTypeNetwork: FfiConverterRustBuffer<Network> {
    public static FfiConverterTypeNetwork INSTANCE = new FfiConverterTypeNetwork();

    public override Network Read(BigEndianStream stream) {
        var value = stream.ReadInt() - 1;
        if (Enum.IsDefined(typeof(Network), value)) {
            return (Network)value;
        } else {
            throw new InternalException(String.Format("invalid enum value '{}' in FfiConverterTypeNetwork.Read()", value));
        }
    }

    public override int AllocationSize(Network value) {
        return 4;
    }

    public override void Write(Network value, BigEndianStream stream) {
        stream.WriteInt((int)value + 1);
    }
}







public enum PaymentDirection: int {
    
    INBOUND,
    OUTBOUND
}

class FfiConverterTypePaymentDirection: FfiConverterRustBuffer<PaymentDirection> {
    public static FfiConverterTypePaymentDirection INSTANCE = new FfiConverterTypePaymentDirection();

    public override PaymentDirection Read(BigEndianStream stream) {
        var value = stream.ReadInt() - 1;
        if (Enum.IsDefined(typeof(PaymentDirection), value)) {
            return (PaymentDirection)value;
        } else {
            throw new InternalException(String.Format("invalid enum value '{}' in FfiConverterTypePaymentDirection.Read()", value));
        }
    }

    public override int AllocationSize(PaymentDirection value) {
        return 4;
    }

    public override void Write(PaymentDirection value, BigEndianStream stream) {
        stream.WriteInt((int)value + 1);
    }
}







public enum PaymentStatus: int {
    
    PENDING,
    SUCCEEDED,
    FAILED
}

class FfiConverterTypePaymentStatus: FfiConverterRustBuffer<PaymentStatus> {
    public static FfiConverterTypePaymentStatus INSTANCE = new FfiConverterTypePaymentStatus();

    public override PaymentStatus Read(BigEndianStream stream) {
        var value = stream.ReadInt() - 1;
        if (Enum.IsDefined(typeof(PaymentStatus), value)) {
            return (PaymentStatus)value;
        } else {
            throw new InternalException(String.Format("invalid enum value '{}' in FfiConverterTypePaymentStatus.Read()", value));
        }
    }

    public override int AllocationSize(PaymentStatus value) {
        return 4;
    }

    public override void Write(PaymentStatus value, BigEndianStream stream) {
        stream.WriteInt((int)value + 1);
    }
}







public class BuildException: UniffiException {
    BuildException(string message): base(message) {}

    // Each variant is a nested class
    // Flat enums carries a string error message, so no special implementation is necessary.
    
    public class InvalidSeedBytes: BuildException {
        public InvalidSeedBytes(string message): base(message) {}
    }
    
    public class InvalidSeedFile: BuildException {
        public InvalidSeedFile(string message): base(message) {}
    }
    
    public class InvalidSystemTime: BuildException {
        public InvalidSystemTime(string message): base(message) {}
    }
    
    public class ReadFailed: BuildException {
        public ReadFailed(string message): base(message) {}
    }
    
    public class WriteFailed: BuildException {
        public WriteFailed(string message): base(message) {}
    }
    
    public class StoragePathAccessFailed: BuildException {
        public StoragePathAccessFailed(string message): base(message) {}
    }
    
    public class WalletSetupFailed: BuildException {
        public WalletSetupFailed(string message): base(message) {}
    }
    
    public class LoggerSetupFailed: BuildException {
        public LoggerSetupFailed(string message): base(message) {}
    }
    
}

class FfiConverterTypeBuildError : FfiConverterRustBuffer<BuildException>, CallStatusErrorHandler<BuildException> {
    public static FfiConverterTypeBuildError INSTANCE = new FfiConverterTypeBuildError();

    public override BuildException Read(BigEndianStream stream) {
        var value = stream.ReadInt();
        switch (value) {
            case 1: return new BuildException.InvalidSeedBytes(FfiConverterString.INSTANCE.Read(stream));
            case 2: return new BuildException.InvalidSeedFile(FfiConverterString.INSTANCE.Read(stream));
            case 3: return new BuildException.InvalidSystemTime(FfiConverterString.INSTANCE.Read(stream));
            case 4: return new BuildException.ReadFailed(FfiConverterString.INSTANCE.Read(stream));
            case 5: return new BuildException.WriteFailed(FfiConverterString.INSTANCE.Read(stream));
            case 6: return new BuildException.StoragePathAccessFailed(FfiConverterString.INSTANCE.Read(stream));
            case 7: return new BuildException.WalletSetupFailed(FfiConverterString.INSTANCE.Read(stream));
            case 8: return new BuildException.LoggerSetupFailed(FfiConverterString.INSTANCE.Read(stream));
            default:
                throw new InternalException(String.Format("invalid enum value '{}' in FfiConverterTypeBuildError.Read()", value));
        }
    }

    public override int AllocationSize(BuildException value) {
        return 4 + FfiConverterString.INSTANCE.AllocationSize(value.Message);
    }

    public override void Write(BuildException value, BigEndianStream stream) {
        switch (value) {
            case BuildException.InvalidSeedBytes:
                stream.WriteInt(1);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case BuildException.InvalidSeedFile:
                stream.WriteInt(2);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case BuildException.InvalidSystemTime:
                stream.WriteInt(3);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case BuildException.ReadFailed:
                stream.WriteInt(4);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case BuildException.WriteFailed:
                stream.WriteInt(5);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case BuildException.StoragePathAccessFailed:
                stream.WriteInt(6);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case BuildException.WalletSetupFailed:
                stream.WriteInt(7);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case BuildException.LoggerSetupFailed:
                stream.WriteInt(8);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            default:
                throw new InternalException(String.Format("invalid enum value '{}' in FfiConverterTypeBuildError.Write()", value));
        }
    }
}





public class NodeException: UniffiException {
    NodeException(string message): base(message) {}

    // Each variant is a nested class
    // Flat enums carries a string error message, so no special implementation is necessary.
    
    public class AlreadyRunning: NodeException {
        public AlreadyRunning(string message): base(message) {}
    }
    
    public class NotRunning: NodeException {
        public NotRunning(string message): base(message) {}
    }
    
    public class OnchainTxCreationFailed: NodeException {
        public OnchainTxCreationFailed(string message): base(message) {}
    }
    
    public class ConnectionFailed: NodeException {
        public ConnectionFailed(string message): base(message) {}
    }
    
    public class InvoiceCreationFailed: NodeException {
        public InvoiceCreationFailed(string message): base(message) {}
    }
    
    public class PaymentSendingFailed: NodeException {
        public PaymentSendingFailed(string message): base(message) {}
    }
    
    public class ProbeSendingFailed: NodeException {
        public ProbeSendingFailed(string message): base(message) {}
    }
    
    public class ChannelCreationFailed: NodeException {
        public ChannelCreationFailed(string message): base(message) {}
    }
    
    public class ChannelClosingFailed: NodeException {
        public ChannelClosingFailed(string message): base(message) {}
    }
    
    public class ChannelConfigUpdateFailed: NodeException {
        public ChannelConfigUpdateFailed(string message): base(message) {}
    }
    
    public class PersistenceFailed: NodeException {
        public PersistenceFailed(string message): base(message) {}
    }
    
    public class WalletOperationFailed: NodeException {
        public WalletOperationFailed(string message): base(message) {}
    }
    
    public class OnchainTxSigningFailed: NodeException {
        public OnchainTxSigningFailed(string message): base(message) {}
    }
    
    public class MessageSigningFailed: NodeException {
        public MessageSigningFailed(string message): base(message) {}
    }
    
    public class TxSyncFailed: NodeException {
        public TxSyncFailed(string message): base(message) {}
    }
    
    public class GossipUpdateFailed: NodeException {
        public GossipUpdateFailed(string message): base(message) {}
    }
    
    public class InvalidAddress: NodeException {
        public InvalidAddress(string message): base(message) {}
    }
    
    public class InvalidNetAddress: NodeException {
        public InvalidNetAddress(string message): base(message) {}
    }
    
    public class InvalidPublicKey: NodeException {
        public InvalidPublicKey(string message): base(message) {}
    }
    
    public class InvalidSecretKey: NodeException {
        public InvalidSecretKey(string message): base(message) {}
    }
    
    public class InvalidPaymentHash: NodeException {
        public InvalidPaymentHash(string message): base(message) {}
    }
    
    public class InvalidPaymentPreimage: NodeException {
        public InvalidPaymentPreimage(string message): base(message) {}
    }
    
    public class InvalidPaymentSecret: NodeException {
        public InvalidPaymentSecret(string message): base(message) {}
    }
    
    public class InvalidAmount: NodeException {
        public InvalidAmount(string message): base(message) {}
    }
    
    public class InvalidInvoice: NodeException {
        public InvalidInvoice(string message): base(message) {}
    }
    
    public class InvalidChannelId: NodeException {
        public InvalidChannelId(string message): base(message) {}
    }
    
    public class InvalidNetwork: NodeException {
        public InvalidNetwork(string message): base(message) {}
    }
    
    public class DuplicatePayment: NodeException {
        public DuplicatePayment(string message): base(message) {}
    }
    
    public class InsufficientFunds: NodeException {
        public InsufficientFunds(string message): base(message) {}
    }
    
}

class FfiConverterTypeNodeError : FfiConverterRustBuffer<NodeException>, CallStatusErrorHandler<NodeException> {
    public static FfiConverterTypeNodeError INSTANCE = new FfiConverterTypeNodeError();

    public override NodeException Read(BigEndianStream stream) {
        var value = stream.ReadInt();
        switch (value) {
            case 1: return new NodeException.AlreadyRunning(FfiConverterString.INSTANCE.Read(stream));
            case 2: return new NodeException.NotRunning(FfiConverterString.INSTANCE.Read(stream));
            case 3: return new NodeException.OnchainTxCreationFailed(FfiConverterString.INSTANCE.Read(stream));
            case 4: return new NodeException.ConnectionFailed(FfiConverterString.INSTANCE.Read(stream));
            case 5: return new NodeException.InvoiceCreationFailed(FfiConverterString.INSTANCE.Read(stream));
            case 6: return new NodeException.PaymentSendingFailed(FfiConverterString.INSTANCE.Read(stream));
            case 7: return new NodeException.ProbeSendingFailed(FfiConverterString.INSTANCE.Read(stream));
            case 8: return new NodeException.ChannelCreationFailed(FfiConverterString.INSTANCE.Read(stream));
            case 9: return new NodeException.ChannelClosingFailed(FfiConverterString.INSTANCE.Read(stream));
            case 10: return new NodeException.ChannelConfigUpdateFailed(FfiConverterString.INSTANCE.Read(stream));
            case 11: return new NodeException.PersistenceFailed(FfiConverterString.INSTANCE.Read(stream));
            case 12: return new NodeException.WalletOperationFailed(FfiConverterString.INSTANCE.Read(stream));
            case 13: return new NodeException.OnchainTxSigningFailed(FfiConverterString.INSTANCE.Read(stream));
            case 14: return new NodeException.MessageSigningFailed(FfiConverterString.INSTANCE.Read(stream));
            case 15: return new NodeException.TxSyncFailed(FfiConverterString.INSTANCE.Read(stream));
            case 16: return new NodeException.GossipUpdateFailed(FfiConverterString.INSTANCE.Read(stream));
            case 17: return new NodeException.InvalidAddress(FfiConverterString.INSTANCE.Read(stream));
            case 18: return new NodeException.InvalidNetAddress(FfiConverterString.INSTANCE.Read(stream));
            case 19: return new NodeException.InvalidPublicKey(FfiConverterString.INSTANCE.Read(stream));
            case 20: return new NodeException.InvalidSecretKey(FfiConverterString.INSTANCE.Read(stream));
            case 21: return new NodeException.InvalidPaymentHash(FfiConverterString.INSTANCE.Read(stream));
            case 22: return new NodeException.InvalidPaymentPreimage(FfiConverterString.INSTANCE.Read(stream));
            case 23: return new NodeException.InvalidPaymentSecret(FfiConverterString.INSTANCE.Read(stream));
            case 24: return new NodeException.InvalidAmount(FfiConverterString.INSTANCE.Read(stream));
            case 25: return new NodeException.InvalidInvoice(FfiConverterString.INSTANCE.Read(stream));
            case 26: return new NodeException.InvalidChannelId(FfiConverterString.INSTANCE.Read(stream));
            case 27: return new NodeException.InvalidNetwork(FfiConverterString.INSTANCE.Read(stream));
            case 28: return new NodeException.DuplicatePayment(FfiConverterString.INSTANCE.Read(stream));
            case 29: return new NodeException.InsufficientFunds(FfiConverterString.INSTANCE.Read(stream));
            default:
                throw new InternalException(String.Format("invalid enum value '{}' in FfiConverterTypeNodeError.Read()", value));
        }
    }

    public override int AllocationSize(NodeException value) {
        return 4 + FfiConverterString.INSTANCE.AllocationSize(value.Message);
    }

    public override void Write(NodeException value, BigEndianStream stream) {
        switch (value) {
            case NodeException.AlreadyRunning:
                stream.WriteInt(1);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case NodeException.NotRunning:
                stream.WriteInt(2);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case NodeException.OnchainTxCreationFailed:
                stream.WriteInt(3);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case NodeException.ConnectionFailed:
                stream.WriteInt(4);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case NodeException.InvoiceCreationFailed:
                stream.WriteInt(5);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case NodeException.PaymentSendingFailed:
                stream.WriteInt(6);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case NodeException.ProbeSendingFailed:
                stream.WriteInt(7);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case NodeException.ChannelCreationFailed:
                stream.WriteInt(8);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case NodeException.ChannelClosingFailed:
                stream.WriteInt(9);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case NodeException.ChannelConfigUpdateFailed:
                stream.WriteInt(10);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case NodeException.PersistenceFailed:
                stream.WriteInt(11);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case NodeException.WalletOperationFailed:
                stream.WriteInt(12);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case NodeException.OnchainTxSigningFailed:
                stream.WriteInt(13);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case NodeException.MessageSigningFailed:
                stream.WriteInt(14);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case NodeException.TxSyncFailed:
                stream.WriteInt(15);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case NodeException.GossipUpdateFailed:
                stream.WriteInt(16);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case NodeException.InvalidAddress:
                stream.WriteInt(17);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case NodeException.InvalidNetAddress:
                stream.WriteInt(18);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case NodeException.InvalidPublicKey:
                stream.WriteInt(19);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case NodeException.InvalidSecretKey:
                stream.WriteInt(20);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case NodeException.InvalidPaymentHash:
                stream.WriteInt(21);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case NodeException.InvalidPaymentPreimage:
                stream.WriteInt(22);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case NodeException.InvalidPaymentSecret:
                stream.WriteInt(23);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case NodeException.InvalidAmount:
                stream.WriteInt(24);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case NodeException.InvalidInvoice:
                stream.WriteInt(25);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case NodeException.InvalidChannelId:
                stream.WriteInt(26);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case NodeException.InvalidNetwork:
                stream.WriteInt(27);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case NodeException.DuplicatePayment:
                stream.WriteInt(28);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            case NodeException.InsufficientFunds:
                stream.WriteInt(29);
                FfiConverterString.INSTANCE.Write(value.Message, stream);
                break;
            default:
                throw new InternalException(String.Format("invalid enum value '{}' in FfiConverterTypeNodeError.Write()", value));
        }
    }
}




class FfiConverterOptionalUShort: FfiConverterRustBuffer<UInt16?> {
    public static FfiConverterOptionalUShort INSTANCE = new FfiConverterOptionalUShort();

    public override UInt16? Read(BigEndianStream stream) {
        if (stream.ReadByte() == 0) {
            return null;
        }
        return FfiConverterUShort.INSTANCE.Read(stream);
    }

    public override int AllocationSize(UInt16? value) {
        if (value == null) {
            return 1;
        } else {
            return 1 + FfiConverterUShort.INSTANCE.AllocationSize((UInt16)value);
        }
    }

    public override void Write(UInt16? value, BigEndianStream stream) {
        if (value == null) {
            stream.WriteByte(0);
        } else {
            stream.WriteByte(1);
            FfiConverterUShort.INSTANCE.Write((UInt16)value, stream);
        }
    }
}




class FfiConverterOptionalUInt: FfiConverterRustBuffer<UInt32?> {
    public static FfiConverterOptionalUInt INSTANCE = new FfiConverterOptionalUInt();

    public override UInt32? Read(BigEndianStream stream) {
        if (stream.ReadByte() == 0) {
            return null;
        }
        return FfiConverterUInt.INSTANCE.Read(stream);
    }

    public override int AllocationSize(UInt32? value) {
        if (value == null) {
            return 1;
        } else {
            return 1 + FfiConverterUInt.INSTANCE.AllocationSize((UInt32)value);
        }
    }

    public override void Write(UInt32? value, BigEndianStream stream) {
        if (value == null) {
            stream.WriteByte(0);
        } else {
            stream.WriteByte(1);
            FfiConverterUInt.INSTANCE.Write((UInt32)value, stream);
        }
    }
}




class FfiConverterOptionalULong: FfiConverterRustBuffer<UInt64?> {
    public static FfiConverterOptionalULong INSTANCE = new FfiConverterOptionalULong();

    public override UInt64? Read(BigEndianStream stream) {
        if (stream.ReadByte() == 0) {
            return null;
        }
        return FfiConverterULong.INSTANCE.Read(stream);
    }

    public override int AllocationSize(UInt64? value) {
        if (value == null) {
            return 1;
        } else {
            return 1 + FfiConverterULong.INSTANCE.AllocationSize((UInt64)value);
        }
    }

    public override void Write(UInt64? value, BigEndianStream stream) {
        if (value == null) {
            stream.WriteByte(0);
        } else {
            stream.WriteByte(1);
            FfiConverterULong.INSTANCE.Write((UInt64)value, stream);
        }
    }
}




class FfiConverterOptionalString: FfiConverterRustBuffer<String?> {
    public static FfiConverterOptionalString INSTANCE = new FfiConverterOptionalString();

    public override String? Read(BigEndianStream stream) {
        if (stream.ReadByte() == 0) {
            return null;
        }
        return FfiConverterString.INSTANCE.Read(stream);
    }

    public override int AllocationSize(String? value) {
        if (value == null) {
            return 1;
        } else {
            return 1 + FfiConverterString.INSTANCE.AllocationSize((String)value);
        }
    }

    public override void Write(String? value, BigEndianStream stream) {
        if (value == null) {
            stream.WriteByte(0);
        } else {
            stream.WriteByte(1);
            FfiConverterString.INSTANCE.Write((String)value, stream);
        }
    }
}




class FfiConverterOptionalTypeChannelConfig: FfiConverterRustBuffer<ChannelConfig?> {
    public static FfiConverterOptionalTypeChannelConfig INSTANCE = new FfiConverterOptionalTypeChannelConfig();

    public override ChannelConfig? Read(BigEndianStream stream) {
        if (stream.ReadByte() == 0) {
            return null;
        }
        return FfiConverterTypeChannelConfig.INSTANCE.Read(stream);
    }

    public override int AllocationSize(ChannelConfig? value) {
        if (value == null) {
            return 1;
        } else {
            return 1 + FfiConverterTypeChannelConfig.INSTANCE.AllocationSize((ChannelConfig)value);
        }
    }

    public override void Write(ChannelConfig? value, BigEndianStream stream) {
        if (value == null) {
            stream.WriteByte(0);
        } else {
            stream.WriteByte(1);
            FfiConverterTypeChannelConfig.INSTANCE.Write((ChannelConfig)value, stream);
        }
    }
}




class FfiConverterOptionalTypeOutPoint: FfiConverterRustBuffer<OutPoint?> {
    public static FfiConverterOptionalTypeOutPoint INSTANCE = new FfiConverterOptionalTypeOutPoint();

    public override OutPoint? Read(BigEndianStream stream) {
        if (stream.ReadByte() == 0) {
            return null;
        }
        return FfiConverterTypeOutPoint.INSTANCE.Read(stream);
    }

    public override int AllocationSize(OutPoint? value) {
        if (value == null) {
            return 1;
        } else {
            return 1 + FfiConverterTypeOutPoint.INSTANCE.AllocationSize((OutPoint)value);
        }
    }

    public override void Write(OutPoint? value, BigEndianStream stream) {
        if (value == null) {
            stream.WriteByte(0);
        } else {
            stream.WriteByte(1);
            FfiConverterTypeOutPoint.INSTANCE.Write((OutPoint)value, stream);
        }
    }
}




class FfiConverterOptionalTypePaymentDetails: FfiConverterRustBuffer<PaymentDetails?> {
    public static FfiConverterOptionalTypePaymentDetails INSTANCE = new FfiConverterOptionalTypePaymentDetails();

    public override PaymentDetails? Read(BigEndianStream stream) {
        if (stream.ReadByte() == 0) {
            return null;
        }
        return FfiConverterTypePaymentDetails.INSTANCE.Read(stream);
    }

    public override int AllocationSize(PaymentDetails? value) {
        if (value == null) {
            return 1;
        } else {
            return 1 + FfiConverterTypePaymentDetails.INSTANCE.AllocationSize((PaymentDetails)value);
        }
    }

    public override void Write(PaymentDetails? value, BigEndianStream stream) {
        if (value == null) {
            stream.WriteByte(0);
        } else {
            stream.WriteByte(1);
            FfiConverterTypePaymentDetails.INSTANCE.Write((PaymentDetails)value, stream);
        }
    }
}




class FfiConverterOptionalTypeEvent: FfiConverterRustBuffer<Event?> {
    public static FfiConverterOptionalTypeEvent INSTANCE = new FfiConverterOptionalTypeEvent();

    public override Event? Read(BigEndianStream stream) {
        if (stream.ReadByte() == 0) {
            return null;
        }
        return FfiConverterTypeEvent.INSTANCE.Read(stream);
    }

    public override int AllocationSize(Event? value) {
        if (value == null) {
            return 1;
        } else {
            return 1 + FfiConverterTypeEvent.INSTANCE.AllocationSize((Event)value);
        }
    }

    public override void Write(Event? value, BigEndianStream stream) {
        if (value == null) {
            stream.WriteByte(0);
        } else {
            stream.WriteByte(1);
            FfiConverterTypeEvent.INSTANCE.Write((Event)value, stream);
        }
    }
}




class FfiConverterOptionalTypeNetAddress: FfiConverterRustBuffer<NetAddress?> {
    public static FfiConverterOptionalTypeNetAddress INSTANCE = new FfiConverterOptionalTypeNetAddress();

    public override NetAddress? Read(BigEndianStream stream) {
        if (stream.ReadByte() == 0) {
            return null;
        }
        return FfiConverterTypeNetAddress.INSTANCE.Read(stream);
    }

    public override int AllocationSize(NetAddress? value) {
        if (value == null) {
            return 1;
        } else {
            return 1 + FfiConverterTypeNetAddress.INSTANCE.AllocationSize((NetAddress)value);
        }
    }

    public override void Write(NetAddress? value, BigEndianStream stream) {
        if (value == null) {
            stream.WriteByte(0);
        } else {
            stream.WriteByte(1);
            FfiConverterTypeNetAddress.INSTANCE.Write((NetAddress)value, stream);
        }
    }
}




class FfiConverterOptionalTypePaymentPreimage: FfiConverterRustBuffer<PaymentPreimage?> {
    public static FfiConverterOptionalTypePaymentPreimage INSTANCE = new FfiConverterOptionalTypePaymentPreimage();

    public override PaymentPreimage? Read(BigEndianStream stream) {
        if (stream.ReadByte() == 0) {
            return null;
        }
        return FfiConverterTypePaymentPreimage.INSTANCE.Read(stream);
    }

    public override int AllocationSize(PaymentPreimage? value) {
        if (value == null) {
            return 1;
        } else {
            return 1 + FfiConverterTypePaymentPreimage.INSTANCE.AllocationSize((PaymentPreimage)value);
        }
    }

    public override void Write(PaymentPreimage? value, BigEndianStream stream) {
        if (value == null) {
            stream.WriteByte(0);
        } else {
            stream.WriteByte(1);
            FfiConverterTypePaymentPreimage.INSTANCE.Write((PaymentPreimage)value, stream);
        }
    }
}




class FfiConverterOptionalTypePaymentSecret: FfiConverterRustBuffer<PaymentSecret?> {
    public static FfiConverterOptionalTypePaymentSecret INSTANCE = new FfiConverterOptionalTypePaymentSecret();

    public override PaymentSecret? Read(BigEndianStream stream) {
        if (stream.ReadByte() == 0) {
            return null;
        }
        return FfiConverterTypePaymentSecret.INSTANCE.Read(stream);
    }

    public override int AllocationSize(PaymentSecret? value) {
        if (value == null) {
            return 1;
        } else {
            return 1 + FfiConverterTypePaymentSecret.INSTANCE.AllocationSize((PaymentSecret)value);
        }
    }

    public override void Write(PaymentSecret? value, BigEndianStream stream) {
        if (value == null) {
            stream.WriteByte(0);
        } else {
            stream.WriteByte(1);
            FfiConverterTypePaymentSecret.INSTANCE.Write((PaymentSecret)value, stream);
        }
    }
}




class FfiConverterSequenceByte: FfiConverterRustBuffer<List<Byte>> {
    public static FfiConverterSequenceByte INSTANCE = new FfiConverterSequenceByte();

    public override List<Byte> Read(BigEndianStream stream) {
        var length = stream.ReadInt();
        var result = new List<Byte>(length);
        for (int i = 0; i < length; i++) {
            result.Add(FfiConverterByte.INSTANCE.Read(stream));
        }
        return result;
    }

    public override int AllocationSize(List<Byte> value) {
        var sizeForLength = 4;

        // details/1-empty-list-as-default-method-parameter.md
        if (value == null) {
            return sizeForLength;
        }

        var sizeForItems = value.Select(item => FfiConverterByte.INSTANCE.AllocationSize(item)).Sum();
        return sizeForLength + sizeForItems;
    }

    public override void Write(List<Byte> value, BigEndianStream stream) {
        // details/1-empty-list-as-default-method-parameter.md
        if (value == null) {
            stream.WriteInt(0);
            return;
        }

        stream.WriteInt(value.Count);
        value.ForEach(item => FfiConverterByte.INSTANCE.Write(item, stream));
    }
}




class FfiConverterSequenceTypeChannelDetails: FfiConverterRustBuffer<List<ChannelDetails>> {
    public static FfiConverterSequenceTypeChannelDetails INSTANCE = new FfiConverterSequenceTypeChannelDetails();

    public override List<ChannelDetails> Read(BigEndianStream stream) {
        var length = stream.ReadInt();
        var result = new List<ChannelDetails>(length);
        for (int i = 0; i < length; i++) {
            result.Add(FfiConverterTypeChannelDetails.INSTANCE.Read(stream));
        }
        return result;
    }

    public override int AllocationSize(List<ChannelDetails> value) {
        var sizeForLength = 4;

        // details/1-empty-list-as-default-method-parameter.md
        if (value == null) {
            return sizeForLength;
        }

        var sizeForItems = value.Select(item => FfiConverterTypeChannelDetails.INSTANCE.AllocationSize(item)).Sum();
        return sizeForLength + sizeForItems;
    }

    public override void Write(List<ChannelDetails> value, BigEndianStream stream) {
        // details/1-empty-list-as-default-method-parameter.md
        if (value == null) {
            stream.WriteInt(0);
            return;
        }

        stream.WriteInt(value.Count);
        value.ForEach(item => FfiConverterTypeChannelDetails.INSTANCE.Write(item, stream));
    }
}




class FfiConverterSequenceTypePaymentDetails: FfiConverterRustBuffer<List<PaymentDetails>> {
    public static FfiConverterSequenceTypePaymentDetails INSTANCE = new FfiConverterSequenceTypePaymentDetails();

    public override List<PaymentDetails> Read(BigEndianStream stream) {
        var length = stream.ReadInt();
        var result = new List<PaymentDetails>(length);
        for (int i = 0; i < length; i++) {
            result.Add(FfiConverterTypePaymentDetails.INSTANCE.Read(stream));
        }
        return result;
    }

    public override int AllocationSize(List<PaymentDetails> value) {
        var sizeForLength = 4;

        // details/1-empty-list-as-default-method-parameter.md
        if (value == null) {
            return sizeForLength;
        }

        var sizeForItems = value.Select(item => FfiConverterTypePaymentDetails.INSTANCE.AllocationSize(item)).Sum();
        return sizeForLength + sizeForItems;
    }

    public override void Write(List<PaymentDetails> value, BigEndianStream stream) {
        // details/1-empty-list-as-default-method-parameter.md
        if (value == null) {
            stream.WriteInt(0);
            return;
        }

        stream.WriteInt(value.Count);
        value.ForEach(item => FfiConverterTypePaymentDetails.INSTANCE.Write(item, stream));
    }
}




class FfiConverterSequenceTypePeerDetails: FfiConverterRustBuffer<List<PeerDetails>> {
    public static FfiConverterSequenceTypePeerDetails INSTANCE = new FfiConverterSequenceTypePeerDetails();

    public override List<PeerDetails> Read(BigEndianStream stream) {
        var length = stream.ReadInt();
        var result = new List<PeerDetails>(length);
        for (int i = 0; i < length; i++) {
            result.Add(FfiConverterTypePeerDetails.INSTANCE.Read(stream));
        }
        return result;
    }

    public override int AllocationSize(List<PeerDetails> value) {
        var sizeForLength = 4;

        // details/1-empty-list-as-default-method-parameter.md
        if (value == null) {
            return sizeForLength;
        }

        var sizeForItems = value.Select(item => FfiConverterTypePeerDetails.INSTANCE.AllocationSize(item)).Sum();
        return sizeForLength + sizeForItems;
    }

    public override void Write(List<PeerDetails> value, BigEndianStream stream) {
        // details/1-empty-list-as-default-method-parameter.md
        if (value == null) {
            stream.WriteInt(0);
            return;
        }

        stream.WriteInt(value.Count);
        value.ForEach(item => FfiConverterTypePeerDetails.INSTANCE.Write(item, stream));
    }
}




class FfiConverterSequenceTypePublicKey: FfiConverterRustBuffer<List<PublicKey>> {
    public static FfiConverterSequenceTypePublicKey INSTANCE = new FfiConverterSequenceTypePublicKey();

    public override List<PublicKey> Read(BigEndianStream stream) {
        var length = stream.ReadInt();
        var result = new List<PublicKey>(length);
        for (int i = 0; i < length; i++) {
            result.Add(FfiConverterTypePublicKey.INSTANCE.Read(stream));
        }
        return result;
    }

    public override int AllocationSize(List<PublicKey> value) {
        var sizeForLength = 4;

        // details/1-empty-list-as-default-method-parameter.md
        if (value == null) {
            return sizeForLength;
        }

        var sizeForItems = value.Select(item => FfiConverterTypePublicKey.INSTANCE.AllocationSize(item)).Sum();
        return sizeForLength + sizeForItems;
    }

    public override void Write(List<PublicKey> value, BigEndianStream stream) {
        // details/1-empty-list-as-default-method-parameter.md
        if (value == null) {
            stream.WriteInt(0);
            return;
        }

        stream.WriteInt(value.Count);
        value.ForEach(item => FfiConverterTypePublicKey.INSTANCE.Write(item, stream));
    }
}



/**
 * Typealias from the type name used in the UDL file to the builtin type.  This
 * is needed because the UDL type name is used in function/method signatures.
 * It's also what we have an external type that references a custom type.
 */



/**
 * Typealias from the type name used in the UDL file to the builtin type.  This
 * is needed because the UDL type name is used in function/method signatures.
 * It's also what we have an external type that references a custom type.
 */



/**
 * Typealias from the type name used in the UDL file to the builtin type.  This
 * is needed because the UDL type name is used in function/method signatures.
 * It's also what we have an external type that references a custom type.
 */



/**
 * Typealias from the type name used in the UDL file to the builtin type.  This
 * is needed because the UDL type name is used in function/method signatures.
 * It's also what we have an external type that references a custom type.
 */



/**
 * Typealias from the type name used in the UDL file to the builtin type.  This
 * is needed because the UDL type name is used in function/method signatures.
 * It's also what we have an external type that references a custom type.
 */



/**
 * Typealias from the type name used in the UDL file to the builtin type.  This
 * is needed because the UDL type name is used in function/method signatures.
 * It's also what we have an external type that references a custom type.
 */



/**
 * Typealias from the type name used in the UDL file to the builtin type.  This
 * is needed because the UDL type name is used in function/method signatures.
 * It's also what we have an external type that references a custom type.
 */



/**
 * Typealias from the type name used in the UDL file to the builtin type.  This
 * is needed because the UDL type name is used in function/method signatures.
 * It's also what we have an external type that references a custom type.
 */



/**
 * Typealias from the type name used in the UDL file to the builtin type.  This
 * is needed because the UDL type name is used in function/method signatures.
 * It's also what we have an external type that references a custom type.
 */



/**
 * Typealias from the type name used in the UDL file to the builtin type.  This
 * is needed because the UDL type name is used in function/method signatures.
 * It's also what we have an external type that references a custom type.
 */



/**
 * Typealias from the type name used in the UDL file to the builtin type.  This
 * is needed because the UDL type name is used in function/method signatures.
 * It's also what we have an external type that references a custom type.
 */
#pragma warning restore 8625

public static class LdkNodeMethods {
    public static Mnemonic GenerateEntropyMnemonic() {
        return FfiConverterTypeMnemonic.INSTANCE.Lift(
    _UniffiHelpers.RustCall( (ref RustCallStatus _status) =>
    _UniFFILib.ldk_node_f89f_generate_entropy_mnemonic( ref _status)
));
    }

}

