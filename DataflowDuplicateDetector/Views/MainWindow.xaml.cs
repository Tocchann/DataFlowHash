using System.Windows;

namespace DataflowDuplicateDetector.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new ViewModels.MainViewModel(this);
        Closed += (_, _) => ((ViewModels.MainViewModel)DataContext).CancelScan();
    }
}
