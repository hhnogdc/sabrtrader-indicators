# SabrTrader Indicators

A collection of standard technical indicators for the [SabrTrader](https://www.sabrtrader.com) platform.

This repository contains the source code for the built-in indicators that ship with SabrTrader — moving averages, oscillators, volatility bands, volume studies, pivots, and many more. They are provided as reference implementations and as a starting point for building your own custom indicators on top of the SabrTrader SDK.

> **Note:** This repository contains only the **standard** indicators. Premium TDU indicators are not included.

## SDK Documentation

Full documentation for the SabrTrader SDK — including the indicator API, plotting, rendering, and how to build and deploy your own indicators — is available at:

**https://www.sabrtrader.com/pages/sdk**

## What's here

Each `.cs` file is a self-contained indicator (a few come with companion `*Rendering` / `*Engine` files). Examples include:

- **Moving averages** — SMA, EMA, WMA, HMA, JMA, KAMA, ALMA, T3, TEMA, DEMA, VWMA, and more
- **Oscillators** — RSI, MACD, Stochastic, CCI, Awesome, TSI, Ultimate Oscillator, Fisher Transform
- **Volatility & channels** — Bollinger Bands, Keltner, Donchian, ATR, SuperTrend, Chandelier
- **Volume studies** — OBV, CMF, MFI, VWAP, Cumulative Delta, Volume Zones
- **Pivots & levels** — Camarilla, Fibonacci, Woodie, Murray Math, Rolling Pivots
- ...and many others

## Using an indicator

These files target the SabrTrader indicator API. To use or extend one, follow the setup and build instructions in the [SDK documentation](https://www.sabrtrader.com/pages/sdk).

## License

Released under the [MIT License](LICENSE).
