using PassManager.Maui.ViewModels;

namespace PassManager.Maui.Views;

public partial class VaultTreePage : ContentPage
{
    private const double MinFoldersColumnWidth = 180;
    private const double MaxFoldersColumnWidth = 480;

    private readonly VaultTreeViewModel _viewModel;
    private double _panStartWidth;

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

    private void OnSplitterPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        var foldersColumn = ContentGrid.ColumnDefinitions[0];

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panStartWidth = foldersColumn.Width.Value;
                break;

            case GestureStatus.Running:
                var newWidth = Math.Clamp(_panStartWidth + e.TotalX, MinFoldersColumnWidth, MaxFoldersColumnWidth);
                foldersColumn.Width = new GridLength(newWidth);
                break;
        }
    }
}
