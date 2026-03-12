using cAlgo.API;
using cAlgo.API.Indicators;
using cTraderV1.Models;
using System.Collections.Generic;
using System;

namespace cTraderV1.Core
{
  /// <summary>
  /// The SignalEngine is the "translator" of your architecture. Its job is to take raw 
  /// market data (candles and indicators) and map them into the SignalCollection dictionary.
  /// 
  /// By centralizing this, you ensure that if three different strategies all use the RSI, 
  /// the RSI is only calculated once, saving significant CPU resources during backtesting
  /// </summary>
  public class SignalEngine
  {
    private readonly Robot _bot;

    // Define Indicators here
    private AverageTrueRange _atr;

    // Efficiency Filter Parameters
    private const double EfficiencyMultiplier = 2.0;

    public SignalEngine(Robot bot)
    {
      _bot = bot;
      InitIndicators();
    }

    private void InitIndicators()
    {
      // Initialize your technical indicators
      _atr = _bot.Indicators.AverageTrueRange(14, MovingAverageType.Exponential);
    }

    /// <summary>
    /// Translates raw data into a structured collection for strategies to consume.
    /// </summary>
    public SignalCollection GenerateSignals()
    {
      var signals = new SignalCollection();

      // ==========================================================
      // STRATEGY: Signals shared by multiple strategies
      // ==========================================================

      // 1. Pre-Trade Signals (The "Triggers")

      // 2. Confirmation Signals (The "Filters")
      signals.Confirmation["Bullish_Engulfing"] = IsBullishEngulfing();
      signals.Confirmation["Bearish_Engulfing"] = IsBearishEngulfing();
      signals.Confirmation["Bullish_PinBar"] = IsBullishPinBar();
      signals.Confirmation["Bearish_PinBar"] = IsBearishPinBar();
      signals.Confirmation["IsHighEfficiencyMove"] = IsHighEfficiencyMove();

      // MMOS-1 Signal
      signals.PreTrade["IsMajorMarketOpenWindow"] = IsInMajorMarketOpenWindow();

      // 3. In-Trade Signals (The "Exits/Management")

      // 4. Raw Values (For math-based decisions like Position Sizing)

      // ==========================================================
      // STRATEGY: 1st Candle Scalper (NY Open)
      // ==========================================================

      // 1. Define the New York Time Zone and convert server UTC time.
      // This correctly handles all daylight saving time adjustments.
      var nyZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
      DateTime nyTime = TimeZoneInfo.ConvertTimeFromUtc(_bot.Server.TimeInUtc, nyZone);

      // 2. Check if it's the close of the first 15m candle (9:45 AM NY time).
      bool isNyOpenWindow = nyTime.Hour == 9 && nyTime.Minute == 45;
      if (isNyOpenWindow)
      {
        // 1. Compute 14-Day ATR
        // Note: We get the ATR from the bot's timeframe, not M15 explicitly.
        // This assumes the bot is running on the M15 chart.
        double atrValue = _atr.Result.Last(1); 

        // 2. Aggregate the first three 5m bars to create a synthetic 15m candle
        // The trigger time (14:45) is the close of the third 5m bar (14:30, 14:35, 14:40)
        var bar1 = _bot.Bars.Last(3); // 14:30 - 14:35 bar
        var bar2 = _bot.Bars.Last(2); // 14:35 - 14:40 bar
        var bar3 = _bot.Bars.Last(1); // 14:40 - 14:45 bar

        double syntheticHigh = System.Math.Max(bar1.High, System.Math.Max(bar2.High, bar3.High));
        double syntheticLow = System.Math.Min(bar1.Low, System.Math.Min(bar2.Low, bar3.Low));
        double syntheticOpen = bar1.Open;
        double syntheticClose = bar3.Close;
        double candleSize = syntheticHigh - syntheticLow;
        double threshold = atrValue * 0.25;

        // 3. Map to Signals
        // The strategy expects "NY_Open_Window" and "Is_Liquidity_Candle"
        signals.PreTrade["NY_Open_Window"] = true;
        signals.PreTrade["Is_Liquidity_Candle"] = candleSize >= threshold;

        // Store the synthetic candle's properties for the strategy to use
        signals.Values["1stCandle_High"] = syntheticHigh;
        signals.Values["1stCandle_Low"] = syntheticLow;
        signals.Values["1stCandle_Open"] = syntheticOpen;
        signals.Values["1stCandle_Close"] = syntheticClose;
        signals.Values["1stCandle_ATR_Threshold"] = threshold;
        signals.Values["1stCandle_ActualSize"] = candleSize;
      }

      return signals;
    }

    /// <summary>
    /// Checks if the current time is within one hour of a major market open (UTC).
    /// </summary>
    private bool IsInMajorMarketOpenWindow() // Correctly handles Daylight Saving Time
    {
        var currentTimeUtc = _bot.Server.TimeInUtc;

        // New York: Opens at 9:30 AM local time
        var nyZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var nyTime = TimeZoneInfo.ConvertTimeFromUtc(currentTimeUtc, nyZone);
        if (nyTime.Hour == 9 && nyTime.Minute >= 30)
            return true;

        // London: Opens at 8:00 AM local time
        var londonZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
        var londonTime = TimeZoneInfo.ConvertTimeFromUtc(currentTimeUtc, londonZone);
        if (londonTime.Hour == 8)
            return true;

        // Tokyo: Opens at 9:00 AM local time (Japan does not observe DST)
        var tokyoZone = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
        var tokyoTime = TimeZoneInfo.ConvertTimeFromUtc(currentTimeUtc, tokyoZone);
        if (tokyoTime.Hour == 9)
            return true;

        // Sydney: Opens at 10:00 AM local time
        var sydneyZone = TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time");
        var sydneyTime = TimeZoneInfo.ConvertTimeFromUtc(currentTimeUtc, sydneyZone);
        if (sydneyTime.Hour == 10)
            return true;

        return false;
    }

