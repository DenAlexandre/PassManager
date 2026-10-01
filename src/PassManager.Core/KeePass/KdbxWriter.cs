using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace PassManager.Core.KeePass;

public static class KdbxWriter
{
    private static readonly byte[] Aes256CipherUuid =
        [0x31, 0xC1, 0xF2, 0xE6, 0xBF, 0x71, 0x43, 0x50, 0xBE, 0x58, 0x05, 0x21, 0x6A, 0xFC, 0x5A, 0xFF];

    public static void Write(Stream output, KdbxDocument document, string password)
    {
        var masterSeed = RandomNumberGenerator.GetBytes(32);
        var encryptionIv = RandomNumberGenerator.GetBytes(16);
        var kdfSalt = RandomNumberGenerator.GetBytes(32);
        var innerRandomStreamKey = RandomNumberGenerator.GetBytes(64);

        var kdfParameters = new Dictionary<string, object>
        {
            ["$UUID"] = KdbxCrypto.Argon2idUuid,
            ["S"] = kdfSalt,
            ["I"] = (ulong)3,
            ["M"] = (ulong)(64 * 1024 * 1024),
            ["P"] = (uint)2,
            ["V"] = (uint)0x13
        };
        var kdfParametersBytes = VariantDictionary.Write(kdfParameters);

        using var headerStream = new MemoryStream();
        headerStream.Write(BitConverter.GetBytes(0x9AA2D903));
        headerStream.Write(BitConverter.GetBytes(0xB54BFB67));
        headerStream.Write(BitConverter.GetBytes((ushort)0));
        headerStream.Write(BitConverter.GetBytes((ushort)4));
        WriteField(headerStream, 2, Aes256CipherUuid);
        WriteField(headerStream, 3, BitConverter.GetBytes((uint)1));
        WriteField(headerStream, 4, masterSeed);
        WriteField(headerStream, 7, encryptionIv);
        WriteField(headerStream, 11, kdfParametersBytes);
        WriteField(headerStream, 0, [0x0D, 0x0A, 0x0D, 0x0A]);
        var headerBytes = headerStream.ToArray();

        var compositeKey = KdbxCrypto.DeriveCompositeKey(password);
        var transformedKey = KdbxCrypto.TransformKey(compositeKey, kdfParameters);
        var finalKey = KdbxCrypto.DeriveFinalKey(masterSeed, transformedKey);
        var hmacKeyBase = KdbxCrypto.DeriveHmacKeyBase(masterSeed, transformedKey);
        var headerHash = SHA256.HashData(headerBytes);
        var headerHmac = KdbxCrypto.ComputeHeaderHmac(headerBytes, hmacKeyBase);

        var streamKeyHash = SHA512.HashData(innerRandomStreamKey);
        var chachaKey = streamKeyHash[..32];
        var chachaNonce = streamKeyHash[32..44];

        var innerHeaderAndXml = BuildInnerPayload(document, innerRandomStreamKey, chachaKey, chachaNonce);
        var compressed = Compress(innerHeaderAndXml);

        using var aes = Aes.Create();
        aes.Key = finalKey;
        aes.IV = encryptionIv;
        aes.Mode = CipherMode.CBC;
        using var encryptor = aes.CreateEncryptor();
        var ciphertext = encryptor.TransformFinalBlock(compressed, 0, compressed.Length);

        output.Write(headerBytes, 0, headerBytes.Length);
        output.Write(headerHash, 0, 32);
        output.Write(headerHmac, 0, 32);
        KdbxCrypto.WriteHmacBlockStream(output, ciphertext, hmacKeyBase);
    }

