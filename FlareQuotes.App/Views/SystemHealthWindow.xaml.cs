using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using FlareQuotes.Core.Models;
using FlareQuotes.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using System.IO;

namespace FlareQuotes.App.Views;

public partial class SystemHealthWindow : Window
{
    public SystemHealthWindow(IReadOnlyList<SystemHealthItem> items)
    {
        InitializeComponent();
        DataContext = new SystemHealthWindowModel(items);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OpenLogsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var logger = App.Services.GetService<IAppLogger>();
            var path = logger?.LogFilePath;

            if (!string.IsNullOrWhiteSpace(path))
            {
                var dir = Path.GetDirectoryName(path);

                if (!string.IsNullOrWhiteSpace(dir))
                    Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
            }
        }
        catch
        {
            // Opening logs is helpful but never required.
        }
    }

    private sealed class SystemHealthWindowModel
    {
        public SystemHealthWindowModel(IReadOnlyList<SystemHealthItem> items)
        {
            Items = new ObservableCollection<SystemHealthItem>(items);
        }

        public ObservableCollection<SystemHealthItem> Items { get; }
    }
}