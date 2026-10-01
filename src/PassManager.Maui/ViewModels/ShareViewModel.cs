using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PassManager.Core.Sharing;

namespace PassManager.Maui.ViewModels;

public partial class ShareViewModel(IShareApiClient shareApiClient) : ObservableObject, IQueryAttributable
{
    private Guid _itemId;
    private bool _isEntry;

    [ObservableProperty]
    private string itemName = "";

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
        if (query.TryGetValue("FolderId", out var folderId) && folderId is Guid folderGuid)
        {
            _itemId = folderGuid;
            _isEntry = false;
        }
        else if (query.TryGetValue("EntryId", out var entryId) && entryId is Guid entryGuid)
        {
            _itemId = entryGuid;
            _isEntry = true;
        }

        if (query.TryGetValue("FolderName", out var folderName) && folderName is string folderNameValue)
        {
            ItemName = folderNameValue;
        }
        else if (query.TryGetValue("EntryName", out var entryName) && entryName is string entryNameValue)
        {
            ItemName = entryNameValue;
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
            if (_isEntry)
            {
                await shareApiClient.ShareEntryAsync(_itemId, user.Id);
            }
            else
            {
                await shareApiClient.ShareFolderAsync(_itemId, user.Id);
            }

            StatusMessage = $"« {ItemName} » a été partagé avec {user.Email}. Le destinataire le verra à sa prochaine synchronisation.";
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
