using CommunityToolkit.Mvvm.ComponentModel;
using PassManager.Core.Models;

namespace PassManager.Maui.ViewModels;

public partial class EntryRowViewModel(VaultEntry entry) : ObservableObject
{
    public Guid Id { get; } = entry.Id;
    public string Title { get; } = entry.Title;
    public string? Url { get; } = entry.Url;
    public string? Login { get; } = entry.Login;
}
