using PassManager.Maui.ViewModels;

namespace PassManager.Maui.Views;

public partial class EntryDetailPage : ContentPage
{
    public EntryDetailPage(EntryDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
