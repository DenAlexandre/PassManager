using PassManager.Core.Abstractions;

namespace PassManager.Maui.Services.Platform;

public class ShellNavigationService : INavigationService
{
    public Task NavigateToAsync(string route, IDictionary<string, object>? parameters = null) =>
        parameters is null
            ? Shell.Current.GoToAsync(route)
            : Shell.Current.GoToAsync(route, parameters);

    public Task GoBackAsync() => Shell.Current.GoToAsync("..");
}
