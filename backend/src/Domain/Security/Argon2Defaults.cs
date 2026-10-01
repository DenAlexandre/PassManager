namespace PassManager.Domain.Security;

public static class Argon2Defaults
{
    public const int MemoryKiB = 65536;
    public const int Iterations = 3;
    public const int Parallelism = 2;
    public const int SaltSizeBytes = 16;
}
