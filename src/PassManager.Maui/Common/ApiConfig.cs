namespace PassManager.Maui.Common;

public static class ApiConfig
{
    /// <summary>
    /// Local dev backend (docker-compose). On Android emulator this must be "http://10.0.2.2:8080/" instead of
    /// "localhost", since the emulator's localhost refers to the emulator itself, not the host machine.
    /// </summary>
    public static string BaseUrl =>
#if ANDROID
        "http://10.0.2.2:8080/";
#else
        "http://localhost:8080/";
#endif
}
