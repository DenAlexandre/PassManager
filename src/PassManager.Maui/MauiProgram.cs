using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using PassManager.Core.Abstractions;
using PassManager.Core.Auth;
using PassManager.Core.Common;
using PassManager.Core.Crypto;
using PassManager.Core.Sharing;
using PassManager.Core.Sync;
using PassManager.Maui.Common;
using PassManager.Maui.Services.Platform;
using PassManager.Maui.ViewModels;
using PassManager.Maui.Views;

namespace PassManager.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		// Core services
		builder.Services.AddSingleton<IClock, SystemClock>();
		builder.Services.AddSingleton<VaultCryptoService>();
		builder.Services.AddSingleton<ISecureStorageService, SecureStorageService>();
		builder.Services.AddSingleton<IVaultFileStore, VaultFileStore>();
		builder.Services.AddSingleton<INavigationService, ShellNavigationService>();
		builder.Services.AddSingleton<SyncEngine>();
		builder.Services.AddSingleton<AuthSessionService>();

		builder.Services.AddHttpClient<IAuthApiClient, HttpAuthApiClient>(client =>
		{
			client.BaseAddress = new Uri(ApiConfig.BaseUrl);
		});

		builder.Services.AddTransient<AuthHeaderHandler>();
		builder.Services.AddHttpClient<ISyncApiClient, HttpSyncApiClient>(client =>
		{
			client.BaseAddress = new Uri(ApiConfig.BaseUrl);
		}).AddHttpMessageHandler<AuthHeaderHandler>();
		builder.Services.AddHttpClient<IShareApiClient, HttpShareApiClient>(client =>
		{
			client.BaseAddress = new Uri(ApiConfig.BaseUrl);
		}).AddHttpMessageHandler<AuthHeaderHandler>();

		// ViewModels
		builder.Services.AddTransient<LoginViewModel>();
		builder.Services.AddTransient<RegisterViewModel>();
		builder.Services.AddTransient<ConfirmEmailViewModel>();
		builder.Services.AddTransient<VaultTreeViewModel>();
		builder.Services.AddTransient<EntryDetailViewModel>();
		builder.Services.AddTransient<ShareViewModel>();

		// Pages
		builder.Services.AddTransient<LoginPage>();
		builder.Services.AddTransient<RegisterPage>();
		builder.Services.AddTransient<ConfirmEmailPendingPage>();
		builder.Services.AddTransient<VaultTreePage>();
		builder.Services.AddTransient<EntryDetailPage>();
		builder.Services.AddTransient<SharePage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
