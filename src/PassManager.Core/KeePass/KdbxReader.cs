using System.IO.Compression;
using System.Xml.Linq;

namespace PassManager.Core.KeePass;

public static class KdbxReader
{
    private static readonly byte[] Aes256CipherUuid =
        [0x31, 0xC1, 0xF2, 0xE6, 0xBF, 0x71, 0x43, 0x50, 0xBE, 0x58, 0x05, 0x21, 0x6A, 0xFC, 0x5A, 0xFF];

    public static KdbxDocument Read(Stream kdbxFile, string password)
    {
        using var ms = new MemoryStream();
        kdbxFile.CopyTo(ms);
        var fileBytes = ms.ToArray();

        var sig1 = BitConverter.ToUInt32(fileBytes, 0);
        var sig2 = BitConverter.ToUInt32(fileBytes, 4);
        if (sig1 != 0x9AA2D903 || sig2 != 0xB54BFB67)
        {
            throw new KdbxFormatException("Signature KDBX invalide.");
        }

        var versionMinor = BitConverter.ToUInt16(fileBytes, 8);
        var versionMajor = BitConverter.ToUInt16(fileBytes, 10);
        if (versionMajor != 4)
        {
            throw new KdbxFormatException($"Seul KDBX4 est supporté (version trouvée : {versionMajor}.{versionMinor}).");
        }

        var offset = 12;
        byte[]? cipherId = null;
        uint compressionFlags = 0;
        byte[]? masterSeed = null;
        byte[]? encryptionIv = null;
        Dictionary<string, object>? kdfParameters = null;

        while (true)
        {
            var fieldId = fileBytes[offset];
            offset += 1;
            var fieldLength = BitConverter.ToInt32(fileBytes, offset);
            offset += 4;
            var fieldData = fileBytes[offset..(offset + fieldLength)];
            offset += fieldLength;

            if (fieldId == 0)
            {
                break;
            }

            switch (fieldId)
            {
                case 2: cipherId = fieldData; break;
                case 3: compressionFlags = BitConverter.ToUInt32(fieldData, 0); break;
                case 4: masterSeed = fieldData; break;
                case 7: encryptionIv = fieldData; break;
                case 11: kdfParameters = VariantDictionary.Read(fieldData); break;
            }
        }

        if (cipherId is null || masterSeed is null || encryptionIv is null || kdfParameters is null)
        {
            throw new KdbxFormatException("En-tête KDBX incomplet.");
        }

        if (!cipherId.AsSpan().SequenceEqual(Aes256CipherUuid))
        {
            throw new KdbxFormatException("Seul le chiffrement AES256 est supporté.");
        }

        var headerBytes = fileBytes[..offset];
        var headerHash = fileBytes[offset..(offset + 32)];
        offset += 32;
        var headerHmac = fileBytes[offset..(offset + 32)];
        offset += 32;

        if (!System.Security.Cryptography.SHA256.HashData(headerBytes).AsSpan().SequenceEqual(headerHash))
        {
            throw new KdbxFormatException("En-tête KDBX corrompu (hachage SHA-256 invalide).");
        }

        var compositeKey = KdbxCrypto.DeriveCompositeKey(password);
        var transformedKey = KdbxCrypto.TransformKey(compositeKey, kdfParameters);
        var finalKey = KdbxCrypto.DeriveFinalKey(masterSeed, transformedKey);
        var hmacKeyBase = KdbxCrypto.DeriveHmacKeyBase(masterSeed, transformedKey);

        KdbxCrypto.VerifyHeaderHmac(headerBytes, headerHmac, hmacKeyBase);

        using var remainingStream = new MemoryStream(fileBytes, offset, fileBytes.Length - offset);
        var ciphertext = KdbxCrypto.ReadHmacBlockStream(remainingStream, hmacKeyBase);

        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = finalKey;
        aes.IV = encryptionIv;
        aes.Mode = System.Security.Cryptography.CipherMode.CBC;
        using var decryptor = aes.CreateDecryptor();
        var decrypted = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);

        var payload = compressionFlags == 1 ? Decompress(decrypted) : decrypted;

