using cAlgo.API;
using cTraderV1.Core;
using cTraderV1.Models;
using System;
using System.Linq;

namespace cTraderV1.Strategies
{
    /// <summary>
    /// Strategy Name: XxxReversal
    /// Description: Enters a trade upon identifying a reversal candlestick pattern (Engulfing or Pin Bar).
    /// Stop loss is based on the average size of the 3 candles preceding the signal.
    /// Take profit is set to 2.5x the stop loss distance.
    /// </summary>
    public class XxxReversal : BaseStrategy
    {
        public override string Name => "XxxReversal";
        private readonly RiskManager _riskManager;
        private Position _activePosition;

        // --- Strategy Parameters ---
        private const double VolumeInLots = 0.1;

        public XxxReversal(Robot robot, RiskManager riskManager) : base(robot)
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
            if (CurrentState != StrategyState.Idle)
            {
                return; // Only look for new trades when idle
            }

            // Ensure we have enough historical data for calculations
            if (Bot.Bars.Count < 5) return;

            bool isBullishReversal = signals.Confirmation.ContainsKey("Bullish_Engulfing") && signals.Confirmation["Bullish_Engulfing"] ||
                                     signals.Confirmation.ContainsKey("Bullish_PinBar") && signals.Confirmation["Bullish_PinBar"];

            bool isBearishReversal = signals.Confirmation.ContainsKey("Bearish_Engulfing") && signals.Confirmation["Bearish_Engulfing"] ||
                                     signals.Confirmation.ContainsKey("Bearish_PinBar") && signals.Confirmation["Bearish_PinBar"];

            if (!isBullishReversal && !isBearishReversal)
            {
                return; // No reversal signal on this bar
            }

            // Calculate the average size of the 3 candles *before* the signal candle
            // Signal candle is at index 1. Previous candles are at 2, 3, 4.
            // We skip the last 2 bars (current and signal) and take the 3 before that.
            double avgSize = Bot.Bars.SkipLast(2).TakeLast(3).Average(b => b.High - b.Low);

            // Stop loss is 1/3 of the average size
            double stopLossDistance = avgSize / 3.0;
            if (stopLossDistance <= 0)
            {
                Bot.Print($"[{Name}] Invalid stop loss calculated ({stopLossDistance:F5}). Skipping trade.");
                return;
            }

            // Take profit is 2.5x the stop loss
            double takeProfitDistance = stopLossDistance * 2.5;

            if (isBullishReversal)
            {
                ExecuteTrade(TradeType.Buy, stopLossDistance, takeProfitDistance);
            }
            else if (isBearishReversal)
            {
                ExecuteTrade(TradeType.Sell, stopLossDistance, takeProfitDistance);
            }
        }

        private void ExecuteTrade(TradeType tradeType, double stopLossDistance, double takeProfitDistance)
        {
            double stopLossPips = stopLossDistance / Bot.Symbol.PipSize;
            double takeProfitPips = takeProfitDistance / Bot.Symbol.PipSize;

            // if (!_riskManager.ValidateTrade(Name, tradeType, stopLossPips))
            // {
            //     return; // Risk validation failed
            // }

            // Use a fixed lot size for this strategy.
            double volume = Bot.Symbol.NormalizeVolumeInUnits(VolumeInLots * Bot.Symbol.LotSize);
            // Use the correct overload that takes SL/TP in pips.
            var result = Bot.ExecuteMarketOrder(tradeType, Bot.Symbol.Name, volume, Name, stopLossPips, takeProfitPips);

            if (result.IsSuccessful)
            {
                _activePosition = result.Position;
                CurrentState = StrategyState.Active;
                Bot.Print($"[{Name}] {tradeType} trade executed successfully. Position ID: {_activePosition.Id}");
                
                // For drawing, we need the absolute prices.
                double entryPrice = _activePosition.EntryPrice;
                double stopLossPrice = (tradeType == TradeType.Buy) ? entryPrice - stopLossDistance : entryPrice + stopLossDistance;
                double takeProfitPrice = (tradeType == TradeType.Buy) ? entryPrice + takeProfitDistance : entryPrice - takeProfitDistance;
                // Draw trade info on chart
                var iconType = tradeType == TradeType.Buy ? ChartIconType.UpTriangle : ChartIconType.DownTriangle;
                var iconColor = tradeType == TradeType.Buy ? Color.DodgerBlue : Color.Crimson;
                // Anchor the drawing to the signal bar (the one that just closed) to ensure visibility in backtesting.
                var signalBarTime = Bot.Bars.Last(1).OpenTime;
                Bot.Chart.DrawIcon($"{Name}_Entry_{_activePosition.Id}", iconType, signalBarTime, entryPrice, iconColor);

                Bot.Chart.DrawHorizontalLine($"{Name}_TP_{_activePosition.Id}", takeProfitPrice, Color.Green, 2, LineStyle.Dots);
                Bot.Chart.DrawHorizontalLine($"{Name}_SL_{_activePosition.Id}", stopLossPrice, Color.Red, 2, LineStyle.Dots);
            }
            else
            {
                Bot.Print($"[{Name}] Trade execution failed: {result.Error}");
            }
        }

        private void OnPositionClosed(PositionClosedEventArgs args)
        {
            if (_activePosition == null || args.Position.Id != _activePosition.Id) return;

            // Clean up chart objects
            Bot.Chart.RemoveObject($"{Name}_Entry_{_activePosition.Id}");
            Bot.Chart.RemoveObject($"{Name}_TP_{_activePosition.Id}");
            Bot.Chart.RemoveObject($"{Name}_SL_{_activePosition.Id}");

            Bot.Print($"[{Name}] Position {_activePosition.Id} closed. P/L: {args.Position.GrossProfit}. Resetting strategy.");
            Reset();
        }

        public override void OnTick()
        {
            // Not used in this strategy
        }

        public override void Dispose()
        {
            Bot.Positions.Closed -= OnPositionClosed;
            base.Dispose();
        }
    }
}