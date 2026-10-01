using PassManager.Core.Abstractions;

namespace PassManager.Core.Tests.TestDoubles;

public class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
