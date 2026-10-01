using PassManager.Domain.Abstractions;

namespace PassManager.Infrastructure.Common;

public class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
