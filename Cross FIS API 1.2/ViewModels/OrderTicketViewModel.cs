using Cross_FIS_API_1._2.Commands;
using Cross_FIS_API_1._2.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace Cross_FIS_API_1._2.ViewModels
{
    public class OrderTicketViewModel : INotifyPropertyChanged
    {
        private readonly FisConnectionService _fisService;
        private readonly MdsConnectionService _mdsService;

        public Instrument SelectedInstrument { get; }

        // Market Data Properties
        private decimal _bidPrice;
        private long _bidSize;
        private decimal _askPrice;
        private long _askSize;
        private decimal _lastPrice;
        private long _volume;

        // Order Properties
        private long _orderQuantity;
        private decimal _orderPrice;

        public OrderTicketViewModel(Instrument selectedInstrument, FisConnectionService fisService, MdsConnectionService mdsService)
        {
            SelectedInstrument = selectedInstrument;
            _fisService = fisService;
            _mdsService = mdsService;

            // Subscribe to market data updates for this instrument
            _mdsService.MarketDataUpdate += OnMarketDataUpdate;
            _mdsService.SubscribeToInstrumentAsync(SelectedInstrument.Glid);

            BuyCommand = new RelayCommand(PlaceBuyOrder, CanPlaceOrder);
            SellCommand = new RelayCommand(PlaceSellOrder, CanPlaceOrder);
        }

        private void OnMarketDataUpdate(MarketData data)
        {
            // Ensure the update is for our instrument and update on the UI thread
            if (data.Glid == SelectedInstrument.Glid)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    BidPrice = data.BidPrice;
                    BidSize = data.BidSize;
                    AskPrice = data.AskPrice;
                    AskSize = data.AskSize;
                    LastPrice = data.LastPrice;
                    Volume = data.Volume;
                });
            }
        }

        #region Market Data Bindings
        public decimal BidPrice { get => _bidPrice; set => SetProperty(ref _bidPrice, value); }
        public long BidSize { get => _bidSize; set => SetProperty(ref _bidSize, value); }
        public decimal AskPrice { get => _askPrice; set => SetProperty(ref _askPrice, value); }
        public long AskSize { get => _askSize; set => SetProperty(ref _askSize, value); }
        public decimal LastPrice { get => _lastPrice; set => SetProperty(ref _lastPrice, value); }
        public long Volume { get => _volume; set => SetProperty(ref _volume, value); }
        #endregion

        #region Order Bindings
        public long OrderQuantity { get => _orderQuantity; set => SetProperty(ref _orderQuantity, value); }
        public decimal OrderPrice { get => _orderPrice; set => SetProperty(ref _orderPrice, value); }
        public ICommand BuyCommand { get; }
        public ICommand SellCommand { get; }
        #endregion

        private async void PlaceBuyOrder()
        {
            var order = new OrderParameters
            {
                Glid = SelectedInstrument.Glid,
                Quantity = this.OrderQuantity,
                Price = this.OrderPrice,
                Side = OrderSide.Buy
            };
            await _fisService.SendNewOrderAsync(order);
            // TODO: Add user feedback (e.g., show confirmation)
        }

        private async void PlaceSellOrder()
        {
            var order = new OrderParameters
            {
                Glid = SelectedInstrument.Glid,
                Quantity = this.OrderQuantity,
                Price = this.OrderPrice,
                Side = OrderSide.Sell
            };
            await _fisService.SendNewOrderAsync(order);
            // TODO: Add user feedback (e.g., show confirmation)
        }

        private bool CanPlaceOrder()
        {
            return _fisService.IsConnected && OrderQuantity > 0 && OrderPrice > 0;
        }

        public void Cleanup()
        {
            // Unsubscribe from market data updates to prevent memory leaks
            _mdsService.MarketDataUpdate -= OnMarketDataUpdate;
            _mdsService.UnsubscribeFromInstrumentAsync(SelectedInstrument.Glid);
        }

        #region INotifyPropertyChanged Implementation
        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
        #endregion
    }
}
