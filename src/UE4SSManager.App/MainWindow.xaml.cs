using System.Collections.Specialized;
using System.IO;
using System.Windows;
using UE4SSManager.App.Services;
using UE4SSManager.App.ViewModels;

namespace UE4SSManager.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(new Dialogs(this));
        DataContext = _vm;
        SourceInitialized += (_, _) => Native.UseDarkTitleBar(this);
        Loaded += async (_, _) => await _vm.InitializeAsync();
        // Keep the newest log line in view.
        _vm.Log.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && _vm.Log.Count > 0) LogList.ScrollIntoView(_vm.Log[^1]);
        };
    }

    private void OnModsDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && _vm.HasGame ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnModsDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        var usable = paths.Where(p => Directory.Exists(p) || p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
        if (usable.Count == 0)
        {
            new Dialogs(this).Error("Drop .zip files or mod folders. Other archive types (.7z, .rar) need to be extracted first.");
            return;
        }
        _vm.InstallMods(usable);
    }
}
