using Microsoft.AspNetCore.Identity;
using PassManager.Domain.Abstractions;
using PassManager.Domain.Entities;

namespace PassManager.Infrastructure.Auth;

public class PasswordHasherAdapter : IPasswordHasher
{
    private readonly PasswordHasher<User> _inner = new();

    public string Hash(string password) => _inner.HashPassword(default!, password);

    public PassManager.Domain.Abstractions.PasswordVerificationResult Verify(string hash, string password)
    {
        var result = _inner.VerifyHashedPassword(default!, hash, password);
        return result switch
        {
            Microsoft.AspNetCore.Identity.PasswordVerificationResult.Success => Domain.Abstractions.PasswordVerificationResult.Success,
            Microsoft.AspNetCore.Identity.PasswordVerificationResult.SuccessRehashNeeded => Domain.Abstractions.PasswordVerificationResult.SuccessRehashNeeded,
            _ => Domain.Abstractions.PasswordVerificationResult.Failed
        };
    }
}
