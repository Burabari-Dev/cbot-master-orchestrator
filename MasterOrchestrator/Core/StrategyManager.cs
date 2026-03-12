using System.Collections.Generic;
using cTraderV1.Core;
using cTraderV1.Models;
using cAlgo.API;

namespace cTraderV1.Core
{
    /// <summary>
    /// The StrategyManager is the "Registry" of your architecture. Its primary purpose 
    /// is to maintain a lifecycle record of every strategy you’ve plugged into the bot.
    /// 
    /// In a multi-strategy environment, the StrategyManager prevents the "Master Bot" from 
    /// becoming cluttered with management code. It handles the registration, distribution 
    /// of signals, and cleanup of finished strategies.
    /// </summary>
    public class StrategyManager
    {
        private readonly Robot _bot;
        private readonly List<ITradingStrategy> _allStrategies = new List<ITradingStrategy>();

        public StrategyManager(Robot bot)
        {
            _bot = bot;
        }

        /// <summary>
        /// Registers a strategy into the orchestrator.
        /// </summary>
        public void RegisterStrategy(ITradingStrategy strategy)
        {
            strategy.Initialize();
            _allStrategies.Add(strategy);
            _bot.Print($"[StrategyManager] {strategy.Name} registered and initialized.");
        }

        /// <summary>
        /// Distributes signals to all registered strategies.
        /// </summary>
        public void ProcessOnBar(SignalCollection signals)
        {
            foreach (var strategy in _allStrategies)
            {
                // We wrap this in a try-catch so one buggy strategy 
                // doesn't crash the entire Master Bot.
                try 
                {
                    strategy.OnBar(signals);
                }
                catch (System.Exception ex)
                {
                    _bot.Print($"[Error] Strategy {strategy.Name} failed OnBar: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Passes tick updates for real-time management (trailing stops, etc).
        /// </summary>
        public void ProcessOnTick()
        {
            foreach (var strategy in _allStrategies)
            {
                strategy.OnTick();
            }
        }

        /// <summary>
        /// Cleans up all registered strategies.
        /// </summary>
        public void DisposeAll()
        {
            foreach (var strategy in _allStrategies)
            {
                strategy.Dispose();
            }
            _bot.Print("[StrategyManager] All strategies disposed.");
        }

        // Returns a list for the Master Bot to display status if needed
        public List<ITradingStrategy> GetActiveStrategies() => _allStrategies;
    }
}