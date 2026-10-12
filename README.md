# CryptoJournal

CryptoJournal is a Windows desktop app for recording spot and linear futures trades.
Transactions are entered manually and stored on the local machine. The app uses public
exchange APIs for prices and candles; it does not connect to an exchange account or
ask for API keys.

https://github.com/user-attachments/assets/5c1bc9f5-d1d2-4473-983f-716bc6e66eb0

## Features

- FIFO cost basis and realized PnL for spot trades
- long and short futures positions with margin, unrealized PnL, and ROE
- portfolio equity and asset allocation dashboard
- take-profit and stop-loss scenarios
- notes and image attachments for transactions
- live prices and candles from Binance, Bybit, OKX, Kraken, and MEXC
- local candle and icon caches

Spot transactions support `Buy`, `Sell`, `Deposit`, `Withdraw`, and `Fee`. Futures
transactions support `Open Long`, `Close Long`, `Open Short`, and `Close Short`.

The unrealized return shown for a spot position is based on the cost basis of the
remaining quantity. Realized proceeds from earlier sales are not part of that
percentage. Futures leverage changes margin and ROE, but does not multiply absolute
PnL. See [Accounting rules](docs/Accounting.md) for the formulas used by the app.

Liquidation prices are estimates based on isolated margin. They do not include an
exchange's maintenance-margin tiers, funding, or liquidation fees.

## Data and privacy

Application data is stored in:

```text
%LocalAppData%\CryptoJournal
```

This directory contains portfolios, settings, scenarios, attachments, and caches.
Portfolio data is stored as JSON. Saves use a temporary file and retain the previous
file as a `.bak` backup.

On the first launch after upgrading, CryptoJournal copies data from these older
locations when they exist:

```text
CryptoJournal_data                 # next to the executable
%AppData%\CryptoJournal
```

The old directories are not deleted. Keep them until the migrated portfolio has been
checked.

CryptoJournal sends requests only for public market data and coin icons. Transaction
history and attachments are not uploaded by the application.

## Installation

Download `Setup_CryptoJournal_X.Y.Z.exe` from the
[latest release](https://github.com/ButterDevelop/CryptoJournal.Wpf/releases/latest).
The installer contains the .NET runtime and supports 64-bit Windows.

Each release also includes a SHA-256 file. To verify a download in PowerShell:

```powershell
Get-FileHash .\Setup_CryptoJournal_X.Y.Z.exe -Algorithm SHA256
```

Compare the printed hash with the contents of the matching `.sha256` file. The
installer is currently unsigned, so Windows SmartScreen may show an unknown-publisher
warning.

CryptoJournal checks the latest public GitHub release when it starts. When an update
is available, the Info tab can download the installer, verify its SHA-256 checksum,
and run it over the existing installation. Portfolio data in
`%LocalAppData%\CryptoJournal` is kept during the update.

## Building from source

Requirements:

- Windows
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

```powershell
git clone https://github.com/ButterDevelop/CryptoJournal.Wpf.git
cd CryptoJournal.Wpf
dotnet build CryptoJournal.Wpf.slnx
dotnet test CryptoJournal.Wpf.slnx
```

The solution can also be opened in Visual Studio or JetBrains Rider.

## Building the installer

Install [Inno Setup 6](https://jrsoftware.org/isinfo.php), then run:

```powershell
.\scripts\Build-Installer.ps1 -Version 1.0.6
```

The script creates a self-contained `win-x64` publish, compiles the installer, and
writes both the installer and its checksum to `artifacts\installer`.

## Releases

GitHub Actions builds and tests pull requests and pushes to `main`. A tag in `vX.Y.Z`
format also publishes a GitHub Release with the installer, checksum, and generated
release notes.

```powershell
git tag v1.0.6
git push origin v1.0.6
```

The tag supplies the version for both the application and installer. The release job
rejects tags that do not match `vX.Y.Z`.

## Technology

- .NET 10 and WPF
- CommunityToolkit.Mvvm
- Microsoft.Extensions.Hosting
- MaterialDesignThemes
- LiveCharts2 and SkiaSharp
- NUnit

## License

CryptoJournal is available under the [Apache License 2.0](LICENSE).

If the project is useful to you, you can support its development through
[NOWPayments](https://nowpayments.io/donation/butterdevelop).
