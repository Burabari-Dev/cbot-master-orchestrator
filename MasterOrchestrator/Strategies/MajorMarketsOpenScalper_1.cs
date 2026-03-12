using cAlgo.API;
using cAlgo.API.Internals;
using cTraderV1.Core;
using cTraderV1.Models;

namespace cTraderV1.Strategies
{
    /// <summary>
    /// Strategy Name: MajorMarketsOpenScalper_1 (MMOS-1)
    /// Description: Places a straddle (simultaneous buy and sell) during the first hour of major market opens,
    /// aiming to catch sharp price movements. Uses a trailing stop to manage trades.
    /// </summary>
    public class MajorMarketsOpenScalper_1 : BaseStrategy
    {
        public override string Name => "MMOS-1";

        // --- Strategy Parameters ---
        private const double VolumeInLots = 0.1;
        private const int TrailingStopPips = 5;
        // --------------------------

        private Position _buyPosition;
        private Position _sellPosition;
        private bool _isWindowActive = false; // Tracks if we are inside a valid trading window

        public MajorMarketsOpenScalper_1(Robot robot) : base(robot)
        {
        }

        public override void Initialize()
        {
            Bot.Print($"[{Name}] Initialized. State: {CurrentState}");
            Bot.Positions.Closed += OnPositionClosed;
        }

        public override void OnBar(SignalCollection signals)
        {
            bool isMajorMarketOpenWindow = signals.PreTrade.ContainsKey("IsMajorMarketOpenWindow") && signals.PreTrade["IsMajorMarketOpenWindow"];
            bool isHighEfficiencyMove = signals.Confirmation.ContainsKey("IsHighEfficiencyMove") && signals.Confirmation["IsHighEfficiencyMove"];

            // --- State Management ---
            // If we enter a new window and the strategy is idle, mark the window as active.
            if (isMajorMarketOpenWindow && CurrentState == StrategyState.Idle)
            {
                _isWindowActive = true;
                Bot.Print($"[{Name}] Entered major market open window. Waiting for opportunity.");
            }
            // If the window closes, reset the flag. The strategy itself only resets when trades are done.
            else if (!isMajorMarketOpenWindow)
            {
                _isWindowActive = false;
            }
            // --------------------------

            // --- Execution Logic ---
            // We only execute if we are in a valid window and the strategy is idle (has no active trades).
            if (_isWindowActive && CurrentState == StrategyState.Idle && isHighEfficiencyMove)
            {
                Bot.Print($"[{Name}] High efficiency move detected within market open window. Executing straddle.");
                ExecuteStraddle();
            }
        }

        private void ExecuteStraddle()
        {
            Bot.Print($"[{Name}] Executing straddle trade.");
            CurrentState = StrategyState.Active; // Set state to Active to prevent re-entry

            double volumeInUnits = Bot.Symbol.NormalizeVolumeInUnits(VolumeInLots * Bot.Symbol.LotSize);

            // Execute Buy Order
            // We use the overload with a trailing stop. The SL pips here are just for the trailing mechanism.
            var buyResult = Bot.ExecuteMarketOrder(TradeType.Buy, Bot.Symbol.Name, volumeInUnits, Name, TrailingStopPips, null, "MMOS-1 Buy", hasTrailingStop: true);
            if (buyResult.IsSuccessful)
            {
                _buyPosition = buyResult.Position;
                Bot.Print($"[{Name}] Buy order placed successfully. Position ID: {_buyPosition.Id}");
            }
            else
            {
                Bot.Print($"[{Name}] Buy order failed: {buyResult.Error}");
            }

            // Execute Sell Order
            var sellResult = Bot.ExecuteMarketOrder(TradeType.Sell, Bot.Symbol.Name, volumeInUnits, Name, TrailingStopPips, null, "MMOS-1 Sell", hasTrailingStop: true);
            if (sellResult.IsSuccessful)
            {
                _sellPosition = sellResult.Position;
                Bot.Print($"[{Name}] Sell order placed successfully. Position ID: {_sellPosition.Id}");
            }
            else
            {
                Bot.Print($"[{Name}] Sell order failed: {sellResult.Error}");
            }

            // If both trades failed to open, reset immediately.
            if (_buyPosition == null && _sellPosition == null)
            {
                Reset();
            }
        }

        private void OnPositionClosed(PositionClosedEventArgs args)
        {
            // Check if the closed position belongs to this strategy instance
            if ((_buyPosition != null && args.Position.Id == _buyPosition.Id) ||
                (_sellPosition != null && args.Position.Id == _sellPosition.Id))
            {
                // Check which position was closed and nullify the reference
                if (_buyPosition != null && args.Position.Id == _buyPosition.Id)
                {
                    _buyPosition = null;
                }
                if (_sellPosition != null && args.Position.Id == _sellPosition.Id)
                {
                    _sellPosition = null;
                }

                // If both positions are now null (meaning both have been closed), reset the strategy.
                if ((_buyPosition == null || _buyPosition.Pips == 0) && (_sellPosition == null || _sellPosition.Pips == 0))
                {
                    Bot.Print($"[{Name}] Both straddle trades have closed. Resetting strategy.");
                    Reset();
                }
            }
        }

        public override void OnTick() { /* Not used in this strategy */ }
        public override void Dispose() => Bot.Positions.Closed -= OnPositionClosed;
    }
}