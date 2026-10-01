using System.Text;

namespace PassManager.Core.KeePass;

/// <summary>
/// Reads/writes the binary type-tagged key-value map used for KdfParameters (KDF settings).
/// Format: UInt16 version, then repeated (byte type, Int32 keyLength, byte[] key, Int32 valueLength,
/// byte[] value) entries, terminated by a type-0x00 byte. Confirmed byte-for-byte against the real
/// DatabaseTest.kdbx fixture's KdfParameters blob (see the design spec).
/// </summary>
public static class VariantDictionary
{
    private const byte TypeUInt32 = 0x04;
    private const byte TypeUInt64 = 0x05;
    private const byte TypeBool = 0x08;
    private const byte TypeInt32 = 0x0C;
    private const byte TypeInt64 = 0x0D;
    private const byte TypeString = 0x18;
    private const byte TypeByteArray = 0x42;
    private const byte TypeEnd = 0x00;

    public static Dictionary<string, object> Read(byte[] data)
    {
        var result = new Dictionary<string, object>();
        var offset = 0;

        var version = BitConverter.ToUInt16(data, offset);
        offset += 2;
        if ((version >> 8) != 1)
        {
            throw new KdbxFormatException($"Version de VariantDictionary non supportée : 0x{version:X4}.");
        }

        while (true)
        {
            var type = data[offset];
            offset += 1;
            if (type == TypeEnd)
            {
                break;
            }

            var keyLength = BitConverter.ToInt32(data, offset);
            offset += 4;
            var key = Encoding.UTF8.GetString(data, offset, keyLength);
            offset += keyLength;

            var valueLength = BitConverter.ToInt32(data, offset);
            offset += 4;
            var valueBytes = new byte[valueLength];
            Array.Copy(data, offset, valueBytes, 0, valueLength);
            offset += valueLength;

            object value = type switch
            {
                TypeUInt32 => BitConverter.ToUInt32(valueBytes, 0),
                TypeUInt64 => BitConverter.ToUInt64(valueBytes, 0),
                TypeBool => valueBytes[0] != 0,
                TypeInt32 => BitConverter.ToInt32(valueBytes, 0),
                TypeInt64 => BitConverter.ToInt64(valueBytes, 0),
                TypeString => Encoding.UTF8.GetString(valueBytes),
                TypeByteArray => valueBytes,
                _ => throw new KdbxFormatException($"Type de VariantDictionary non supporté : 0x{type:X2}.")
            };

            result[key] = value;
        }

        return result;
    }

    public static byte[] Write(Dictionary<string, object> values)
    {
        using var stream = new MemoryStream();
        stream.Write(BitConverter.GetBytes((ushort)0x0100));

        foreach (var (key, value) in values)
        {
            byte type;
            byte[] valueBytes;
            switch (value)
            {
                case uint u32:
                    type = TypeUInt32;
                    valueBytes = BitConverter.GetBytes(u32);
                    break;
                case ulong u64:
                    type = TypeUInt64;
                    valueBytes = BitConverter.GetBytes(u64);
                    break;
                case byte[] bytes:
                    type = TypeByteArray;
                    valueBytes = bytes;
                    break;
                default:
                    throw new KdbxFormatException($"Type non supporté pour l'écriture : {value.GetType()}.");
            }

            stream.WriteByte(type);
            var keyBytes = Encoding.UTF8.GetBytes(key);
            stream.Write(BitConverter.GetBytes(keyBytes.Length));
            stream.Write(keyBytes);
            stream.Write(BitConverter.GetBytes(valueBytes.Length));
            stream.Write(valueBytes);
        }

        stream.WriteByte(TypeEnd);
        return stream.ToArray();
    }
}
