# Accounting rules

CryptoJournal calculates spot and linear futures positions from the transaction
ledger. All calculations use the same chronological order: `TimeUtc`, then `Id`.

## Spot positions

A `Buy` creates a FIFO lot. Its cost basis is:

```text
quantity × price + feeQuote
```

A base-asset `Deposit` also creates a FIFO lot. Its `Price` is the supplied cost basis
per unit. `FeeQuote` is paid from quote-currency cash and is included in the lot's cost
basis; it is not subtracted from the deposited quantity.

A `Sell` consumes the oldest available lots. Realized PnL is net proceeds minus the
cost basis consumed by that sale. A `Withdraw` consumes lots without realizing PnL.

For an open spot position:

```text
unrealized PnL    = market value - remaining cost basis
unrealized return = unrealized PnL / remaining cost basis
```

For example, buying 11,250 STRK for 450 and later selling 3,750 STRK for 450 leaves
7,500 STRK with a cost basis of 300. At a mark price of 0.12, its market value is 900,
unrealized PnL is 600, and unrealized return is 200%.

## Linear futures

```text
margin       = quantity × entry price / leverage
long PnL     = (close price - entry price) × quantity - closing fee
short PnL    = (entry price - close price) × quantity - closing fee
ROE          = unrealized PnL / margin
```

Leverage changes margin and ROE, not absolute PnL. Opening fees reduce quote cash when
the position is opened. Closing fees reduce realized PnL and are split proportionally
when one close consumes several FIFO lots.

When open lots have different leverage, the displayed leverage is the effective value:

```text
effective leverage = total notional / total margin
```

Liquidation price is an estimate. Exchange-specific maintenance margin, funding, and
liquidation fees are outside the current model.

## Invalid ledgers

The calculator reports an issue when a transaction tries to sell, withdraw, or close
more than is available. It does not create balancing lots or rewrite the stored
transaction. Calculation stops for the affected symbol and side; unrelated positions
can still be calculated.

## Persistence

Portfolio saves are serialized per environment. A save is written to a temporary
file, flushed, and atomically promoted. When a portfolio already exists, its previous
contents are retained in a `.bak` file.

Application data lives in `%LocalAppData%\CryptoJournal`. The first launch copies
legacy data from an executable-relative `CryptoJournal_data` directory and from
`%AppData%\CryptoJournal`. The sources remain untouched after migration.

## Regression tests

The test suite covers FIFO lot preservation, fees, deposits and withdrawals, invalid
quantities, deterministic ordering, spot and futures transaction PnL, mixed leverage,
cash reconciliation, storage recovery, and data migration.

```powershell
dotnet test CryptoJournal.Wpf.slnx
```
