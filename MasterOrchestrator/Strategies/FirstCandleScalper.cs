using cAlgo.API;
using cAlgo.API.Internals;
using cTraderV1.Core;
using cTraderV1.Models;
using System;

namespace cTraderV1.Strategies
{
    public class FirstCandleScalper : BaseStrategy
    {
        public override string Name => "FirstCandleScalper";
        private readonly RiskManager _riskManager;

        // --- Strategy Parameters ---
        private const double VolumeInLots = 0.1;

        // Strategy-specific state
        private double _firstCandleHigh;
        private double _firstCandleLow;
        private string _firstCandleDirection; // "BULLISH" or "BEARISH"
        private DateTime _expirationTime;

        private Position _activePosition;

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
                    HandleWaitingForReversalState(signals);
                    break;
                case StrategyState.Active:
                    HandleActiveState();
                    break;
            }
        }

        private void HandleIdleState(SignalCollection signals)
        {
            bool isNyOpenWindow = signals.PreTrade.ContainsKey("NY_Open_Window") && signals.PreTrade["NY_Open_Window"];
            bool isLiquidityCandle = signals.PreTrade.ContainsKey("Is_Liquidity_Candle") && signals.PreTrade["Is_Liquidity_Candle"];

            if (isNyOpenWindow && isLiquidityCandle)
            {
                Bot.Print($"[{Name}] NY Open liquidity candle detected. Waiting for Reversal.");

                _firstCandleHigh = signals.Values["1stCandle_High"];
                _firstCandleLow = signals.Values["1stCandle_Low"];
                _firstCandleDirection = (signals.Values["1stCandle_Close"] > signals.Values["1stCandle_Open"]) ? "BULLISH" : "BEARISH";

                _expirationTime = Bot.Server.Time.AddMinutes(90);

                var startTime = Bot.Bars.Last(3).OpenTime;
                // To get the bar duration, we calculate the difference between the open times of two consecutive bars.
                var barDuration = Bot.Bars.Last(1).OpenTime - Bot.Bars.Last(2).OpenTime;
                var endTime = Bot.Bars.Last(1).OpenTime.Add(barDuration);
                var boxColor = _firstCandleDirection == "BULLISH" ? Color.FromArgb(50, Color.LightGreen) : Color.FromArgb(50, Color.PaleVioletRed);
                Bot.Chart.DrawRectangle($"{Name}_Box", startTime, _firstCandleHigh, endTime, _firstCandleLow, boxColor, 4, LineStyle.Solid);

                CurrentState = StrategyState.WaitingConfirmation;
                Bot.Print($"[{Name}] Setup valid until {_expirationTime}. Direction: {_firstCandleDirection}. Waiting for reversal signal.");
            }
        }

        private void HandleWaitingForReversalState(SignalCollection signals)
        {
            if (Bot.Server.Time > _expirationTime)
            {
                Bot.Print($"[{Name}] Confirmation window expired. Resetting.");
                Reset();
                return;
            }

            var reversalBar = Bot.Bars.Last(1);
            bool isBearishReversal = signals.Confirmation.ContainsKey("Bearish_Engulfing") && signals.Confirmation["Bearish_Engulfing"] ||
                                     signals.Confirmation.ContainsKey("Bearish_PinBar") && signals.Confirmation["Bearish_PinBar"];
            bool isBullishReversal = signals.Confirmation.ContainsKey("Bullish_Engulfing") && signals.Confirmation["Bullish_Engulfing"] ||
                                     signals.Confirmation.ContainsKey("Bullish_PinBar") && signals.Confirmation["Bullish_PinBar"];

            double firstCandleSize = _firstCandleHigh - _firstCandleLow;
            double maxStopLossSize = firstCandleSize * 0.25;
            double profitTargetDistance = firstCandleSize * 0.75;

            if (_firstCandleDirection == "BULLISH" && isBearishReversal)
            {
                double entryPrice = Bot.Symbol.Bid;
                double naturalStopLossPrice = reversalBar.High;
                double stopLossDistance = naturalStopLossPrice - entryPrice;
                double stopLossPrice = naturalStopLossPrice;

                // Rule 1: Stop loss must be valid. If it's too large, adjust it to the max allowed size.
                if (stopLossDistance <= 0) return; // Invalid stop, skip trade.
                if (stopLossDistance > maxStopLossSize)
                {
                    stopLossPrice = entryPrice + maxStopLossSize;
                    Bot.Print($"[{Name}] SELL stop loss adjusted from {naturalStopLossPrice:F5} to {stopLossPrice:F5} to meet risk rules.");
                }

                // Rule 2: Set take profit based on a factor of the first candle's size.
                double takeProfitPrice = entryPrice - profitTargetDistance;
                ExecuteTrade(TradeType.Sell, stopLossPrice, takeProfitPrice);
            }
            else if (_firstCandleDirection == "BEARISH" && isBullishReversal)
            {
                double entryPrice = Bot.Symbol.Ask;
                double naturalStopLossPrice = reversalBar.Low;
                double stopLossDistance = entryPrice - naturalStopLossPrice;
                double stopLossPrice = naturalStopLossPrice;

                // Rule 1: Stop loss must be valid. If it's too large, adjust it to the max allowed size.
                if (stopLossDistance <= 0) return; // Invalid stop, skip trade.
                if (stopLossDistance > maxStopLossSize)
                {
                    stopLossPrice = entryPrice - maxStopLossSize;
                    Bot.Print($"[{Name}] BUY stop loss adjusted from {naturalStopLossPrice:F5} to {stopLossPrice:F5} to meet risk rules.");
                }

                // Rule 2: Set take profit based on a factor of the first candle's size.
                double takeProfitPrice = entryPrice + profitTargetDistance;
                ExecuteTrade(TradeType.Buy, stopLossPrice, takeProfitPrice);
            }
        }

        private void HandleActiveState()
        {
            if (_activePosition == null) return;

            // Time-Based Exit: Close position if it has been open for 90 minutes
            if (Bot.Server.Time >= _activePosition.EntryTime.AddMinutes(90))
            {
                Bot.Print($"[{Name}] 90-minute time limit reached. Closing position {_activePosition.Id}.");
                _activePosition.Close();
                // The OnPositionClosed event will handle the reset.
            }
        }

        private void ExecuteTrade(TradeType tradeType, double stopLossPrice, double takeProfitPrice)
        {
            double entryPrice = (tradeType == TradeType.Buy) ? Bot.Symbol.Ask : Bot.Symbol.Bid;
            double stopLossDistance = Math.Abs(entryPrice - stopLossPrice);

            if (!_riskManager.ValidateTrade(Name, tradeType, stopLossDistance / Bot.Symbol.PipSize))
            {
                return; // Risk validation failed
            }

            double volume = Bot.Symbol.NormalizeVolumeInUnits(VolumeInLots * Bot.Symbol.LotSize);
            // Use the overload that takes absolute SL/TP prices to avoid any ambiguity with pips.
            var result = Bot.ExecuteMarketOrder(tradeType, Bot.Symbol.Name, volume, Name, stopLossPrice, takeProfitPrice);

            if (result.IsSuccessful)
            {
                _activePosition = result.Position;
                CurrentState = StrategyState.Active;
                Bot.Print($"[{Name}] {tradeType} trade executed successfully. Position ID: {_activePosition.Id}");

                var iconType = tradeType == TradeType.Buy ? ChartIconType.UpArrow : ChartIconType.DownArrow;
                var iconColor = tradeType == TradeType.Buy ? Color.DodgerBlue : Color.Crimson;

                Bot.Chart.DrawIcon($"{Name}_Entry_{_activePosition.Id}", iconType, Bot.Server.Time, entryPrice, iconColor);
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

            var positionId = args.Position.Id;
            Bot.Chart.RemoveObject($"{Name}_Entry_{positionId}");
            Bot.Chart.RemoveObject($"{Name}_TP_{positionId}");
            Bot.Chart.RemoveObject($"{Name}_SL_{positionId}");
            Bot.Print($"[{Name}] Active trade closed. P/L: {args.Position.GrossProfit}. Resetting strategy.");
            Reset();
        }

        public override void OnTick()
        {
            // Tick-based logic is not ideal for this strategy. OnBar is sufficient.
        }

        public override void Reset()
        {
            _firstCandleHigh = 0;
            _firstCandleLow = 0;
            _firstCandleDirection = null;
            _expirationTime = DateTime.MinValue;
            _activePosition = null;

            Bot.Chart.RemoveObject($"{Name}_Box");

            base.Reset(); // This sets the state to Idle and prints a message
        }

        public override void Dispose()
        {
            Bot.Positions.Closed -= OnPositionClosed;
            base.Dispose();
        }
    }
}
