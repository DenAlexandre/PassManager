using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Storage;
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

    [ObservableProperty]
    private bool isMenuOpen;

    private readonly HashSet<Guid> collapsedFolderIds = [];

    public ObservableCollection<FolderNodeViewModel> Folders { get; } = [];

    public ObservableCollection<EntryRowViewModel> Entries { get; } = [];

    public void OnAppearing() => RebuildTree();

    [RelayCommand]
    private void ToggleExpand(FolderNodeViewModel folder)
    {
        if (!collapsedFolderIds.Remove(folder.Id))
        {
            collapsedFolderIds.Add(folder.Id);
        }

        RebuildTree(SelectedFolder?.Id);
    }

    partial void OnSelectedFolderChanged(FolderNodeViewModel? oldValue, FolderNodeViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }

        RefreshEntries();
    }

    [RelayCommand]
    private void SelectFolder(FolderNodeViewModel folder) => SelectedFolder = folder;

    private FolderNodeViewModel? draggedFolder;

    [RelayCommand]
    private void FolderDragStarting(FolderNodeViewModel folder) =>
        draggedFolder = folder.IsRoot ? null : folder;

    [RelayCommand]
    private async Task FolderDropAsync(FolderNodeViewModel target)
    {
        var source = draggedFolder;
        draggedFolder = null;

        if (source is null || source.Id == target.Id)
        {
            return;
        }

        await MoveFoldersAsync([source.Id], target.Id);
    }

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

    public async Task MoveFoldersAsync(IReadOnlyList<Guid> folderIds, Guid targetFolderId)
    {
        var vault = authSession.Vault;
        if (vault is null || folderIds.Count == 0)
        {
            return;
        }

        var moved = false;
        foreach (var folderId in folderIds)
        {
            if (folderId == targetFolderId)
            {
                continue;
            }

            try
            {
                vault.MoveFolder(folderId, targetFolderId);
                moved = true;
            }
            catch (InvalidOperationException)
            {
                // Déplacement invalide (racine, ou cible = soi-même/un de ses sous-dossiers) : ignoré.
            }
        }

        if (!moved)
        {
            return;
        }

        await authSession.SaveVaultAsync();
        RebuildTree(targetFolderId);
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
    private async Task EntryOptionsAsync(EntryRowViewModel entry)
    {
        var vault = authSession.Vault;
        if (vault is null)
        {
            return;
        }

        var page = Shell.Current.CurrentPage;
        var action = await page.DisplayActionSheet(entry.Title, "Annuler", null, "Modifier", "Partager", "Supprimer");

        switch (action)
        {
            case "Modifier":
                await navigation.NavigateToAsync(nameof(EntryDetailPage), new Dictionary<string, object> { ["EntryId"] = entry.Id });
                break;

            case "Partager":
                await navigation.NavigateToAsync(nameof(SharePage), new Dictionary<string, object> { ["EntryId"] = entry.Id, ["EntryName"] = entry.Title });
                break;

            case "Supprimer":
                var confirmed = await page.DisplayAlert("Confirmer", $"Supprimer « {entry.Title} » ?", "Supprimer", "Annuler");
                if (confirmed)
                {
                    vault.DeleteEntry(entry.Id);
                    await authSession.SaveVaultAsync();
                    RefreshEntries();
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

    [RelayCommand]
    private void ToggleMenu() => IsMenuOpen = !IsMenuOpen;

    [RelayCommand]
    private void CloseMenu() => IsMenuOpen = false;

    [RelayCommand]
    private async Task ImportAsync()
    {
        IsMenuOpen = false;

        var vault = authSession.Vault;
        if (vault is null)
        {
            return;
        }

        await ImportKdbxAsync(vault, Shell.Current.CurrentPage);
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        IsMenuOpen = false;

        var vault = authSession.Vault;
        if (vault is null)
        {
            return;
        }

        await ExportKdbxAsync(vault, Shell.Current.CurrentPage);
    }

    private async Task ImportKdbxAsync(PassManager.Core.Vault.VaultRepository vault, Page page)
    {
        FileResult? pickedFile;
        try
        {
            pickedFile = await PickKdbxFileAsync();
        }
        catch (Exception)
        {
            return;
        }

        if (pickedFile is null)
        {
            return;
        }

        var password = await page.DisplayPromptAsync("Mot de passe", "Mot de passe du fichier KeePass", "Importer", "Annuler");
        if (password is null)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            using var stream = await OpenPickedFileAsync(pickedFile);
            var document = await Task.Run(() => PassManager.Core.KeePass.KdbxReader.Read(stream, password));
            PassManager.Core.KeePass.KdbxImportService.Import(vault, document, new PassManager.Core.Common.SystemClock());
            await authSession.SaveVaultAsync();
            RebuildTree();
            StatusMessage = "Import KeePass terminé.";
        }
        catch (PassManager.Core.KeePass.KdbxFormatException)
        {
            ErrorMessage = "Mot de passe incorrect ou fichier invalide.";
        }
        catch (Exception)
        {
            ErrorMessage = "Échec de l'import.";
        }
        finally
        {
            IsBusy = false;
        }
    }

#if WINDOWS
    // FileResult.OpenReadAsync() on Windows calls into WindowsRuntimeStorageExtensions.OpenStreamForReadAsync,
    // which requires an internal WinRT StorageFile populated only when FileResult was constructed through
    // MAUI's own (WinRT-based) FilePicker flow. Our FileResult here was built from a plain path returned by
    // the WindowsAPICodePack dialog, so that internal state is null and OpenReadAsync throws
    // ArgumentNullException — open the path directly with plain file I/O instead.
    private static Task<Stream> OpenPickedFileAsync(FileResult file) => Task.FromResult<Stream>(File.OpenRead(file.FullPath));
#else
    private static Task<Stream> OpenPickedFileAsync(FileResult file) => file.OpenReadAsync();
#endif

#if WINDOWS
    // WinRT's FileOpenPicker throws UnauthorizedAccessException on this unpackaged app regardless of window
    // association (confirmed: a valid, non-zero hwnd still hits the same exception — this is a packaged-
    // process-identity restriction, not a window-ownership one). The Win32 common file dialog COM API has
    // no such restriction; WindowsAPICodePack.Shell.CommonFileDialogs wraps it without a WinForms/WPF dependency.
    private Task<FileResult?> PickKdbxFileAsync()
    {
        using var dialog = new WindowsAPICodePack.Dialogs.CommonOpenFileDialog
        {
            Title = "Choisir un fichier KeePass (.kdbx)",
            EnsureFileExists = true
        };
        dialog.Filters.Add(new WindowsAPICodePack.Dialogs.CommonFileDialogFilter("Fichiers KeePass", "*.kdbx"));

        var result = dialog.ShowDialog();
        FileResult? picked = result == WindowsAPICodePack.Dialogs.CommonFileDialogResult.Ok
            ? new FileResult(dialog.FileName)
            : null;
        return Task.FromResult(picked);
    }
#else
    private Task<FileResult?> PickKdbxFileAsync() =>
        FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choisir un fichier KeePass (.kdbx)" });
#endif

#if WINDOWS
    private async Task<bool> SaveKdbxFileAsync(Page page, Stream stream)
    {
        using var dialog = new WindowsAPICodePack.Dialogs.CommonSaveFileDialog
        {
            Title = "Enregistrer le fichier KeePass",
            DefaultFileName = "export",
            DefaultExtension = "kdbx"
        };
        dialog.Filters.Add(new WindowsAPICodePack.Dialogs.CommonFileDialogFilter("Fichiers KeePass", "*.kdbx"));

        if (dialog.ShowDialog() != WindowsAPICodePack.Dialogs.CommonFileDialogResult.Ok)
        {
            return false;
        }

        using var fileStream = File.Create(dialog.FileName);
        await stream.CopyToAsync(fileStream);
        return true;
    }
#else
    private async Task<bool> SaveKdbxFileAsync(Page page, Stream stream)
    {
        var result = await CommunityToolkit.Maui.Storage.FileSaver.Default.SaveAsync("export.kdbx", stream);
        return result.IsSuccessful;
    }
#endif

    private async Task ExportKdbxAsync(PassManager.Core.Vault.VaultRepository vault, Page page)
    {
        var password = await page.DisplayPromptAsync("Mot de passe", "Nouveau mot de passe pour le fichier exporté", "Suivant", "Annuler");
        if (string.IsNullOrEmpty(password))
        {
            return;
        }

        var confirmPassword = await page.DisplayPromptAsync("Confirmation", "Confirmez le mot de passe", "Exporter", "Annuler");
        if (confirmPassword != password)
        {
            ErrorMessage = "Les mots de passe ne correspondent pas.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            var document = PassManager.Core.KeePass.KdbxExportService.Export(vault);
            using var stream = new MemoryStream();
            await Task.Run(() => PassManager.Core.KeePass.KdbxWriter.Write(stream, document, password));
            stream.Position = 0;

            var saved = await SaveKdbxFileAsync(page, stream);
            if (saved)
            {
                StatusMessage = "Export KeePass terminé.";
            }
        }
        catch (Exception)
        {
            ErrorMessage = "Échec de l'export.";
        }
        finally
        {
            IsBusy = false;
        }
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
        var children = all.Where(f => f.ParentId == folder.Id).OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var isExpanded = !collapsedFolderIds.Contains(folder.Id);
        Folders.Add(new FolderNodeViewModel(folder, depth, children.Count > 0, isExpanded));

        if (!isExpanded)
        {
            return;
        }

        foreach (var child in children)
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
