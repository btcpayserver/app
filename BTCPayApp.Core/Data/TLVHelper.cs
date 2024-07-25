namespace BTCPayApp.Core.Data;

public static class TLVHelper
{
    public record TLV(byte Tag, byte[] Value);

    public static byte[] Write(List<TLV> tlvList)
    {
        List<byte> byteArray = new List<byte>();

        foreach (var tlv in tlvList)
        {
            byteArray.Add(tlv.Tag);
            byteArray.AddRange(BitConverter.GetBytes(tlv.Value.Length));
            byteArray.AddRange(tlv.Value);
        }

        return byteArray.ToArray();
    }

    public static List<TLV> Read(byte[] byteArray)
    {
        var tlvList = new List<TLV>();
        var index = 0;

        while (index < byteArray.Length)
        {
            var tag = byteArray[index];
            index += 1;

            var length = BitConverter.ToInt32(byteArray, index);
            index += 4;

            var value = new byte[length];
            Array.Copy(byteArray, index, value, 0, length);
            index += length;

            tlvList.Add(new TLV(tag, value));
        }

        return tlvList;
    }
}