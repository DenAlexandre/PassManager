using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PassManager.Core.Abstractions;
using PassManager.Core.Auth;
using PassManager.Core.Models;
using PassManager.Maui.Views;

namespace PassManager.Maui.ViewModels;

public partial class VaultTreeViewModel(AuthSessionService authSession, INavigationService navigation) : ObservableObject
{
    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private FolderNodeViewModel? selectedFolder;

    public ObservableCollection<FolderNodeViewModel> Folders { get; } = [];

    public ObservableCollection<EntryRowViewModel> Entries { get; } = [];

    public void OnAppearing() => RebuildTree();

    partial void OnSelectedFolderChanged(FolderNodeViewModel? value) => RefreshEntries();

    [RelayCommand]
    private async Task SyncAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            await authSession.SyncAsync();
            var selectedId = SelectedFolder?.Id;
            RebuildTree(selectedId);
            StatusMessage = "Synchronisation terminée.";
        }
        catch (Exception)
        {
            ErrorMessage = "Synchronisation impossible (serveur injoignable ?).";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task NewFolderAsync()
    {
        var vault = authSession.Vault;
        if (vault is null || SelectedFolder is null)
        {
            return;
        }

        var name = await Shell.Current.CurrentPage.DisplayPromptAsync("Nouveau dossier", "Nom du dossier");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        vault.CreateFolder(SelectedFolder.Id, name.Trim());
        await authSession.SaveVaultAsync();
        RebuildTree(SelectedFolder.Id);
    }

    [RelayCommand]
    private async Task NewEntryAsync()
    {
        var vault = authSession.Vault;
        if (vault is null || SelectedFolder is null)
        {
            return;
        }

        var entry = vault.CreateEntry(SelectedFolder.Id, "Nouvelle entrée", null, null, "", null);
        await authSession.SaveVaultAsync();
        RefreshEntries();
        await navigation.NavigateToAsync(nameof(EntryDetailPage), new Dictionary<string, object> { ["EntryId"] = entry.Id });
    }

    [RelayCommand]
    private Task OpenEntryAsync(EntryRowViewModel entry) =>
        navigation.NavigateToAsync(nameof(EntryDetailPage), new Dictionary<string, object> { ["EntryId"] = entry.Id });

    [RelayCommand]
    private async Task FolderOptionsAsync(FolderNodeViewModel folder)
    {
        var vault = authSession.Vault;
        if (vault is null || folder.IsRoot)
        {
            return;
        }

        var page = Shell.Current.CurrentPage;
        var action = await page.DisplayActionSheet(folder.Name, "Annuler", null, "Renommer", "Partager", "Supprimer");

        switch (action)
        {
            case "Renommer":
                var newName = await page.DisplayPromptAsync("Renommer", "Nouveau nom", initialValue: folder.Name);
                if (!string.IsNullOrWhiteSpace(newName))
                {
                    vault.RenameFolder(folder.Id, newName.Trim());
                    await authSession.SaveVaultAsync();
                    RebuildTree(folder.Id);
                }
                break;

            case "Partager":
                await navigation.NavigateToAsync(nameof(SharePage), new Dictionary<string, object> { ["FolderId"] = folder.Id, ["FolderName"] = folder.Name });
                break;

            case "Supprimer":
                var confirmed = await page.DisplayAlert("Confirmer", $"Supprimer « {folder.Name} » et tout son contenu ?", "Supprimer", "Annuler");
                if (confirmed)
                {
                    vault.DeleteFolder(folder.Id);
                    await authSession.SaveVaultAsync();
                    RebuildTree();
                }
                break;
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        await authSession.LogoutAsync();
        await navigation.NavigateToAsync($"//{nameof(LoginPage)}");
    }

    private void RebuildTree(Guid? preferredSelectionId = null)
    {
        Folders.Clear();
        var vault = authSession.Vault;
        if (vault is null)
        {
            return;
        }

        var all = vault.GetFolders();
        var root = all.FirstOrDefault(f => f.IsRoot);
        if (root is not null)
        {
            AddRecursive(root, 0, all);
        }

        SelectedFolder = (preferredSelectionId is not null ? Folders.FirstOrDefault(f => f.Id == preferredSelectionId) : null)
            ?? Folders.FirstOrDefault(f => f.Id == SelectedFolder?.Id)
            ?? Folders.FirstOrDefault();

        RefreshEntries();
    }

    private void AddRecursive(VaultFolder folder, int depth, IReadOnlyList<VaultFolder> all)
    {
        Folders.Add(new FolderNodeViewModel(folder, depth));
        foreach (var child in all.Where(f => f.ParentId == folder.Id).OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            AddRecursive(child, depth + 1, all);
        }
    }

    private void RefreshEntries()
    {
        Entries.Clear();
        var vault = authSession.Vault;
        if (vault is null || SelectedFolder is null)
        {
            return;
        }

        foreach (var entry in vault.GetEntries(SelectedFolder.Id).OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            Entries.Add(new EntryRowViewModel(entry));
        }
    }
}
