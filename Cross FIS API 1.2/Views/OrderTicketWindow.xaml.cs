using Cross_FIS_API_1._2.ViewModels;
using System.Windows;

namespace Cross_FIS_API_1._2.Views
{
    public partial class OrderTicketWindow : Window
    {
        private readonly OrderTicketViewModel _viewModel;

        public OrderTicketWindow(OrderTicketViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Ensure we clean up resources (like unsubscribing from market data) when the window is closed.
            _viewModel.Cleanup();
        }
    }
}
