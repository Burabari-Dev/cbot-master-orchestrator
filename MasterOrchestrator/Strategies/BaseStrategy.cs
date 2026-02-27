using cAlgo.API;
using cTraderV1.Core;
using cTraderV1.Models;

namespace cTraderV1.Strategies
{
    /// <summary>
    /// To avoid writing the same code (like logging or drawing) in every strategy, 
    /// we create an Abstract Base Class. This is a "template" for your strategies.
    /// </summary>
    public abstract class BaseStrategy : ITradingStrategy
    {
        protected readonly Robot Bot;
        public abstract string Name { get; }
        public StrategyState CurrentState { get; protected set; } = StrategyState.Idle;

        protected BaseStrategy(Robot robot)
        {
            this.Bot = robot;
        }

        public abstract void Initialize();
        public abstract void OnBar(SignalCollection signals);
        public abstract void OnTick();

        public virtual void Reset()
        {
            CurrentState = StrategyState.Idle;
            Bot.Print($"{Name} reset to Idle.");
        }
    }
}