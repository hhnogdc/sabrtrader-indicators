# SabrTrader Indicator Review Rules
- Reference the built-in `.cs` files in the root directory (e.g., `Ema.cs`, `Rsi.cs`, `Atr.cs`) as canonical examples of SabrTrader indicator structure.
- Check `docs/sdk-reference.md` for SabrTrader SDK lifecycle phases, series indexing (bars-ago), and plotting rules.
- When reviewing custom indicator code:
  1. Check for proper `IndicatorBase` inheritance and a public parameterless constructor.
  2. Check for safe bars-ago indexing (no out-of-bounds lookbacks on early bars).
  3. Verify clean per-bar vs. per-tick calculation logic and clear, readable C#.
