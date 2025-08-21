using Cross_FIS_API_1._2.ViewModels;
using System.Windows;

namespace Cross_FIS_API_1._2.Views
{
    public partial class InstrumentDetailsWindow : Window
    {
        public InstrumentDetailsWindow(InstrumentDetailsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (DataContext is InstrumentDetailsViewModel vm)
            {
                vm.Cleanup();
            }
        }
    }
}
