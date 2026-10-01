using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PassManager.Core.Sharing;

namespace PassManager.Maui.ViewModels;

public partial class ShareViewModel(IShareApiClient shareApiClient) : ObservableObject, IQueryAttributable
{
    private Guid _folderId;

    [ObservableProperty]
    private string folderName = "";

    [ObservableProperty]
    private string query = "";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string? errorMessage;

    public ObservableCollection<UserSummary> Results { get; } = [];

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("FolderId", out var id) && id is Guid folderId)
        {
            _folderId = folderId;
        }

        if (query.TryGetValue("FolderName", out var name) && name is string folderNameValue)
        {
            FolderName = folderNameValue;
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
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
            Results.Clear();
            foreach (var user in await shareApiClient.SearchUsersAsync(Query.Trim()))
            {
                Results.Add(user);
            }

            if (Results.Count == 0)
            {
                StatusMessage = "Aucun compte trouvé (2 caractères minimum).";
            }
        }
        catch (Exception)
        {
            ErrorMessage = "Recherche impossible.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ShareWithAsync(UserSummary user)
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
            await shareApiClient.ShareFolderAsync(_folderId, user.Id);
            StatusMessage = $"« {FolderName} » a été partagé avec {user.Email}. Le destinataire le verra à sa prochaine synchronisation.";
        }
        catch (Exception)
        {
            ErrorMessage = "Le partage a échoué.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
