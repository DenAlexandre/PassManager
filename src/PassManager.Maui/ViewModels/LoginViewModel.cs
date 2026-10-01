using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PassManager.Core.Abstractions;
using PassManager.Core.Auth;
using PassManager.Maui.Common;
using PassManager.Maui.Views;

namespace PassManager.Maui.ViewModels;

public partial class LoginViewModel(AuthSessionService authSession, INavigationService navigation) : ObservableObject
{
    [ObservableProperty]
    private string email = "";

    [ObservableProperty]
    private string password = "";

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool isBusy;

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var deviceId = DeviceIdProvider.GetOrCreate();
            await authSession.LoginAsync(Email.Trim(), Password, deviceId);
            Password = "";
            await navigation.NavigateToAsync($"//{nameof(VaultTreePage)}");
        }
        catch (AuthApiException ex) when (ex.Code == AuthApiErrorCode.EmailNotConfirmed)
        {
            await navigation.NavigateToAsync(nameof(ConfirmEmailPendingPage), new Dictionary<string, object> { ["Email"] = Email.Trim() });
        }
        catch (AuthApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception)
        {
            ErrorMessage = "Impossible de contacter le serveur.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task GoToRegisterAsync() => navigation.NavigateToAsync(nameof(RegisterPage));
}
