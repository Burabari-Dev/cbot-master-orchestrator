using cAlgo.API;
using cTraderV1.Core;
using cTraderV1.Strategies; // You'll create specific strategies here

namespace cAlgo
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class MasterOrchestrator : Robot
    {
        // --- Strategy Toggles ---
        [Parameter("First Candle Scalper", Group = "Strategies", DefaultValue = true)]
        public bool EnableFirstCandleScalper { get; set; }

        [Parameter("Major Markets Open Scalper", Group = "Strategies", DefaultValue = false)]
        public bool EnableMajorMarketsOpenScalper { get; set; }

        [Parameter("XxxReversal", Group = "Strategies", DefaultValue = false)]
        public bool EnableXxxReversal { get; set; }

        [Parameter("3 Days Reversal", Group = "Strategies", DefaultValue = false)]
        public bool EnableFirst3DaysReversal { get; set; }

        // -------------------------


        private SignalEngine _signalEngine;
        private RiskManager _riskManager;
        private StrategyManager _strategyManager;

        // Timeframe validation to ensure the bot runs on the intended chart period.
        // The "FirstCandleScalper" logic is designed for a 5-minute timeframe.
        private static readonly TimeFrame RequiredTimeFrame = TimeFrame.Minute5;

        protected override void OnStart()
        {
            if (Bars.TimeFrame != RequiredTimeFrame)
            {
                Print($"Error: This cBot is designed to run on an {RequiredTimeFrame} timeframe only. Please attach it to an {RequiredTimeFrame} chart.");
                Stop();
                return;
            }

            // 1. Initialize Core Services
            _signalEngine = new SignalEngine(this);
            _riskManager = new RiskManager(this);
            _strategyManager = new StrategyManager(this);

            // 2. Conditional Registration based on UI Parameters
            if (EnableFirstCandleScalper)
            {
                _strategyManager.RegisterStrategy(new FirstCandleScalper(this, _riskManager));
            }

            if (EnableMajorMarketsOpenScalper)
            {
                _strategyManager.RegisterStrategy(new MajorMarketsOpenScalper_1(this));
            }

            if (EnableXxxReversal)
            {
                _strategyManager.RegisterStrategy(new XxxReversal(this, _riskManager));
            }

            if (EnableFirst3DaysReversal)
            {
                _strategyManager.RegisterStrategy(new First3DaysReversal(this, _riskManager));
            }
            
            Print("Master Orchestrator Online. Waiting for market data...");
        }

        protected override void OnBar()
        {
            // Step 1: Generate Signals
            var signals = _signalEngine.GenerateSignals();

            // Step 2: Dispatch to Strategy Manager
            _strategyManager.ProcessOnBar(signals);
        }

        protected override void OnTick()
        {
            _strategyManager.ProcessOnTick();
        }

        protected override void OnStop()
        {
            _strategyManager.DisposeAll();
        }
    }
}