using PassManager.Maui.Views;

namespace PassManager.Maui;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute(nameof(RegisterPage), typeof(RegisterPage));
        Routing.RegisterRoute(nameof(ConfirmEmailPendingPage), typeof(ConfirmEmailPendingPage));
        Routing.RegisterRoute(nameof(EntryDetailPage), typeof(EntryDetailPage));
        Routing.RegisterRoute(nameof(SharePage), typeof(SharePage));
    }
}
