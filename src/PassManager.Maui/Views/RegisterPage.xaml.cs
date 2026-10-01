using PassManager.Maui.ViewModels;

namespace PassManager.Maui.Views;

public partial class RegisterPage : ContentPage
{
    public RegisterPage(RegisterViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
