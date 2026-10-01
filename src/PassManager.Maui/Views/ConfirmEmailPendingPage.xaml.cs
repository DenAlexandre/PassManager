using PassManager.Maui.ViewModels;

namespace PassManager.Maui.Views;

public partial class ConfirmEmailPendingPage : ContentPage
{
    public ConfirmEmailPendingPage(ConfirmEmailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