    private static void WriteField(Stream stream, byte fieldId, byte[] data)
    {
        stream.WriteByte(fieldId);
        stream.Write(BitConverter.GetBytes(data.Length));
        stream.Write(data);
    }

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
        {
            gzip.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    private static byte[] BuildInnerPayload(KdbxDocument document, byte[] innerRandomStreamKey, byte[] chachaKey, byte[] chachaNonce)
    {
        using var inner = new MemoryStream();
        WriteField(inner, 1, BitConverter.GetBytes((uint)3));
        WriteField(inner, 2, innerRandomStreamKey);
        WriteField(inner, 0, []);

        var xml = BuildXml(document);

        // Same continuous-keystream rule as KdbxReader: encrypt every protected value's plaintext as one
        // concatenation in document order, in a single ChaCha20Cipher.Apply call, then split back out.
        var protectedElements = new List<(XElement Element, byte[] Plaintext)>();
        CollectProtectedPlaceholders(xml.Root!.Element("Root")!.Element("Group")!, protectedElements);

        var plaintexts = protectedElements.Select(p => p.Plaintext).ToList();
        var concatenatedPlaintext = plaintexts.SelectMany(p => p).ToArray();
        var concatenatedCiphertext = ChaCha20Cipher.Apply(chachaKey, chachaNonce, concatenatedPlaintext);

        var position = 0;
        foreach (var (element, plaintext) in protectedElements)
        {
            var length = plaintext.Length;
            var cipherSlice = concatenatedCiphertext[position..(position + length)];
            element.Value = Convert.ToBase64String(cipherSlice);
            position += length;
        }

        var xmlBytes = System.Text.Encoding.UTF8.GetBytes(xml.ToString());
        inner.Write(xmlBytes, 0, xmlBytes.Length);

        return inner.ToArray();
    }

    private static void CollectProtectedPlaceholders(XElement groupXml, List<(XElement, byte[])> accumulator)
    {
        foreach (var entryXml in groupXml.Elements("Entry"))
        {
            foreach (var stringXml in entryXml.Elements("String"))
            {
                var valueXml = stringXml.Element("Value")!;
                if (valueXml.Attribute("Protected")?.Value == "True")
                {
                    var plaintext = System.Text.Encoding.UTF8.GetBytes(valueXml.Value);
                    accumulator.Add((valueXml, plaintext));
                }
            }
        }

        foreach (var childGroupXml in groupXml.Elements("Group"))
        {
            CollectProtectedPlaceholders(childGroupXml, accumulator);
        }
    }

    private static XDocument BuildXml(KdbxDocument document)
    {
        var meta = new XElement("Meta",
            new XElement("Generator", "PassManager"),
            new XElement("DatabaseName", "PassManager export"),
            new XElement("RecycleBinEnabled", "False"));

        var rootGroupXml = BuildGroupXml(document.Root);

        var root = new XElement("KeePassFile", meta, new XElement("Root", rootGroupXml));
        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    private static XElement BuildGroupXml(KdbxGroup group)
    {
        var element = new XElement("Group", new XElement("Name", group.Name));

        foreach (var entry in group.Entries)
        {
            element.Add(BuildEntryXml(entry));
        }

        foreach (var child in group.Groups)
        {
            element.Add(BuildGroupXml(child));
        }

        return element;
    }

    private static XElement BuildEntryXml(KdbxEntry entry)
    {
        XElement StringField(string key, string? value, bool isProtected)
        {
            var valueElement = new XElement("Value", value ?? "");
            if (isProtected)
            {
                // Placeholder plaintext stored here temporarily; BuildInnerPayload replaces .Value with
                // the real base64 ciphertext once the continuous keystream has been computed.
                valueElement.SetAttributeValue("Protected", "True");
            }

            return new XElement("String", new XElement("Key", key), valueElement);
        }

        return new XElement("Entry",
            StringField("Title", entry.Title, isProtected: false),
            StringField("URL", entry.Url, isProtected: false),
            StringField("UserName", entry.UserName, isProtected: false),
            StringField("Password", entry.Password, isProtected: true),
            StringField("Notes", entry.Notes, isProtected: false));
    }
}
