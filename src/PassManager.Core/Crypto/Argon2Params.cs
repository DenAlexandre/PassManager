namespace PassManager.Core.Crypto;

public record Argon2Params(int MemoryKiB, int Iterations, int Parallelism)
{
    public static Argon2Params Default { get; } = new(MemoryKiB: 65536, Iterations: 3, Parallelism: 2);
}
