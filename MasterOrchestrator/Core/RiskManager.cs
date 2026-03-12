using cAlgo.API;
using cTraderV1.Models;
using System.Linq;

namespace cTraderV1.Core
{
    /// <summary>
    /// The RiskManager serves as the final gatekeeper, ensuring that no matter how 
    /// aggressive a strategy's signal is, the portfolio remains protected.
    /// By centralizing risk, you prevent "Strategy Overlap" (where multiple strategies 
    /// drain your margin simultaneously) and ensure consistent position sizing based on account equity
    /// </summary>
    public class RiskManager
    {
        private readonly Robot _bot;

        // Configuration - These could also be passed from Bot Parameters
        public double MaxRiskPerTradePercent { get; set; } = 1.0; 
        public int MaxOpenPositions { get; set; } = 5;
        public double MaxDailyLossPercent { get; set; } = 5.0;

        private double _startingDailyEquity;

        public RiskManager(Robot bot)
        {
            _bot = bot;
            _startingDailyEquity = _bot.Account.Equity;
        }

        /// <summary>
        /// Validates if a trade request meets portfolio-wide safety standards.
        /// </summary>
        public bool ValidateTrade(string strategyName, TradeType type, double stopLossPips)
        {
            // 1. Check Max Open Positions
            if (_bot.Positions.Count >= MaxOpenPositions)
            {
                _bot.Print($"[Risk] Trade rejected for {strategyName}: Max open positions reached.");
                return false;
            }

            // 2. Check Daily Drawdown
            double currentDrawdown = (_startingDailyEquity - _bot.Account.Equity) / _startingDailyEquity * 100;
            if (currentDrawdown >= MaxDailyLossPercent)
            {
                _bot.Print($"[Risk] Trade rejected for {strategyName}: Max daily loss limit hit.");
                return false;
            }

            // 3. Margin Check (Basic)
            if (_bot.Account.FreeMargin < (_bot.Account.Equity * 0.1)) // Example: Require 10% free margin
            {
                _bot.Print($"[Risk] Trade rejected for {strategyName}: Insufficient free margin.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Calculates volume based on a percentage of equity and the distance to Stop Loss.
        /// </summary>
        public double CalculateVolume(double stopLossDistanceInPrice)
        {
            double riskAmount = _bot.Account.Equity * (MaxRiskPerTradePercent / 100);

            // This is a universal formula that works for Forex, Indices, and Commodities.
            // It calculates how much quantity of the asset you can trade for your given risk amount and stop loss distance.
            double quantity = riskAmount / stopLossDistanceInPrice;

            // Convert the asset quantity (e.g., 100 ounces of gold) to the broker's volume format (e.g., 1 lot).
            double volume = _bot.Symbol.QuantityToVolumeInUnits(quantity);

            // Normalize the volume to the nearest valid step for the symbol.
            return _bot.Symbol.NormalizeVolumeInUnits(volume, RoundingMode.ToNearest);
        }
    }
}