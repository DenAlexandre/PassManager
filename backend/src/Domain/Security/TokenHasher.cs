using System.Security.Cryptography;
using System.Text;

namespace PassManager.Domain.Security;

public static class TokenHasher
{
    public static string Sha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }
}
