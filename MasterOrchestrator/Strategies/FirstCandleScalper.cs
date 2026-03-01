using cAlgo.API;
using cAlgo.API.Internals;
using cTraderV1.Core;
using cTraderV1.Models;
using cTraderV1.Strategies;
using System;
using cAlgo.API.Indicators;

namespace cTraderV1.Strategies
{
  public class FirstCandleScalper : BaseStrategy
  {
    public override string Name => "FirstCandleScalper";
    private readonly RiskManager _riskManager;

    // Strategy-specific state
    private double _firstCandleHigh;
    private double _firstCandleLow;
    private double _firstCandleClose;
    private string _firstCandleDirection; // "BULLISH" or "BEARISH"
    private DateTime _expirationTime;

    public FirstCandleScalper(Robot robot, RiskManager riskManager) : base(robot)
    {
      _riskManager = riskManager;
    }

    public override void Initialize()
    {
      Bot.Print($"[{Name}] Initialized. State: {CurrentState}");
      Bot.Positions.Closed += OnPositionClosed;
    }

    public override void OnBar(SignalCollection signals)
    {
      switch (CurrentState)
      {
        case StrategyState.Idle:
          HandleIdleState(signals);
          break;
        case StrategyState.WaitingConfirmation:
          HandleWaitingConfirmationState(signals);
          break;
        case StrategyState.Active:
          // Logic to manage an active trade would go here if needed.
          // For now, we reset when the trade is closed (handled by OnPositionClosed).
          break;
      }
    }

    private void HandleIdleState(SignalCollection signals)
    {
      // Check for the two primary setup signals from the SignalEngine
      bool isNyOpenWindow = signals.PreTrade.ContainsKey("NY_Open_Window") && signals.PreTrade["NY_Open_Window"];
      bool isLiquidityCandle = signals.PreTrade.ContainsKey("Is_Liquidity_Candle") && signals.PreTrade["Is_Liquidity_Candle"];

      if (isNyOpenWindow && isLiquidityCandle)
      {
        Bot.Print($"[{Name}] NY Open liquidity candle detected. Moving to WaitingConfirmation.");

        // Get the synthetic 15m candle data from the SignalEngine
        _firstCandleHigh = signals.Values["1stCandle_High"];
        _firstCandleLow = signals.Values["1stCandle_Low"];
        _firstCandleClose = signals.Values["1stCandle_Close"];
        _firstCandleDirection = (signals.Values["1stCandle_Close"] > signals.Values["1stCandle_Open"]) ? "BULLISH" : "BEARISH";

        // Set the 90-minute expiration window for the setup
        _expirationTime = Bot.Server.Time.AddMinutes(90);

        // --- Draw the 15m Box on the Chart ---
        var startTime = Bot.Bars.Last(3).OpenTime;
        var endTime = Bot.Server.Time; // This is the close time of the last bar (bar3)
        var boxColor = _firstCandleDirection == "BULLISH" ? Color.FromArgb(50, Color.Green) : Color.FromArgb(50, Color.Red);
        Bot.Chart.DrawRectangle($"{Name}_Box", startTime, _firstCandleHigh, endTime, _firstCandleLow, boxColor, 1, LineStyle.Solid);
        // ---

        CurrentState = StrategyState.WaitingConfirmation;
        Bot.Print($"[{Name}] Setup valid until {_expirationTime}. Direction: {_firstCandleDirection}. Close: {_firstCandleClose}");
      }
    }