    /// <summary>
    /// Calculates if the most recent bar's price movement per tick is significantly
    /// higher than the recent average, indicating a high-quality move.
    /// </summary>
    private bool IsHighEfficiencyMove()
    {
        int lookback = 6;
        // Need enough bars for the lookback period plus the current bar.
        if (_bot.Bars.Count < lookback + 2) return false;

        // We use Index 1 for the candle that just closed
        double currentMove = Math.Abs(_bot.Bars.ClosePrices.Last(1) - _bot.Bars.OpenPrices.Last(1));
        double currentTicks = _bot.Bars.TickVolumes.Last(1);

        if (currentTicks == 0) return false;

        double currentEff = currentMove / currentTicks;

        // Average efficiency over the last 'lookback' candles (from index 2 to lookback+1)
        double sumEff = 0;
        for (int i = 2; i <= lookback + 1; i++)
        {
            double move = Math.Abs(_bot.Bars.ClosePrices.Last(i) - _bot.Bars.OpenPrices.Last(i));
            double ticks = _bot.Bars.TickVolumes.Last(i);
            if (ticks > 0)
            {
                sumEff += (move / ticks);
            }
        }

        double avgEff = sumEff / lookback;

        // A high-efficiency move occurs if the current bar's efficiency is a multiple of the average.
        return currentEff > (avgEff * EfficiencyMultiplier);
    }

    #region Candlestick Pattern Methods

    private bool IsBullishEngulfing()
    {
        // Needs at least 2 bars to compare
        if (_bot.Bars.Count < 2) return false;

        var currentBar = _bot.Bars.Last(1);
        var previousBar = _bot.Bars.Last(2);

        // Previous bar must be bearish, current bar must be bullish
        bool isPreviousBearish = previousBar.Close < previousBar.Open;
        bool isCurrentBullish = currentBar.Close > currentBar.Open;

        // Current bar's body must engulf the previous bar's body
        bool isEngulfing = currentBar.Open < previousBar.Close && currentBar.Close > previousBar.Open;

        // New Rule: Wicks must be small, indicating a strong move.
        double totalCandleSize = currentBar.High - currentBar.Low;
        if (totalCandleSize == 0) return false; // Avoid division by zero on doji candles

        double upperWick = currentBar.High - currentBar.Close;
        double lowerWick = currentBar.Open - currentBar.Low;
        bool hasSmallWicks = (upperWick / totalCandleSize < 0.05) && (lowerWick / totalCandleSize < 0.05);

        return isPreviousBearish && isCurrentBullish && isEngulfing && hasSmallWicks;
    }

    private bool IsBearishEngulfing()
    {
        if (_bot.Bars.Count < 2) return false;

        var currentBar = _bot.Bars.Last(1);
        var previousBar = _bot.Bars.Last(2);

        // Previous bar must be bullish, current bar must be bearish
        bool isPreviousBullish = previousBar.Close > previousBar.Open;
        bool isCurrentBearish = currentBar.Close < currentBar.Open;

        // Current bar's body must engulf the previous bar's body
        bool isEngulfing = currentBar.Open > previousBar.Close && currentBar.Close < previousBar.Open;

        // New Rule: Wicks must be small, indicating a strong move.
        double totalCandleSize = currentBar.High - currentBar.Low;
        if (totalCandleSize == 0) return false; // Avoid division by zero on doji candles

        double upperWick = currentBar.High - currentBar.Open;
        double lowerWick = currentBar.Close - currentBar.Low;
        bool hasSmallWicks = (upperWick / totalCandleSize < 0.05) && (lowerWick / totalCandleSize < 0.05);

        return isPreviousBullish && isCurrentBearish && isEngulfing && hasSmallWicks;
    }

    private bool IsBullishPinBar()
    {
        var bar = _bot.Bars.Last(1);
        double bodySize = System.Math.Abs(bar.Open - bar.Close);
        if (bodySize == 0) return false; // Cannot have a wick 4x a zero-sized body

        double lowerWick = System.Math.Min(bar.Open, bar.Close) - bar.Low;
        double upperWick = bar.High - System.Math.Max(bar.Open, bar.Close);
        double totalCandleSize = bar.High - bar.Low;
        if (totalCandleSize == 0) return false; // Avoid division by zero

        // New Rule: Long lower wick (>= 4x body), small upper wick (< 4% of total candle size)
        bool isLongLowerWick = lowerWick >= bodySize * 4;
        bool isSmallUpperWick = upperWick < totalCandleSize * 0.04;

        return isLongLowerWick && isSmallUpperWick;
    }

    private bool IsBearishPinBar()
    {
        var bar = _bot.Bars.Last(1);
        double bodySize = System.Math.Abs(bar.Open - bar.Close);
        if (bodySize == 0) return false; // Cannot have a wick 4x a zero-sized body

        double upperWick = bar.High - System.Math.Max(bar.Open, bar.Close);
        double lowerWick = System.Math.Min(bar.Open, bar.Close) - bar.Low;
        double totalCandleSize = bar.High - bar.Low;
        if (totalCandleSize == 0) return false; // Avoid division by zero

        // New Rule: Long upper wick (>= 4x body), small lower wick (< 4% of total candle size)
        bool isLongUpperWick = upperWick >= bodySize * 4;
        bool isSmallLowerWick = lowerWick < totalCandleSize * 0.04;

        return isLongUpperWick && isSmallLowerWick;
    }

    #endregion
  }
}