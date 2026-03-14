
using cAlgo.API;
using cAlgo.API.Internals;
using cTraderV1.Core;
using cTraderV1.Models;
using System;

namespace cTraderV1.Strategies
{
    public class TVIWithTrendStrategy : BaseStrategy
    {
        public override string Name => "TVIWithTrendStrategy";
        private readonly RiskManager _riskManager;

        // --- Strategy Parameters ---
        [Parameter("TVI Threshold", Group = "TVIWithTrendStrategy", DefaultValue = 0.1)]
        public double TviThreshold { get; set; }

        [Parameter("Trend Lookback", Group = "TVIWithTrendStrategy", DefaultValue = 30)]
        public int TrendLookback { get; set; }

        [Parameter("Strong Trend Threshold", Group = "TVIWithTrendStrategy", DefaultValue = 0.5)]
        public double StrongTrendThreshold { get; set; }

        // Strategy-specific state
        private double _tvi;
        private Position _activePosition;

        public TVIWithTrendStrategy(Robot robot, RiskManager riskManager) : base(robot)
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

            // 2. Implement Trading Logic based on TVI and Trend
            if (CurrentState == StrategyState.Idle)
            {
                HandleIdleState();
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
        }

        private void HandleIdleState()
        {
            // Ensure we have enough bars for the lookback period
            if (Bot.Bars.Count < TrendLookback + 1) return;

            var trendStrength = CalculateTrendStrength();

            bool tviConfirmsBuy = _tvi > 0; 
            bool tviConfirmsSell = _tvi < 0;

            if (trendStrength > StrongTrendThreshold && tviConfirmsBuy)
            {
                double stopLossPrice = Bot.Bars.Last(1).Low;
                double takeProfitPrice = Bot.Symbol.Ask + (Bot.Symbol.Ask - stopLossPrice) * 2; // Simple 1:2 R/R
                ExecuteTrade(TradeType.Buy, stopLossPrice, takeProfitPrice);
            }
            else if (trendStrength < -StrongTrendThreshold && tviConfirmsSell)
            {
                double stopLossPrice = Bot.Bars.Last(1).High;
                double takeProfitPrice = Bot.Symbol.Bid - (stopLossPrice - Bot.Symbol.Bid) * 2; // Simple 1:2 R/R
                ExecuteTrade(TradeType.Sell, stopLossPrice, takeProfitPrice);
            }
        }

        private double CalculateTrendStrength()
        {
            // Simple linear regression to determine trend direction and strength
            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
            for (int i = 0; i < TrendLookback; i++)
            {
                var bar = Bot.Bars.Last(i + 1);
                sumX += i;
                sumY += bar.Close;
                sumXY += i * bar.Close;
                sumX2 += i * i;
            }

            double n = TrendLookback;
            double slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);

            // Normalize the slope by the current price to make it comparable across different assets
            return (slope / Bot.Symbol.Bid) * 10000; 
        }

        private void ExecuteTrade(TradeType tradeType, double stopLossPrice, double takeProfitPrice)
        {
            double entryPrice = (tradeType == TradeType.Buy) ? Bot.Symbol.Ask : Bot.Symbol.Bid;
            double stopLossDistance = Math.Abs(entryPrice - stopLossPrice);

            if (!_riskManager.ValidateTrade(Name, tradeType, stopLossDistance / Bot.Symbol.PipSize))
            {
                return; // Risk validation failed
            }

            double volume = Bot.Symbol.NormalizeVolumeInUnits(0.1 * Bot.Symbol.LotSize);
            var result = Bot.ExecuteMarketOrder(tradeType, Bot.Symbol.Name, volume, Name, stopLossPrice, takeProfitPrice);

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
