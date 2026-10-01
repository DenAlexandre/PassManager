using PassManager.Core.Abstractions;
using PassManager.Core.Vault;

namespace PassManager.Core.KeePass;

public static class KdbxImportService
{
    public static void Import(VaultRepository vault, KdbxDocument document, IClock clock)
    {
        var root = vault.GetRootFolder() ?? throw new InvalidOperationException("Coffre sans dossier racine.");
        var importFolder = vault.CreateFolder(root.Id, $"Import KeePass – {clock.UtcNow:yyyy-MM-dd}");

        ImportGroup(vault, document.Root, importFolder.Id);
    }

    private static void ImportGroup(VaultRepository vault, KdbxGroup group, Guid parentFolderId)
    {
        foreach (var entry in group.Entries)
        {
            vault.CreateEntry(parentFolderId, entry.Title, entry.Url, entry.UserName, entry.Password, entry.Notes);
        }

        foreach (var childGroup in group.Groups)
        {
            var childFolder = vault.CreateFolder(parentFolderId, childGroup.Name);
            ImportGroup(vault, childGroup, childFolder.Id);
        }
    }
}