        return ParseInnerPayload(payload);
    }

    private static byte[] Decompress(byte[] gzipped)
    {
        using var input = new MemoryStream(gzipped);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static KdbxDocument ParseInnerPayload(byte[] payload)
    {
        var offset = 0;
        uint innerRandomStreamId = 0;
        byte[]? innerRandomStreamKey = null;

        while (true)
        {
            var fieldId = payload[offset];
            offset += 1;
            var fieldLength = BitConverter.ToInt32(payload, offset);
            offset += 4;
            var fieldData = payload[offset..(offset + fieldLength)];
            offset += fieldLength;

            if (fieldId == 0)
            {
                break;
            }

            switch (fieldId)
            {
                case 1: innerRandomStreamId = BitConverter.ToUInt32(fieldData, 0); break;
                case 2: innerRandomStreamKey = fieldData; break;
            }
        }

        if (innerRandomStreamId != 3 || innerRandomStreamKey is null)
        {
            throw new KdbxFormatException("Seul le flux interne ChaCha20 est supporté.");
        }

        var streamKeyHash = System.Security.Cryptography.SHA512.HashData(innerRandomStreamKey);
        var chachaKey = streamKeyHash[..32];
        var chachaNonce = streamKeyHash[32..44];

        var xmlBytes = payload[offset..];
        var xml = XDocument.Parse(System.Text.Encoding.UTF8.GetString(xmlBytes));

        var rootGroupXml = xml.Root!.Element("Root")!.Element("Group")!;

        // ChaCha20 protects values with ONE continuous keystream across the whole file, consumed in
        // document order — not reset per value. Collect every protected ciphertext first, decrypt the
        // concatenation in a single Apply call, then split the plaintext back out by original lengths.
        var protectedValueElements = new List<XElement>();
        CollectProtectedValues(rootGroupXml, protectedValueElements);

        var ciphertexts = protectedValueElements.Select(e => Convert.FromBase64String(e.Value)).ToList();
        var concatenatedCiphertext = ciphertexts.SelectMany(c => c).ToArray();
        var concatenatedPlaintext = ChaCha20Cipher.Apply(chachaKey, chachaNonce, concatenatedCiphertext);

        var plaintextByElement = new Dictionary<XElement, string>();
        var position = 0;
        for (var i = 0; i < protectedValueElements.Count; i++)
        {
            var length = ciphertexts[i].Length;
            var plainBytes = concatenatedPlaintext[position..(position + length)];
            plaintextByElement[protectedValueElements[i]] = System.Text.Encoding.UTF8.GetString(plainBytes);
            position += length;
        }

        var rootGroup = ParseGroup(rootGroupXml, plaintextByElement);
        return new KdbxDocument { Root = rootGroup };
    }

    private static void CollectProtectedValues(XElement groupXml, List<XElement> accumulator)
    {
        foreach (var entryXml in groupXml.Elements("Entry"))
        {
            foreach (var stringXml in entryXml.Elements("String"))
            {
                var valueXml = stringXml.Element("Value")!;
                if (valueXml.Attribute("Protected")?.Value == "True")
                {
                    accumulator.Add(valueXml);
                }
            }
        }

        foreach (var childGroupXml in groupXml.Elements("Group"))
        {
            CollectProtectedValues(childGroupXml, accumulator);
        }
    }

    private static KdbxGroup ParseGroup(XElement groupXml, Dictionary<XElement, string> plaintextByElement)
    {
        var group = new KdbxGroup { Name = groupXml.Element("Name")?.Value ?? "" };

        foreach (var entryXml in groupXml.Elements("Entry"))
        {
            group.Entries.Add(ParseEntry(entryXml, plaintextByElement));
        }

        foreach (var childGroupXml in groupXml.Elements("Group"))
        {
            group.Groups.Add(ParseGroup(childGroupXml, plaintextByElement));
        }

        return group;
    }

    private static KdbxEntry ParseEntry(XElement entryXml, Dictionary<XElement, string> plaintextByElement)
    {
        var entry = new KdbxEntry();

        foreach (var stringXml in entryXml.Elements("String"))
        {
            var key = stringXml.Element("Key")!.Value;
            var valueXml = stringXml.Element("Value")!;
            var isProtected = valueXml.Attribute("Protected")?.Value == "True";

            var value = isProtected ? plaintextByElement[valueXml] : valueXml.Value;

            switch (key)
            {
                case "Title": entry.Title = value; break;
                case "URL": entry.Url = string.IsNullOrEmpty(value) ? null : value; break;
                case "UserName": entry.UserName = string.IsNullOrEmpty(value) ? null : value; break;
                case "Password": entry.Password = value; break;
                case "Notes": entry.Notes = string.IsNullOrEmpty(value) ? null : value; break;
            }
        }

        return entry;
    }
}
