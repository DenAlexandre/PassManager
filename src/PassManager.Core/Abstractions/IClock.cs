namespace PassManager.Core.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
