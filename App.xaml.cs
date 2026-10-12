using CryptoJournal.Wpf.Exchanges;
using CryptoJournal.Wpf.Exchanges.Binance;
using CryptoJournal.Wpf.Exchanges.Bybit;
using CryptoJournal.Wpf.Exchanges.Kraken;
using CryptoJournal.Wpf.Exchanges.Mexc;
using CryptoJournal.Wpf.Exchanges.Okx;
using CryptoJournal.Wpf.Infrastructure.Time;
using CryptoJournal.Wpf.Services.Candles;
using CryptoJournal.Wpf.Services.CryptoIcon;
using CryptoJournal.Wpf.Services.CryptoIcon.CoinGecko;
using CryptoJournal.Wpf.Services.Environments;
using CryptoJournal.Wpf.Services.Exchanges;
using CryptoJournal.Wpf.Services.Portfolio;
using CryptoJournal.Wpf.Services.Prices;
using CryptoJournal.Wpf.Services.Updates;
using CryptoJournal.Wpf.Services.Validation;
using CryptoJournal.Wpf.Storage.Attachments;
using CryptoJournal.Wpf.Storage.Candles;
using CryptoJournal.Wpf.Storage.Common;
using CryptoJournal.Wpf.Storage.Portfolio;
using CryptoJournal.Wpf.Storage.Scenarios;
using CryptoJournal.Wpf.UI;
using CryptoJournal.Wpf.ViewModels;
using CryptoJournal.Wpf.ViewModels.Dialogs;
using CryptoJournal.Wpf.Views;
using CryptoJournal.Wpf.Views.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace CryptoJournal.Wpf;

public partial class App : Application
{
    private const string ApplicationMutexName = "CryptoJournal-5E7D3D4C-6E22-4F54-8C9D-1E3BFEA8A8D1";

    private IHost? _host;
    private Mutex? _applicationMutex;

    private const string USER_AGENT = "CryptoJournal.Wpf (https://github.com/ButterDevelop/CryptoJournal.Wpf)";

    protected override async void OnStartup(StartupEventArgs e)
    {
        _applicationMutex = new Mutex(false, ApplicationMutexName);

        var culture = new CultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture   = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentUICulture     = culture;
        Thread.CurrentThread.CurrentCulture       = culture;

        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

        base.OnStartup(e);

        try
        {
            AppDataMigration.CreateDefault().Migrate();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"CryptoJournal could not migrate application data to LocalAppData.\n\n{ex.Message}\n\n" +
                "The original data was not deleted. The application will now close.",
                "Application data migration failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IClock, SystemClock>();

                // Storage
                services.AddSingleton<IPortfolioStore, JsonPortfolioStore>();
                services.AddSingleton<ICandleCache,    FileCandleCache>();

                // Exchanges / APIs
                services.AddSingleton<IMarketDataClient, BinanceMarketDataClient>();
                services.AddSingleton<IMarketDataClient, BybitMarketDataClient>();
                services.AddSingleton<IMarketDataClient, OkxMarketDataClient>();
                services.AddSingleton<IMarketDataClient, KrakenMarketDataClient>();
                services.AddSingleton<IMarketDataClient, MEXCMarketDataClient>();

                services.AddSingleton<IMarketDataClientResolver, MarketDataClientResolver>();

                // Pin store
                services.AddSingleton<ISymbolExchangePinStore, JsonSymbolExchangePinStore>();

                // Services
                services.AddSingleton<CandleSyncService>();
                services.AddSingleton<PriceService>();

                services.AddSingleton<IEnvironmentStore,    JsonEnvironmentStore>();
                services.AddSingleton<IEnvironmentService,  EnvironmentService>();

                services.AddSingleton<ICostBasisEngine,     FifoCostBasisEngine>();
                services.AddSingleton<FuturesEngine>();
                services.AddSingleton<TransactionPnlCalculator>();
                services.AddSingleton<IPortfolioCalculator, PortfolioCalculator>();

                services.AddSingleton<ITradePrecheck,   TradePrecheck>();
                services.AddSingleton<IScenarioStore,   ScenarioStore>();
                services.AddSingleton<ILocalImageStore, LocalImageStore>();

                services.AddSingleton<AthService>();

                services.AddSingleton<IConfirmService, ConfirmService>();

                services.AddHttpClient()
                    .ConfigureHttpClientDefaults(builder =>
                    {
                        builder.ConfigureHttpClient(client =>
                        {
                            client.DefaultRequestHeaders.UserAgent.ParseAdd(USER_AGENT);
                        });
                    });
                services.AddSingleton<ICryptoIconUrlProvider, CoinGeckoSearchIconProvider>();
                services.AddSingleton<ICryptoIconCache,       CryptoIconCache>();

                services.AddSingleton<MarketDataPollingService>();
                services.AddSingleton<IUpdateService, GitHubUpdateService>();

                // VMs
                services.AddSingleton<TransactionsViewModel>();
                services.AddSingleton<PositionsViewModel>();
                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<MainViewModel>();
                services.AddTransient<EnvironmentManagerViewModel>();
                services.AddTransient<ScenarioEditorViewModel>();
                services.AddSingleton<FuturesViewModel>();
                services.AddSingleton<AboutViewModel>();

                // Views
                services.AddSingleton<MainWindow>();
                services.AddTransient<AddTradeDialogViewModel>();
                services.AddTransient<AddTradeDialog>();
                services.AddTransient<EnvironmentManagerDialog>();
                services.AddTransient<ConfirmDialog>();
                services.AddTransient<ScenarioEditorView>();
                services.AddTransient<AboutView>();

                services.AddSingleton<IConfirmDialogService, ConfirmDialogService>();
            })
            .Build();

        await _host.StartAsync();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        _applicationMutex?.Dispose();
        base.OnExit(e);
    }

    internal void PrepareForUpdate()
    {
        _applicationMutex?.Dispose();
        _applicationMutex = null;
    }
}
