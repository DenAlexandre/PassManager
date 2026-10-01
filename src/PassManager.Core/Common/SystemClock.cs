using PassManager.Core.Abstractions;

namespace PassManager.Core.Common;

public class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
