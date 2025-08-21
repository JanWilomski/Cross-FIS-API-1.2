using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Cross_FIS_API_1._2.Models
{
    public class InstrumentDetails : INotifyPropertyChanged
    {
        private string _glidAndSymbol = string.Empty;
        private decimal _bidPrice;
        private long _bidSize;
        private decimal _askPrice;
        private long _askSize;
        private decimal _lastPrice;
        private long _lastSize;
        private string _lastTradeTime = string.Empty;
        private decimal _percentageVariation;
        private long _volume;
        private decimal _openingPrice;
        private decimal _highPrice;
        private decimal _lowPrice;
        private string _suspensionIndicator = string.Empty;
        private string _variationSign = string.Empty;
        private decimal _closingPrice;
        private string _tradingPhase = string.Empty;
        private string _isin = string.Empty;

        public string GlidAndSymbol { get => _glidAndSymbol; set => SetProperty(ref _glidAndSymbol, value); }
        public decimal BidPrice { get => _bidPrice; set => SetProperty(ref _bidPrice, value); }
        public long BidQuantity { get => _bidSize; set => SetProperty(ref _bidSize, value); }
        public decimal AskPrice { get => _askPrice; set => SetProperty(ref _askPrice, value); }
        public long AskQuantity { get => _askSize; set => SetProperty(ref _askSize, value); }
        public decimal LastPrice { get => _lastPrice; set => SetProperty(ref _lastPrice, value); }
        public long LastQuantity { get => _lastSize; set => SetProperty(ref _lastSize, value); }
        public string LastTradeTime { get => _lastTradeTime; set => SetProperty(ref _lastTradeTime, value); }
        public decimal PercentageVariation { get => _percentageVariation; set => SetProperty(ref _percentageVariation, value); }
        public long Volume { get => _volume; set => SetProperty(ref _volume, value); }
        public decimal OpenPrice { get => _openingPrice; set => SetProperty(ref _openingPrice, value); }
        public decimal HighPrice { get => _highPrice; set => SetProperty(ref _highPrice, value); }
        public decimal LowPrice { get => _lowPrice; set => SetProperty(ref _lowPrice, value); }
        public string SuspensionIndicator { get => _suspensionIndicator; set => SetProperty(ref _suspensionIndicator, value); }
        public string VariationSign { get => _variationSign; set => SetProperty(ref _variationSign, value); }
        public decimal ClosePrice { get => _closingPrice; set => SetProperty(ref _closingPrice, value); }
        public string TradingPhase { get => _tradingPhase; set => SetProperty(ref _tradingPhase, value); }
        public string ISIN { get => _isin; set => SetProperty(ref _isin, value); }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
