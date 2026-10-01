namespace PassManager.Maui.Common;

/// <summary>A stable per-install identifier sent to the API so refresh tokens can be tied to a device.</summary>
public static class DeviceIdProvider
{
    private const string PreferenceKey = "device_id";

    public static string GetOrCreate()
    {
        var existing = Preferences.Default.Get(PreferenceKey, string.Empty);
        if (!string.IsNullOrEmpty(existing))
        {
            return existing;
        }

        var id = Guid.NewGuid().ToString();
        Preferences.Default.Set(PreferenceKey, id);
        return id;
    }
}
