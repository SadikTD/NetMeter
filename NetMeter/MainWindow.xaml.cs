using System.ComponentModel;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;

namespace NetMeter;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        Nav.SetPageProviderService(new PageCache());
        Loaded += (_, _) => { if (Nav.SelectedItem == null) Nav.Navigate(typeof(Pages.OverviewPage)); };
    }

    public void Navigate(Type page) => Nav.Navigate(page);

    // Closing only hides the dashboard; the meter keeps running.
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!App.Exiting)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    /// <summary>One instance per page so live charts keep their state between visits.</summary>
    sealed class PageCache : INavigationViewPageProvider
    {
        readonly Dictionary<Type, object> pages = [];
        public object? GetPage(Type t) => pages.TryGetValue(t, out var p) ? p : pages[t] = Activator.CreateInstance(t)!;
    }
}
