using cAlgo.API;
using cTraderV1.Core;
using cTraderV1.Models;

namespace cTraderV1.Strategies
{
  /// <summary>
  /// Strategy Logic
  /// Pre-Trade Setup: Fast EMA crosses above Slow EMA (Bullish Trend identified).
  /// Confirmation: Price breaks and closes above the high of the "Cross" candle.
  /// In-Trade Management: A "Trailing Stop" logic to protect profits.
  /// </summary>
  public class TrendBreakoutStrategy : BaseStrategy
  {
    public override string Name => "TrendBreakout_V1";

    private readonly RiskManager _riskManager;
    private double _triggerHigh;
    private double _stopLossPips = 20;

    public TrendBreakoutStrategy(Robot robot, RiskManager riskManager) : base(robot)
    {
      _riskManager = riskManager;
    }

    public override void Initialize()
    {
      CurrentState = StrategyState.Idle;
    }

    public override void OnBar(SignalCollection signals)
    {
      switch (CurrentState)
      {
        case StrategyState.Idle:
          // Look for Pre-Trade Signal from SignalEngine
          if (signals.PreTrade["EMA_Cross_Up"])
          {
            _triggerHigh = Bot.Bars.HighPrices.Last(1); // Record the high to break
            CurrentState = StrategyState.WaitingConfirmation;
            Bot.Print($"{Name}: Trend detected. Waiting for breakout above {_triggerHigh}");

            // Visual Cue
            Bot.Chart.DrawIcon(Name + "_Pre", ChartIconType.UpTriangle, Bot.Bars.Count - 1, _triggerHigh, Color.Yellow);
          }
          break;

        case StrategyState.WaitingConfirmation:
          // Check for Confirmation: Close price > high of the cross candle
          if (Bot.Bars.ClosePrices.Last(1) > _triggerHigh)
          {
            if (_riskManager.ValidateTrade(Name, TradeType.Buy, _stopLossPips))
            {
              double volume = _riskManager.CalculateVolume(_stopLossPips);
              var result = Bot.ExecuteMarketOrder(TradeType.Buy, Bot.SymbolName, volume, Name, _stopLossPips, _stopLossPips * 2);

              if (result.IsSuccessful)
              {
                CurrentState = StrategyState.Active;
                Bot.Print($"{Name}: Breakout Confirmed. Trade Entered.");
              }
            }
          }
          // Reset if the trend reverses before breakout
          else if (Bot.Bars.ClosePrices.Last(1) < Bot.Bars.LowPrices.Last(2))
          {
            Reset();
          }
          break;

        case StrategyState.Active:
          // In-Trade Signal: Check if we should exit early
          if (signals.InTrade["Price_Below_EMA20"])
          {
            CloseStrategyPositions();
          }
          break;
      }
    }

    public override void OnTick()
    {
      // Performance-sensitive trailing stop could go here
    }

    private void CloseStrategyPositions()
    {
      foreach (var pos in Bot.Positions.FindAll(Name, Bot.SymbolName))
      {
        Bot.ClosePosition(pos);
      }
      Reset();
    }
  }
}