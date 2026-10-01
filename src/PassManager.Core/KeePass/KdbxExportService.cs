using PassManager.Core.Vault;

namespace PassManager.Core.KeePass;

public static class KdbxExportService
{
    public static KdbxDocument Export(VaultRepository vault)
    {
        var root = vault.GetRootFolder() ?? throw new InvalidOperationException("Coffre sans dossier racine.");
        return new KdbxDocument { Root = ExportFolder(vault, root.Id, root.Name) };
    }

    private static KdbxGroup ExportFolder(VaultRepository vault, Guid folderId, string name)
    {
        var group = new KdbxGroup { Name = name };

        foreach (var entry in vault.GetEntries(folderId))
        {
            group.Entries.Add(new KdbxEntry
            {
                Title = entry.Title,
                Url = entry.Url,
                UserName = entry.Login,
                Password = entry.Password,
                Notes = entry.Memo
            });
        }

        foreach (var childFolder in vault.GetChildFolders(folderId))
        {
            group.Groups.Add(ExportFolder(vault, childFolder.Id, childFolder.Name));
        }

        return group;
    }
}
