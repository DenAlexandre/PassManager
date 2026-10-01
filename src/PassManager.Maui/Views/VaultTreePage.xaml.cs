using PassManager.Maui.ViewModels;

namespace PassManager.Maui.Views;

public partial class VaultTreePage : ContentPage
{
    private readonly VaultTreeViewModel _viewModel;

    public VaultTreePage(VaultTreeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.OnAppearing();
    }
}
