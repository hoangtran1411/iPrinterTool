using System.Collections.Specialized;
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

        Loaded += (_, _) =>
        {
            vm.RefreshCommand.Execute(null);
            vm.JobLog.Start();
        };

        ((INotifyCollectionChanged)vm.Log.Entries).CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && vm.Log.AutoScroll && LogListBox.Items.Count > 0)
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (LogListBox.Items.Count > 0)
                    {
                        LogListBox.ScrollIntoView(LogListBox.Items[^1]);
                    }
                });
            }
        };
    }
}
