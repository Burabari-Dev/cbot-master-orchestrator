
using cAlgo.API;
using cAlgo.API.Internals;
using cTraderV1.Core;
using cTraderV1.Models;
using System;
using System.Linq;

namespace cTraderV1.Strategies
{
    public class TradeVolumeIndexStrategy : BaseStrategy
    {
        public override string Name => "TradeVolumeIndexStrategy";
        private readonly RiskManager _riskManager;

        // --- Strategy Parameters ---
        [Parameter("TVI Threshold", Group = "TradeVolumeIndexStrategy", DefaultValue = 0.1)]
        public double TviThreshold { get; set; }
        private const double VolumeInLots = 0.1;

        // Strategy-specific state
        private double _tvi;
        private Position _activePosition;

        public TradeVolumeIndexStrategy(Robot robot, RiskManager riskManager) : base(robot)
        {
            _riskManager = riskManager;
        }

        public override void Initialize()
        {
            Bot.Print($"[{Name}] Initialized. State: {CurrentState}");
            _tvi = 0;
            Bot.Positions.Closed += OnPositionClosed;
        }

        public override void OnBar(SignalCollection signals)
        {
            // 1. Calculate TVI
            CalculateTVI();

            // 2. Implement Trading Logic based on TVI
            switch (CurrentState)
            {
                case StrategyState.Idle:
                    HandleIdleState(signals);
                    break;
                case StrategyState.Active:
                    // Active state logic can be added here if needed (e.g., trailing stops)
                    break;
            }
        }

        private void CalculateTVI()
        {
            var currentBar = Bot.Bars.Last(1);
            var previousBar = Bot.Bars.Last(2);

            double priceChange = currentBar.Close - previousBar.Close;

            if (priceChange > TviThreshold)
            {
                _tvi += currentBar.TickVolume;
            }
            else if (priceChange < -TviThreshold)
            {
                _tvi -= currentBar.TickVolume;
            }
            // If the change is within the threshold, do nothing.
        }

        private void HandleIdleState(SignalCollection signals)
        {
            // Ensure we have enough bars for the lookback period
            if (Bot.Bars.Count < 21) return;

            // Trading Logic: Confirming Breakouts
            // Correctly find the highest high and lowest low over the preceding 20 bars (excluding the current signal bar)
            // var high20 = Bot.Bars.SkipLast(1).TakeLast(20).Max(b => b.High);
            var high20 = Bot.Bars.Last(20).High;
            // var low20 = Bot.Bars.SkipLast(1).TakeLast(20).Min(b => b.Low);
            var low20 = Bot.Bars.Last(20).Low;
            bool isBreakout = Bot.Bars.Last(1).Close > high20;
            bool isBreakdown = Bot.Bars.Last(1).Close < low20;

            // Check for TVI confirmation
            bool tviConfirmsBuy = _tvi > 0; // Simplified: a rising TVI
            bool tviConfirmsSell = _tvi < 0; // Simplified: a falling TVI

            if (isBreakout && tviConfirmsBuy)
            {
                // Execute a BUY trade
                double stopLossPrice = Bot.Bars.Last(1).Low;
                double takeProfitPrice = Bot.Symbol.Ask + (Bot.Symbol.Ask - stopLossPrice) * 2; // Simple 1:2 Risk/Reward
                ExecuteTrade(TradeType.Buy, stopLossPrice, takeProfitPrice);
            }
            else if (isBreakdown && tviConfirmsSell)
            {
                // Execute a SELL trade
                double stopLossPrice = Bot.Bars.Last(1).High;
                double takeProfitPrice = Bot.Symbol.Bid - (stopLossPrice - Bot.Symbol.Bid) * 2; // Simple 1:2 Risk/Reward
                ExecuteTrade(TradeType.Sell, stopLossPrice, takeProfitPrice);
            }
        }
        
        private void ExecuteTrade(TradeType tradeType, double stopLossPrice, double takeProfitPrice)
        {
            double currentPrice = (tradeType == TradeType.Buy) ? Bot.Symbol.Ask : Bot.Symbol.Bid;
            double stopLossDistance = Math.Abs(currentPrice - stopLossPrice);
            double stopLossPips = stopLossDistance / Bot.Symbol.PipSize;
            double takeProfitPips = Math.Abs(currentPrice - takeProfitPrice) / Bot.Symbol.PipSize;

            // if (!_riskManager.ValidateTrade(Name, tradeType, stopLossPips))
            // {
            //     return; // Risk validation failed
            // }

            // Revert to a fixed lot size for performance testing.
            double volume = Bot.Symbol.NormalizeVolumeInUnits(VolumeInLots * Bot.Symbol.LotSize);
            // var result = Bot.ExecuteMarketOrder(tradeType, Bot.Symbol.Name, volume, Name, stopLossPips, takeProfitPips, "", true);
            var result = Bot.ExecuteMarketOrder(tradeType, Bot.Symbol.Name, volume, Name, stopLossPips, takeProfitPips);

            if (result.IsSuccessful)
            {
                _activePosition = result.Position;
                CurrentState = StrategyState.Active;
                Bot.Print($"[{Name}] {tradeType} trade executed successfully. Position ID: {_activePosition.Id}");
            }
            else
            {
                Bot.Print($"[{Name}] Trade execution failed: {result.Error}");
            }
        }

        private void OnPositionClosed(PositionClosedEventArgs args)
        {
            if (_activePosition == null || args.Position.Id != _activePosition.Id) return;
            Reset();
        }

        public override void OnTick()
        {
            // Not used
        }

        public override void Reset()
        {
            _activePosition = null;
            base.Reset(); // Sets state to Idle
        }



        public override void Dispose()
        {
            Bot.Positions.Closed -= OnPositionClosed;
            base.Dispose();
        }
    }
}
