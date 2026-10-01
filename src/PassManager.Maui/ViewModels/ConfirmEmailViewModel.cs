using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PassManager.Core.Abstractions;
using PassManager.Core.Auth;
using PassManager.Maui.Views;

namespace PassManager.Maui.ViewModels;

public partial class ConfirmEmailViewModel(AuthSessionService authSession, INavigationService navigation)
    : ObservableObject, IQueryAttributable
{
    [ObservableProperty]
    private string email = "";

    [ObservableProperty]
    private string token = "";

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool isBusy;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("Email", out var value) && value is string emailValue)
        {
            Email = emailValue;
        }
    }

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;
        StatusMessage = null;
        IsBusy = true;
        try
        {
            await authSession.ConfirmEmailAsync(Email.Trim(), Token.Trim());
            StatusMessage = "Compte confirmé, vous pouvez maintenant vous connecter.";
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
    private async Task ResendAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;
        StatusMessage = null;
        IsBusy = true;
        try
        {
            await authSession.ResendConfirmationAsync(Email.Trim());
            StatusMessage = "Un nouveau lien de confirmation vient d'être envoyé.";
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
    private Task GoToLoginAsync() => navigation.NavigateToAsync($"//{nameof(LoginPage)}");
}
