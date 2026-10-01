using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PassManager.Core.Abstractions;
using PassManager.Core.Auth;
using PassManager.Maui.Views;

namespace PassManager.Maui.ViewModels;

public partial class RegisterViewModel(AuthSessionService authSession, INavigationService navigation) : ObservableObject
{
    [ObservableProperty]
    private string email = "";

    [ObservableProperty]
    private string password = "";

    [ObservableProperty]
    private string confirmPassword = "";

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool isBusy;

    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Email et mot de passe sont requis.";
            return;
        }

        if (Password.Length < 8)
        {
            ErrorMessage = "Le mot de passe doit contenir au moins 8 caractères.";
            return;
        }

        if (Password != ConfirmPassword)
        {
            ErrorMessage = "Les mots de passe ne correspondent pas.";
            return;
        }

        IsBusy = true;
        try
        {
            await authSession.RegisterAsync(Email.Trim(), Password);
            await navigation.NavigateToAsync(
                nameof(ConfirmEmailPendingPage), new Dictionary<string, object> { ["Email"] = Email.Trim() });
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
    private Task GoToLoginAsync() => navigation.GoBackAsync();
}