    private void HandleWaitingConfirmationState(SignalCollection signals)
    {
      // If the 90-minute window has passed, invalidate the setup and reset
      if (Bot.Server.Time > _expirationTime)
      {
        Bot.Print($"[{Name}] Confirmation window expired. Resetting.");
        Reset();
        return;
      }

      var reversalBar = Bot.Bars.Last(1);
      var barBeforeReversal = Bot.Bars.Last(2);

      // Check for a bullish reversal setup
      if (_firstCandleDirection == "BULLISH" && reversalBar.Close > _firstCandleClose)
      {
        bool isBullishReversal = signals.Confirmation.ContainsKey("Bullish_Engulfing") && signals.Confirmation["Bullish_Engulfing"] ||
                                 signals.Confirmation.ContainsKey("Bullish_PinBar") && signals.Confirmation["Bullish_PinBar"];

        if (isBullishReversal)
        {
          double stopLossPrice = barBeforeReversal.Low;
          double takeProfitPrice = _firstCandleHigh;
          ExecuteTrade(TradeType.Buy, stopLossPrice, takeProfitPrice);
        }
      }
      // Check for a bearish reversal setup
      else if (_firstCandleDirection == "BEARISH" && reversalBar.Close < _firstCandleClose)
      {
        bool isBearishReversal = signals.Confirmation.ContainsKey("Bearish_Engulfing") && signals.Confirmation["Bearish_Engulfing"] ||
                                 signals.Confirmation.ContainsKey("Bearish_PinBar") && signals.Confirmation["Bearish_PinBar"];

        if (isBearishReversal)
        {
          double stopLossPrice = barBeforeReversal.High;
          double takeProfitPrice = _firstCandleLow;
          ExecuteTrade(TradeType.Sell, stopLossPrice, takeProfitPrice);
        }
      }
    }

    private void ExecuteTrade(TradeType tradeType, double stopLossPrice, double takeProfitPrice)
    {
      double entryPrice = Bot.Bars.Last(1).Close;
      double stopLossPips = Math.Abs(entryPrice - stopLossPrice) / Bot.Symbol.PipSize;

      // // Final check with the Risk Manager
      // if (!_riskManager.ValidateTrade(Name, tradeType, stopLossPips))
      // {
      //   return; // Risk validation failed
      // }

      double volume = _riskManager.CalculateVolume(stopLossPips);
      var result = Bot.ExecuteMarketOrder(tradeType, Bot.Symbol.Name, volume, Name, stopLossPips, takeProfitPrice);

      if (result.IsSuccessful)
      {
        Bot.Print($"[{Name}] Trade executed successfully. Position ID: {result.Position.Id}");
        CurrentState = StrategyState.Active;

        // --- Draw Trade Markers on the Chart ---
        var positionId = result.Position.Id;
        var iconType = tradeType == TradeType.Buy ? ChartIconType.UpArrow : ChartIconType.DownArrow;
        var iconColor = tradeType == TradeType.Buy ? Color.DodgerBlue : Color.Crimson;

        Bot.Chart.DrawIcon($"{Name}_Entry_{positionId}", iconType, Bot.Server.Time, entryPrice, iconColor);
        Bot.Chart.DrawHorizontalLine($"{Name}_TP_{positionId}", takeProfitPrice, Color.Green, 2, LineStyle.Dots);
        Bot.Chart.DrawHorizontalLine($"{Name}_SL_{positionId}", stopLossPrice, Color.Red, 2, LineStyle.Dots);
        // ---
      }
      else
      {
        Bot.Print($"[{Name}] Trade execution failed: {result.Error}");
      }
    }

    private void OnPositionClosed(PositionClosedEventArgs args)
    {
      // We only care about this event if the strategy is in an Active state, waiting for its trade to close.
      if (CurrentState != StrategyState.Active) return;

      // Ensure we only reset if the closed position belongs to this strategy instance
      if (args.Position.Label == Name)
      {
        // Clean up drawings related to this specific trade
        var positionId = args.Position.Id;
        Bot.Chart.RemoveObject($"{Name}_Entry_{positionId}");
        Bot.Chart.RemoveObject($"{Name}_TP_{positionId}");
        Bot.Chart.RemoveObject($"{Name}_SL_{positionId}");
        Bot.Print($"[{Name}] Active trade closed. P/L: {args.Position.GrossProfit}. Resetting strategy.");
        // The event is global, so we don't unsubscribe here. We just reset the state.
        Reset();
      }
    }

    public override void OnTick()
    {
      // No tick-based logic required for this strategy
    }

    public override void Reset()
    {
      _firstCandleHigh = 0;
      _firstCandleLow = 0;
      _firstCandleClose = 0;
      _firstCandleDirection = null;
      _expirationTime = DateTime.MinValue;

      // Clean up the main box drawing
      Bot.Chart.RemoveObject($"{Name}_Box");

      base.Reset(); // This sets the state to Idle and prints a message
    }
  }
}
