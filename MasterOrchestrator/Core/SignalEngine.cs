using cAlgo.API;
using cAlgo.API.Indicators;
using cTraderV1.Models;
using System.Collections.Generic;

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
      var serverTime = _bot.Server.Time;

      // ==========================================================
      // STRATEGY: Signals shared by multiple strategies
      // ==========================================================

      // 1. Pre-Trade Signals (The "Triggers")

      // 2. Confirmation Signals (The "Filters")
      signals.Confirmation["Bullish_Engulfing"] = IsBullishEngulfing();
      signals.Confirmation["Bearish_Engulfing"] = IsBearishEngulfing();
      signals.Confirmation["Bullish_PinBar"] = IsBullishPinBar();
      signals.Confirmation["Bearish_PinBar"] = IsBearishPinBar();

      // 3. In-Trade Signals (The "Exits/Management")

      // 4. Raw Values (For math-based decisions like Position Sizing)

      // ==========================================================
      // STRATEGY: 1st Candle Scalper (NY Open)
      // ==========================================================

      // Logic: Only calculate this at exactly 09:45 AM EST (the 15m candle close)
      // Adjust the Hour/Minute based on your broker's server time offset
      bool isNyOpenWindow = serverTime.Hour == 14 && serverTime.Minute == 45;

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

        return isPreviousBearish && isCurrentBullish && isEngulfing;
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

        return isPreviousBullish && isCurrentBearish && isEngulfing;
    }

    private bool IsBullishPinBar()
    {
        var bar = _bot.Bars.Last(1);
        double bodySize = System.Math.Abs(bar.Open - bar.Close);
        double lowerWick = System.Math.Min(bar.Open, bar.Close) - bar.Low;
        double upperWick = bar.High - System.Math.Max(bar.Open, bar.Close);

        // Long lower wick (at least 2x body), small upper wick (less than body size)
        return lowerWick > bodySize * 2 && upperWick < bodySize;
    }

    private bool IsBearishPinBar()
    {
        var bar = _bot.Bars.Last(1);
        double bodySize = System.Math.Abs(bar.Open - bar.Close);
        double upperWick = bar.High - System.Math.Max(bar.Open, bar.Close);
        double lowerWick = System.Math.Min(bar.Open, bar.Close) - bar.Low;

        // Long upper wick (at least 2x body), small lower wick (less than body size)
        return upperWick > bodySize * 2 && lowerWick < bodySize;
    }

    #endregion
  }
}