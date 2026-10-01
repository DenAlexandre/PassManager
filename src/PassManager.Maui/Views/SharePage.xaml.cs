using PassManager.Maui.ViewModels;

namespace PassManager.Maui.Views;

public partial class SharePage : ContentPage
{
    public SharePage(ShareViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
