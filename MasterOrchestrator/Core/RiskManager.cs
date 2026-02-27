using cAlgo.API;
using cTraderV1.Models;
using System.Linq;

namespace cTraderV1.Core
{
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
        public double CalculateVolume(double stopLossPips)
        {
            double riskAmount = _bot.Account.Equity * (MaxRiskPerTradePercent / 100);
            
            // Calculate volume based on pip value
            // Note: This is a simplified calculation; cTrader's Symbol.VolumeInUnits 
            // varies by asset class (Forex vs Indices).
            double volume = _bot.Symbol.QuantityToVolumeInUnits(riskAmount / (stopLossPips * _bot.Symbol.PipValue));
            
            return _bot.Symbol.NormalizeVolumeInUnits(volume, RoundingMode.ToNearest);
        }
    }
}