using Cross_FIS_API_1._2.Commands;
using Cross_FIS_API_1._2.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Cross_FIS_API_1._2.ViewModels
{
    public class InstrumentDetailsViewModel : INotifyPropertyChanged
    {
        private readonly MdsConnectionService _mdsService;
        private readonly FisConnectionService _fisService;
        private readonly string _user;
        public Instrument Instrument { get; }
        public InstrumentDetails Details { get; } = new InstrumentDetails();

        // Order Properties
        private int _orderQuantity;
        private decimal _orderPrice;
        private char _selectedSide = 'B';
        private string _selectedValidity = "Day";
        private string _selectedClientCodeType = "Client";
        public ObservableCollection<char> Sides { get; } = new ObservableCollection<char> { 'B', 'S' };
        public ObservableCollection<string> Validities { get; } = new ObservableCollection<string> { "Day", "GTC", "IOC", "FOK" };
        public ObservableCollection<string> ClientCodeTypes { get; } = new ObservableCollection<string> { "Client", "Principal" };
        public ICommand PlaceOrderCommand { get; }
        public ObservableCollection<OrderUpdate> OrderUpdates { get; } = new ObservableCollection<OrderUpdate>();

        public InstrumentDetailsViewModel(Instrument instrument, MdsConnectionService mdsService, FisConnectionService fisService, string user)
        {
            Debug.WriteLine($"InstrumentDetailsViewModel Constructor: user parameter = '{user ?? "NULL"}'");
            Instrument = instrument;
            _mdsService = mdsService;
            _fisService = fisService;
            _user = user;

            _mdsService.InstrumentDetailsReceived += OnInstrumentDetailsReceived;
            _fisService.OrderStatusUpdated += OnOrderStatusUpdated;
            _fisService.RawMessageReceived += OnRawMessageReceived; // Keep for debugging unhandled messages
            _mdsService.RequestInstrumentDetails(instrument.GlidAndSymbol);

            PlaceOrderCommand = new AsyncRelayCommand(PlaceOrder, () => _fisService.IsConnected);
        }

        private void OnInstrumentDetailsReceived(InstrumentDetails details)
        {
            if (details.GlidAndSymbol == Instrument.GlidAndSymbol)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Details.BidPrice = details.BidPrice;
                    Details.BidQuantity = details.BidQuantity;
                    Details.AskPrice = details.AskPrice;
                    Details.AskQuantity = details.AskQuantity;
                    Details.LastPrice = details.LastPrice;
                    Details.LastQuantity = details.LastQuantity;
                    Details.LastTradeTime = details.LastTradeTime;
                    Details.PercentageVariation = details.PercentageVariation;
                    Details.Volume = details.Volume;
                    Details.OpenPrice = details.OpenPrice;
                    Details.HighPrice = details.HighPrice;
                    Details.LowPrice = details.LowPrice;
                    Details.SuspensionIndicator = details.SuspensionIndicator;
                    Details.VariationSign = details.VariationSign;
                    Details.ClosePrice = details.ClosePrice;
                    Details.TradingPhase = details.TradingPhase;
                    Details.ISIN = details.ISIN;
                });
            }
        }

        private void OnOrderStatusUpdated(OrderUpdate orderUpdate)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Check if an update for this internal reference already exists
                var existingUpdate = OrderUpdates.FirstOrDefault(ou => ou.InternalReference == orderUpdate.InternalReference);
                if (existingUpdate != null)
                {
                    // Update existing entry
                    existingUpdate.OrderStatus = orderUpdate.OrderStatus;
                    existingUpdate.ExchangeOrderNumber = orderUpdate.ExchangeOrderNumber;
                    existingUpdate.CumulatedQuantity = orderUpdate.CumulatedQuantity;
                    existingUpdate.RemainingQuantity = orderUpdate.RemainingQuantity;
                    existingUpdate.AveragePrice = orderUpdate.AveragePrice;
                    existingUpdate.RejectReason = orderUpdate.RejectReason;
                    // ... update other relevant properties
                }
                else
                {
                    // Add new entry
                    OrderUpdates.Add(orderUpdate);
                }
            });
        }

        private void OnRawMessageReceived(string message)
        {
            // For debugging purposes, display raw unhandled messages
            Debug.WriteLine($"Raw SLE Message: {message}");
        }

        public int OrderQuantity { get => _orderQuantity; set => SetProperty(ref _orderQuantity, value); }
        public decimal OrderPrice { get => _orderPrice; set => SetProperty(ref _orderPrice, value); }
        public char SelectedSide { get => _selectedSide; set => SetProperty(ref _selectedSide, value); }
        public string SelectedValidity { get => _selectedValidity; set => SetProperty(ref _selectedValidity, value); }
        public string SelectedClientCodeType { get => _selectedClientCodeType; set => SetProperty(ref _selectedClientCodeType, value); }

        private async Task PlaceOrder()
        {
            Debug.WriteLine($"Instrument in ViewModel: {Instrument?.Symbol ?? "NULL"}");

            // Add null check for Instrument here
            if (Instrument == null)
            {
                MessageBox.Show("Cannot place order: Instrument is not selected or loaded.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var order = new Order
            {
                Instrument = Instrument,
                Quantity = OrderQuantity,
                Price = OrderPrice,
                Side = SelectedSide,
                Type = 'L', // For now, only Limit orders
                Validity = SelectedValidity,
                ClientCodeType = SelectedClientCodeType
            };

            await _fisService.PlaceOrder(order, _user);
            Debug.WriteLine($"InstrumentDetailsViewModel.PlaceOrder: Wartość _user tuż przed wywołaniem FisConnectionService.PlaceOrder: '{_user ?? "NULL"}'");
        }

        public void Cleanup()
        {
            _mdsService.InstrumentDetailsReceived -= OnInstrumentDetailsReceived;
            _fisService.OrderStatusUpdated -= OnOrderStatusUpdated;
            _fisService.RawMessageReceived -= OnRawMessageReceived;
        }

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
    }
}
