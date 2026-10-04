using PassManager.Maui.ViewModels;

namespace PassManager.Maui.Views;

public partial class VaultTreePage : ContentPage
{
    private const double MinFoldersColumnWidth = 180;
    private const double MaxFoldersColumnWidth = 480;

    private readonly VaultTreeViewModel _viewModel;
    private bool _isDraggingSplitter;
    private double _dragStartWidth;
    private double _dragStartX;

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

    private void OnSplitterPointerPressed(object? sender, PointerEventArgs e)
    {
        var position = e.GetPosition(ContentGrid);
        if (position is null)
        {
            return;
        }

        _isDraggingSplitter = true;
        _dragStartWidth = ContentGrid.ColumnDefinitions[0].Width.Value;
        _dragStartX = position.Value.X;
    }

    private void OnContentGridPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDraggingSplitter)
        {
            return;
        }

        var position = e.GetPosition(ContentGrid);
        if (position is null)
        {
            return;
        }

        var deltaX = position.Value.X - _dragStartX;
        var newWidth = Math.Clamp(_dragStartWidth + deltaX, MinFoldersColumnWidth, MaxFoldersColumnWidth);
        ContentGrid.ColumnDefinitions[0].Width = new GridLength(newWidth);
    }

    private void OnSplitterPointerReleased(object? sender, PointerEventArgs e)
    {
        _isDraggingSplitter = false;
    }
}
