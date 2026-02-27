using cAlgo.API;
using cAlgo.API.Indicators;
using cTraderV1.Models;
using System.Collections.Generic;

namespace cTraderV1.Core
{
    public class SignalEngine
    {
        private readonly Robot _bot;
        
        // Define Indicators here
        private RelativeStrengthIndex _rsi;
        private AverageTrueRange _atr;
        private ExponentialMovingAverage _emaFast;
        private ExponentialMovingAverage _emaSlow;

        public SignalEngine(Robot bot)
        {
            _bot = bot;
            InitIndicators();
        }

        private void InitIndicators()
        {
            // Initialize your technical indicators
            _rsi = _bot.Indicators.RelativeStrengthIndex(_bot.Bars.ClosePrices, 14);
            _atr = _bot.Indicators.AverageTrueRange(14, MovingAverageType.Exponential);
            _emaFast = _bot.Indicators.ExponentialMovingAverage(_bot.Bars.ClosePrices, 20);
            _emaSlow = _bot.Indicators.ExponentialMovingAverage(_bot.Bars.ClosePrices, 50);
        }

        /// <summary>
        /// Translates raw data into a structured collection for strategies to consume.
        /// </summary>
        public SignalCollection GenerateSignals()
        {
            var signals = new SignalCollection();

            // 1. Pre-Trade Signals (The "Triggers")
            signals.PreTrade["RSI_Overbought"] = _rsi.Result.Last(1) > 70;
            signals.PreTrade["RSI_Oversold"] = _rsi.Result.Last(1) < 30;
            signals.PreTrade["EMA_Cross_Up"] = _emaFast.Result.HasCrossedAbove(_emaSlow.Result, 1);

            // 2. Confirmation Signals (The "Filters")
            signals.Confirmation["Price_Above_EMA50"] = _bot.Bars.ClosePrices.Last(1) > _emaSlow.Result.Last(1);
            signals.Confirmation["Volume_Increasing"] = _bot.Bars.TickVolumes.Last(1) > _bot.Bars.TickVolumes.Last(2);

            // 3. In-Trade Signals (The "Exits/Management")
            signals.InTrade["RSI_Exit_Long"] = _rsi.Result.Last(1) > 50; // Simple example
            signals.InTrade["Price_Below_EMA20"] = _bot.Bars.ClosePrices.Last(1) < _emaFast.Result.Last(1);

            // 4. Raw Values (For math-based decisions like Position Sizing)
            signals.Values["ATR"] = _atr.Result.Last(1);
            signals.Values["RSI_Value"] = _rsi.Result.Last(1);

            return signals;
        }
    }
}