using System.Collections.Generic;

namespace cTraderV1.Models
{
    public class SignalCollection
    {
        // Category 1: Potential triggers (e.g., RSI Cross)
        public Dictionary<string, bool> PreTrade { get; set; } = new Dictionary<string, bool>();

        // Category 2: Confirmations (e.g., Candle closed above EMA)
        public Dictionary<string, bool> Confirmation { get; set; } = new Dictionary<string, bool>();

        // Category 3: Exit/Safety signals (e.g., Bearish Engulfing)
        public Dictionary<string, bool> InTrade { get; set; } = new Dictionary<string, bool>();

        // Raw values for strategies that need specific numbers (e.g., ATR value)
        public Dictionary<string, double> Values { get; set; } = new Dictionary<string, double>();
    }
}