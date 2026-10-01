using CommunityToolkit.Mvvm.ComponentModel;
using PassManager.Core.Models;

namespace PassManager.Maui.ViewModels;

public partial class FolderNodeViewModel(VaultFolder folder, int depth) : ObservableObject
{
    public Guid Id { get; } = folder.Id;
    public Guid? ParentId { get; } = folder.ParentId;
    public bool IsRoot { get; } = folder.IsRoot;
    public string Name { get; } = folder.Name;
    public int Depth { get; } = depth;
    public Thickness Indent { get; } = new(depth * 20, 0, 0, 0);
}
