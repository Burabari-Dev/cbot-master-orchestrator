using cTraderV1.Models;

namespace cTraderV1.Core
{
    /// <summary>
    /// Defines the contract for a modular trading strategy. 
    /// Each strategy encapsulates its own logic for entering, managing, and exiting trades,
    /// allowing it to be managed by a master cBot.
    /// 
    /// Every strategy you ever write will follow this contract. This allows the Master Bot to 
    /// manage 1 or 100 strategies using a single List<ITradingStrategy>
    /// </summary>
    public interface ITradingStrategy
    {
        /// <summary>
        /// Gets the unique name of the trading strategy.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the current operational state of the strategy (e.g., Idle, Active).
        /// </summary>
        StrategyState CurrentState { get; }
        
        /// <summary>
        /// Initializes the strategy. This method is called once by the master cBot on startup.
        /// Use this to set up indicators, parameters, and the initial state.
        /// </summary>
        void Initialize();

        /// <summary>
        /// The primary logic gate for the strategy, called by the master cBot on each new bar.
        /// This is where the main trading logic should reside, evaluating signals to make decisions.
        /// </summary>
        /// <param name="signals">A collection of pre-calculated signals to be evaluated by the strategy.</param>
        void OnBar(SignalCollection signals);

        /// <summary>
        /// Called by the master cBot on every incoming tick.
        /// This is useful for high-frequency or price-sensitive operations like managing trailing stops or trade protection.
        /// </summary>
        void OnTick();

        /// <summary>
        /// Resets the strategy to its initial state. This can be used to clean up chart drawings,
        /// reset variables, or prepare for a new run without restarting the cBot.
        /// </summary>
        void Reset();
    }
}