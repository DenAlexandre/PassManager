namespace PassManager.Domain.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
