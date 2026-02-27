using cAlgo.API;
using cTraderV1.Core;
using cTraderV1.Strategies; // You'll create specific strategies here

namespace cAlgo
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class MasterOrchestrator : Robot
    {
        private SignalEngine _signalEngine;
        private RiskManager _riskManager;
        private StrategyManager _strategyManager;

        protected override void OnStart()
        {
            // 1. Initialize Core Services
            _signalEngine = new SignalEngine(this);
            _riskManager = new RiskManager(this);
            _strategyManager = new StrategyManager(this);

            // 2. Register Strategies (Example)
            // _strategyManager.RegisterStrategy(new TrendFollowStrategy(this, _riskManager));
            
            Print("V1 Architecture Online. Waiting for first bar...");
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
    }
}