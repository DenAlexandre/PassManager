namespace PassManager.Core.KeePass;

/// <summary>Raw IETF ChaCha20 (RFC 7539) keystream XOR — not AEAD. Used only for KDBX inner protected-value decryption.</summary>
public static class ChaCha20Cipher
{
    public static byte[] Apply(byte[] key, byte[] nonce, byte[] data)
    {
        if (key.Length != 32)
        {
            throw new ArgumentException("La clé ChaCha20 doit faire 32 octets.", nameof(key));
        }

        if (nonce.Length != 12)
        {
            throw new ArgumentException("Le nonce ChaCha20 doit faire 12 octets.", nameof(nonce));
        }

        var result = new byte[data.Length];
        var block = new uint[16];
        uint counter = 0;

        for (var offset = 0; offset < data.Length; offset += 64)
        {
            InitBlock(block, key, nonce, counter);
            var keystream = RunBlock(block);

            var chunkSize = Math.Min(64, data.Length - offset);
            for (var i = 0; i < chunkSize; i++)
            {
                var keystreamByte = (byte)(keystream[i / 4] >> (8 * (i % 4)));
                result[offset + i] = (byte)(data[offset + i] ^ keystreamByte);
            }

            counter += 1;
        }

        return result;
    }

    private static void InitBlock(uint[] block, byte[] key, byte[] nonce, uint counter)
    {
        block[0] = 0x61707865;
        block[1] = 0x3320646e;
        block[2] = 0x79622d32;
        block[3] = 0x6b206574;
        for (var i = 0; i < 8; i++)
        {
            block[4 + i] = BitConverter.ToUInt32(key, i * 4);
        }

        block[12] = counter;
        block[13] = BitConverter.ToUInt32(nonce, 0);
        block[14] = BitConverter.ToUInt32(nonce, 4);
        block[15] = BitConverter.ToUInt32(nonce, 8);
    }

    private static uint[] RunBlock(uint[] initial)
    {
        var state = (uint[])initial.Clone();

        for (var round = 0; round < 10; round++)
        {
            QuarterRound(state, 0, 4, 8, 12);
            QuarterRound(state, 1, 5, 9, 13);
            QuarterRound(state, 2, 6, 10, 14);
            QuarterRound(state, 3, 7, 11, 15);
            QuarterRound(state, 0, 5, 10, 15);
            QuarterRound(state, 1, 6, 11, 12);
            QuarterRound(state, 2, 7, 8, 13);
            QuarterRound(state, 3, 4, 9, 14);
        }

        var output = new uint[16];
        for (var i = 0; i < 16; i++)
        {
            output[i] = state[i] + initial[i];
        }

        return output;
    }

    private static void QuarterRound(uint[] s, int a, int b, int c, int d)
    {
        s[a] += s[b]; s[d] ^= s[a]; s[d] = RotateLeft(s[d], 16);
        s[c] += s[d]; s[b] ^= s[c]; s[b] = RotateLeft(s[b], 12);
        s[a] += s[b]; s[d] ^= s[a]; s[d] = RotateLeft(s[d], 8);
        s[c] += s[d]; s[b] ^= s[c]; s[b] = RotateLeft(s[b], 7);
    }

    private static uint RotateLeft(uint value, int bits) => (value << bits) | (value >> (32 - bits));
}
