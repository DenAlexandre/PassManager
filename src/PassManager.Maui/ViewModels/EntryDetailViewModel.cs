using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PassManager.Core.Abstractions;
using PassManager.Core.Auth;

namespace PassManager.Maui.ViewModels;

public partial class EntryDetailViewModel(AuthSessionService authSession, INavigationService navigation)
    : ObservableObject, IQueryAttributable
{
    private Guid _entryId;

    [ObservableProperty]
    private string title = "";

    [ObservableProperty]
    private string url = "";

    [ObservableProperty]
    private string login = "";

    [ObservableProperty]
    private string password = "";

    [ObservableProperty]
    private bool isPasswordHidden = true;

    [ObservableProperty]
    private string memo = "";

    [ObservableProperty]
    private string? errorMessage;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("EntryId", out var value) && value is Guid entryId)
        {
            _entryId = entryId;
            Load();
        }
    }

    private void Load()
    {
        var entry = authSession.Vault?.Document.Entries.FirstOrDefault(e => e.Id == _entryId);
        if (entry is null)
        {
            return;
        }

        Title = entry.Title;
        Url = entry.Url ?? "";
        Login = entry.Login ?? "";
        Password = entry.Password;
        Memo = entry.Memo ?? "";
    }

    [RelayCommand]
    private void ToggleReveal() => IsPasswordHidden = !IsPasswordHidden;

    [RelayCommand]
    private Task CopyPasswordAsync() => Clipboard.Default.SetTextAsync(Password);

    [RelayCommand]
    private Task CopyLoginAsync() => Clipboard.Default.SetTextAsync(Login);

    [RelayCommand]
    private async Task SaveAsync()
    {
        var vault = authSession.Vault;
        if (vault is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Title))
        {
            ErrorMessage = "Le titre est requis.";
            return;
        }

        vault.UpdateEntry(_entryId, Title.Trim(), NullIfEmpty(Url), NullIfEmpty(Login), Password, NullIfEmpty(Memo));
        await authSession.SaveVaultAsync();
        await navigation.GoBackAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        var vault = authSession.Vault;
        if (vault is null)
        {
            return;
        }

        var confirmed = await Shell.Current.CurrentPage.DisplayAlert("Confirmer", "Supprimer cette entrée ?", "Supprimer", "Annuler");
        if (!confirmed)
        {
            return;
        }

        vault.DeleteEntry(_entryId);
        await authSession.SaveVaultAsync();
        await navigation.GoBackAsync();
    }

    [RelayCommand]
    private Task CancelAsync() => navigation.GoBackAsync();

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
