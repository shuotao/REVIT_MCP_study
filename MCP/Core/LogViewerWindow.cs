using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace RevitMCP.Core
{
    public class LogViewerWindow : Window
    {
        private static LogViewerWindow _instance;
        private readonly TextBox _logTextBox;

        public LogViewerWindow()
        {
            Title = "RevitMCP Real-time Log Viewer";
            Width = 600;
            Height = 400;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true; // Keep it on top of Revit

            // Main Layout
            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Text Box for logs
            _logTextBox = new TextBox
            {
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Background = Brushes.Black,
                Foreground = Brushes.LightGray,
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(5)
            };
            Grid.SetRow(_logTextBox, 0);
            grid.Children.Add(_logTextBox);

            // Clear Button
            Button clearButton = new Button
            {
                Content = "Clear Logs",
                Height = 30,
                Margin = new Thickness(5)
            };
            clearButton.Click += (s, e) => _logTextBox.Clear();
            Grid.SetRow(clearButton, 1);
            grid.Children.Add(clearButton);

            Content = grid;

            // Subscribe to Logger
            Logger.OnLogMessage += AppendLog;

            // Unsubscribe when closed
            Closed += (s, e) => 
            {
                Logger.OnLogMessage -= AppendLog;
                _instance = null;
            };
        }

        public static void ShowWindow()
        {
            if (_instance == null)
            {
                _instance = new LogViewerWindow();
                _instance.Show();
            }
            else
            {
                _instance.Activate();
                if (_instance.WindowState == WindowState.Minimized)
                    _instance.WindowState = WindowState.Normal;
            }
        }

        private void AppendLog(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<string>(AppendLog), message);
                return;
            }

            _logTextBox.AppendText(message + Environment.NewLine);
            _logTextBox.ScrollToEnd();

            // Limit buffer size
            if (_logTextBox.Text.Length > 50000)
            {
                _logTextBox.Text = _logTextBox.Text.Substring(10000);
            }
        }
    }
}
