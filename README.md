# CryptoJournal.Wpf 📈

A secure, offline-first desktop application designed for serious cryptocurrency traders who want complete control over their portfolio data. Track spot transactions, manage futures positions, analyze PnL, build take-profit scenarios, and document your strategic moves - all without needing an exchange API connection.

https://github.com/user-attachments/assets/5c1bc9f5-d1d2-4473-983f-716bc6e66eb0

## 🌟 Why CryptoJournal?
Unlike cloud-based portfolio trackers, CryptoJournal functions entirely offline and locally. Your transaction history, scenarios, and chart annotations stay natively on your hard drive inside `CryptoJournal_data`. It queries public market data endpoints purely to fetch live prices and candlesticks.

---

## 🚀 Core Features

### 📊 Comprehensive Dashboard & Analytics
Get an instant helicopter view of your portfolio's health:
* **Real-Time Equity Calculation:** Automatically totals the value of your free cash (USDT/USDC/USD/whatever you want), active spot positions, and locked futures margins.
* **Allocation Pie Chart:** Visually separates your portfolio into assets, accurately grouping live spot positions alongside futures margin + Unrealized PnL.
* **PnL History Bars:** Visualizes both **Realized PnL** (locked-in trades) and **Unrealized PnL** (floating market value) across your entire trading history.
* **Asset Overview:** Tracks current pricing against All-Time Highs (ATH) to instantly show how far assets have dropped or pumped.

### 💱 Supported Exchanges & Asset Icons
CryptoJournal automatically enriches your portfolio data with live market information:
* **Supported Integrations:** Out-of-the-box support for fetching live prices and historical candles from major exchanges including **Binance**, **Bybit**, **OKX**, **Kraken**, and **MEXC**. 
* **Automatic Crypto Icons:** Visually identify your assets instantly. The app automatically fetches and caches high-quality cryptocurrency icons using the **CoinGecko API**, falling back to a default coin icon if an asset is obscure.

### 💰 Spot & Margin Trading Engines
CryptoJournal utilizes dual PnL engines to accurately calculate your taxes and performance independently:

**1. Spot Trading (FIFO Engine)**
* Supports standard `Buy`, `Sell`, `Deposit`, `Withdraw`, and `Fee` transactions.
* Implements rigorous **FIFO** (First-In, First-Out) cost-basis calculations to automatically pair partial sells with your oldest buy transactions, determining the exact realized profit.

**2. Futures & Perpetual Contracts**
* Full support for `Open Long`, `Close Long`, `Open Short`, and `Close Short`.
* Calculates **Margin**, estimated **Liquidation Price**, and **ROE %** (Return on Equity) based on your custom input `Leverage`.
* Safely isolates short-selling math, inverting PnL calculations exactly like a real exchange. 

### 📝 Scenario Planner (Take-Profit & Stop-Loss)
Don't just track what happened; plan what you *will* do:
* Create dynamic, multi-leg Exit Scenarios for any open spot or futures position.
* Assign target prices and exact quantities (or percentage splits, e.g., Sell 50% at $100k, 50% at $150k).
* As you log real `Sell` or `Close` transactions, the engine automatically **consumes and removes** matching legs from your scenario, seamlessly reconciling your planned actions with reality.

### 📸 Transaction Attachments & Evidence
A trading journal is incomplete without visual context:
* **Attach Images:** Bind chart screenshots, technical analysis, or visual notes directly to any specific transaction.
* **Clipboard Support:** Just hit `Ctrl+V` (or click Paste) in the UI to instantly embed the image from your clipboard as a high-quality PNG.
* **Gallery Viewer:** Click any thumbnail to open a dedicated, floating, full-screen `ImageViewerWindow` to swipe between all attachments related to a particular trade.
* **Auto-Cleanup:** Deleting a transaction automatically garbage-collects its images from your hard drive, preventing storage bloat.

### ⚡ Local-First Reliability & Caching
* **No Database Setup:** Uses local JSON storage inside `CryptoJournal_data/`. 
* **Public Market Data:** Connects directly with public WebSockets/Rest endpoints to fetch the latest Tickers, meaning **no API Keys** or sensitive permissions are ever required.
* **Smart Icon Caching:** Downloaded crypto icons are permanently cached on your disk in `CryptoJournal_data/icons` to minimize API calls and ensure instant loading on subsequent runs.
* **Disk-Backed Candle Cache:** Historical K-Line charts are chunked and cached locally to `.bin` files, massively reducing network requests and enabling instant chart loading upon app restart.

---

## 📥 Getting Started

### Prerequisites
* Windows OS
* [.NET 10.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) 

### Installation
1. Clone the repository to your local machine:
   ```bash
   git clone https://github.com/ButterDevelop/CryptoJournal.Wpf.git
   ```
2. Open the solution (`CryptoJournal.Wpf.sln`) in Visual Studio 2022 or JetBrains Rider.
3. Set `CryptoJournal.Wpf` as the Startup Project.
4. Build and Run! 

> The app will automatically initialize a `CryptoJournal_data` folder in the same directory as the executable. All your portfolios, configurations, databases, and image attachments will securely reside there.

---

## 🛠 Tech Stack

Under the hood, CryptoJournal is built with modern, performant C# patterns:
* **C# / WPF** (Windows Presentation Foundation)
* **.NET 10.0**
* **MVVM Architecture** via `CommunityToolkit.Mvvm` (Source Generators)
* **MaterialDesignThemes** for responsive, modern UI components, smooth transitions, and dark mode.
* **LiveCharts2 (SkiaSharp)** for fluid, hardware-accelerated dashboard charting.
* **Dependency Injection** (Microsoft.Extensions.Hosting) managing domain engines, storage, and market data services.

---

## ❤️ Support & Contribution

**CryptoJournal** is built by developer who trade, for traders who want privacy. 
The software is entirely free and open-source.

If this journal saves you money, helps you nail a trade, or just keeps your mind organized, please consider starring the repository ⭐ or supporting the development!

Donate with NOWPayments!

[![Donate with NOWPayments](https://nowpayments.io/images/logo/logo.svg)](https://nowpayments.io/donation/butterdevelop)

*Developed by ButterDevelop.*
