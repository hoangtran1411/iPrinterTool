using System.Windows;
using iPrinterTool.ViewModels;

namespace iPrinterTool;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var vm = new MainViewModel();
        DataContext = vm;
        Loaded += (_, _) => vm.RefreshCommand.Execute(null);
    }
}