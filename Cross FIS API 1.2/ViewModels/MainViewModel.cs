using Cross_FIS_API_1._2.Commands;
using Cross_FIS_API_1._2.Models;
using Cross_FIS_API_1._2.Views;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Cross_FIS_API_1._2.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        // SLE Connection Properties
        private string _sleIpAddress = "172.31.136.4";
        private int _slePort = 19593;
        private string _sleUser = "103";
        private string _slePassword = "glglgl";
        private string _sleNode = "24300";
        private string _sleSubnode = "14300";
        private string _sleStatusMessage = "Disconnected";
        private bool _isSleConnecting;
        private readonly FisConnectionService _fisConnectionService;

        // MDS Connection Properties
        private string _mdsIpAddress = "172.31.136.4";
        private int _mdsPort = 25003;
        private string _mdsUser = "150";
        private string _mdsPassword = "dupablada";
        private string _mdsNode = "5000";
        private string _mdsSubnode = "4000";
        private string _mdsStatusMessage = "Disconnected";
        private bool _isMdsConnecting;
        private readonly MdsConnectionService _mdsConnectionService;

        // Instrument Properties
        public ObservableCollection<Instrument> Instruments { get; } = new ObservableCollection<Instrument>();
        private bool _isFetchingInstruments;
        private Instrument? _selectedInstrument;

        public MainViewModel()
        {
            _fisConnectionService = new FisConnectionService();
            
            _mdsConnectionService = new MdsConnectionService();
            _mdsConnectionService.InstrumentsReceived += OnInstrumentsReceived;

            ConnectSleCommand = new AsyncRelayCommand(ConnectSle, () => !_fisConnectionService.IsConnected && !IsSleConnecting);
            DisconnectSleCommand = new RelayCommand(DisconnectSle, () => _fisConnectionService.IsConnected);

            ConnectMdsCommand = new AsyncRelayCommand(ConnectMds, () => !_mdsConnectionService.IsConnected && !IsMdsConnecting);
            DisconnectMdsCommand = new RelayCommand(DisconnectMds, () => _mdsConnectionService.IsConnected);
            FetchInstrumentsCommand = new AsyncRelayCommand(FetchInstruments, () => _mdsConnectionService.IsConnected && !IsFetchingInstruments);
            OpenInstrumentDetailsCommand = new RelayCommand(OpenInstrumentDetails, () => SelectedInstrument != null && _mdsConnectionService.IsConnected);
        }

        private void OnInstrumentsReceived(List<Instrument> instruments)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var instrument in instruments)
                {
                    if (!Instruments.Any(i => i.GlidAndSymbol == instrument.GlidAndSymbol))
                    {
                        Instruments.Add(instrument);
                    }
                }
            });
        }

        #region SLE Properties and Commands
        public string SleIpAddress { get => _sleIpAddress; set => SetProperty(ref _sleIpAddress, value); }
        public int SlePort { get => _slePort; set => SetProperty(ref _slePort, value); }
        public string SleUser { get => _sleUser; set => SetProperty(ref _sleUser, value); }
        public string SlePassword { get => _slePassword; set => SetProperty(ref _slePassword, value); }
        public string SleNode { get => _sleNode; set => SetProperty(ref _sleNode, value); }
        public string SleSubnode { get => _sleSubnode; set => SetProperty(ref _sleSubnode, value); }
        public string SleStatusMessage { get => _sleStatusMessage; set => SetProperty(ref _sleStatusMessage, value); }
        public bool IsSleConnecting { get => _isSleConnecting; set => SetProperty(ref _isSleConnecting, value, RaiseSleCommandsCanExecuteChanged); }
        public ICommand ConnectSleCommand { get; }
        public ICommand DisconnectSleCommand { get; }

        private async Task ConnectSle()
        {
            IsSleConnecting = true;
            SleStatusMessage = "Connecting...";
            bool success = await _fisConnectionService.ConnectAndLoginAsync(SleIpAddress, SlePort, SleUser, SlePassword, SleNode, SleSubnode);
            SleStatusMessage = success ? "Connected" : "Connection Failed";
            IsSleConnecting = false;
        }

        private void DisconnectSle()
        {
            _fisConnectionService.Disconnect();
            SleStatusMessage = "Disconnected";
            RaiseSleCommandsCanExecuteChanged();
        }

        private void RaiseSleCommandsCanExecuteChanged() => Application.Current.Dispatcher.Invoke(() =>
        {
            ((AsyncRelayCommand)ConnectSleCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DisconnectSleCommand).RaiseCanExecuteChanged();
        });
        #endregion

        #region MDS Properties and Commands
        public string MdsIpAddress { get => _mdsIpAddress; set => SetProperty(ref _mdsIpAddress, value); }
        public int MdsPort { get => _mdsPort; set => SetProperty(ref _mdsPort, value); }
        public string MdsUser { get => _mdsUser; set => SetProperty(ref _mdsUser, value); }
        public string MdsPassword { get => _mdsPassword; set => SetProperty(ref _mdsPassword, value); }
        public string MdsNode { get => _mdsNode; set => SetProperty(ref _mdsNode, value); }
        public string MdsSubnode { get => _mdsSubnode; set => SetProperty(ref _mdsSubnode, value); }
        public string MdsStatusMessage { get => _mdsStatusMessage; set => SetProperty(ref _mdsStatusMessage, value); }
        public bool IsMdsConnecting { get => _isMdsConnecting; set => SetProperty(ref _isMdsConnecting, value, RaiseMdsCommandsCanExecuteChanged); }
        public ICommand ConnectMdsCommand { get; }
        public ICommand DisconnectMdsCommand { get; }
        public ICommand FetchInstrumentsCommand { get; }
        public bool IsFetchingInstruments { get => _isFetchingInstruments; set => SetProperty(ref _isFetchingInstruments, value, () => ((AsyncRelayCommand)FetchInstrumentsCommand).RaiseCanExecuteChanged()); }

        private async Task ConnectMds()
        {
            IsMdsConnecting = true;
            MdsStatusMessage = "Connecting...";
            bool success = await _mdsConnectionService.ConnectAndLoginAsync(MdsIpAddress, MdsPort, MdsUser, MdsPassword, MdsNode, MdsSubnode);
            MdsStatusMessage = success ? "Connected" : "Connection Failed";
            IsMdsConnecting = false;
        }

        private void DisconnectMds()
        {
            _mdsConnectionService.Disconnect();
            MdsStatusMessage = "Disconnected";
            Instruments.Clear();
            RaiseMdsCommandsCanExecuteChanged();
        }

        private async Task FetchInstruments()
        {
            IsFetchingInstruments = true;
            Instruments.Clear();
            await _mdsConnectionService.RequestAllInstrumentsAsync();
            await Task.Delay(5000); // Give time for responses to arrive
            IsFetchingInstruments = false;
        }

        private void RaiseMdsCommandsCanExecuteChanged() => Application.Current.Dispatcher.Invoke(() =>
        {
            ((AsyncRelayCommand)ConnectMdsCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DisconnectMdsCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)FetchInstrumentsCommand).RaiseCanExecuteChanged();
        });
        #endregion

        #region Details Window Logic
        public Instrument? SelectedInstrument
        {
            get => _selectedInstrument;
            set => SetProperty(ref _selectedInstrument, value, () => ((RelayCommand)OpenInstrumentDetailsCommand).RaiseCanExecuteChanged());
        }
        public ICommand OpenInstrumentDetailsCommand { get; }

        private void OpenInstrumentDetails()
        {
            if (SelectedInstrument == null) return;

            Debug.WriteLine($"SelectedInstrument Symbol (from MainViewModel): {SelectedInstrument.Symbol ?? "NULL"}");
            Debug.WriteLine($"SelectedInstrument GLID (from MainViewModel): {SelectedInstrument.Glid ?? "NULL"}");

            var detailsVM = new InstrumentDetailsViewModel(SelectedInstrument, _mdsConnectionService, _fisConnectionService, SleUser);
            var detailsWindow = new InstrumentDetailsWindow(detailsVM)
            {
                Owner = Application.Current.MainWindow
            };
            detailsWindow.Show();
        }
        #endregion

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, Action? onChanged = null, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            onChanged?.Invoke();
            return true;
        }
    }
}
