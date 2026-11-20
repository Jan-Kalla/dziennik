using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

namespace GradebookApp
{
    public partial class PdfViewerWindow : Window
    {
        private string _filePath;
        private readonly string _displayName;
        private bool _isFullscreen = false;
        private WindowState _previousWindowState = WindowState.Normal;

        public PdfViewerWindow(string filePath, string? displayName = null)
        {
            InitializeComponent();
            _filePath = filePath;
            _displayName = !string.IsNullOrWhiteSpace(displayName) ? displayName : Path.GetFileName(filePath);

            FileNameTextBlock.Text = _displayName;
            FilePathTextBlock.Text = _filePath;
            Title = $"Podgląd PDF — {_displayName}";

            Loaded += async (s, e) => await InitializePdfViewerAsync();
        }

        private async Task InitializePdfViewerAsync()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    string? resolved = GradebookApp.Services.PdfFileResolver.ResolvePdfPath(_filePath, _displayName);
                    if (!string.IsNullOrEmpty(resolved) && File.Exists(resolved))
                    {
                        _filePath = resolved;
                        FilePathTextBlock.Text = _filePath;
                    }
                    else
                    {
                        ShowError("Plik nie został znaleziony na dysku komputera.");
                        return;
                    }
                }

                LoadingProgressBar.Visibility = Visibility.Visible;
                ErrorPanel.Visibility = Visibility.Collapsed;

                // Dedykowany, izolowany folder na pamięć podręczną WebView2 w LocalAppData
                string cacheFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GradebookApp", "WebView2PdfCache");
                Directory.CreateDirectory(cacheFolder);

                var environment = await CoreWebView2Environment.CreateAsync(null, cacheFolder);
                await PdfWebView.EnsureCoreWebView2Async(environment);

                PdfWebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(24, 24, 24);
                
                if (PdfWebView.CoreWebView2 != null)
                {
                    PdfWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    PdfWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    PdfWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;

                    // Otwieramy plik PDF w wewnętrznym silniku przeglądarki
                    var fileUri = new Uri(_filePath).AbsoluteUri;
                    PdfWebView.CoreWebView2.Navigate(fileUri);
                }
            }
            catch (Exception ex)
            {
                ShowError($"Wystąpił błąd podczas inicjalizacji przeglądarki PDF:\n{ex.Message}");
            }
        }

        private void PdfWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            LoadingProgressBar.Visibility = Visibility.Collapsed;
            if (!e.IsSuccess)
            {
                ShowError($"Nie udało się załadować dokumentu (kod błędu: {e.WebErrorStatus}).");
            }
        }

        private void ShowError(string message)
        {
            LoadingProgressBar.Visibility = Visibility.Collapsed;
            ErrorMessageTextBlock.Text = message;
            ErrorPanel.Visibility = Visibility.Visible;
        }

        private async void Retry_Click(object sender, RoutedEventArgs e)
        {
            await InitializePdfViewerAsync();
        }

        private async void Print_Click(object sender, RoutedEventArgs e)
        {
            if (PdfWebView.CoreWebView2 != null)
            {
                await PdfWebView.CoreWebView2.ExecuteScriptAsync("window.print();");
            }
        }

        private void ToggleFullscreen_Click(object sender, RoutedEventArgs e)
        {
            ToggleFullscreen();
        }

        private void ToggleFullscreen()
        {
            if (!_isFullscreen)
            {
                _previousWindowState = WindowState;
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
                FullscreenBtn.Content = "⛶ Okno";
                _isFullscreen = true;
            }
            else
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = _previousWindowState;
                FullscreenBtn.Content = "⛶ Pełny ekran";
                _isFullscreen = false;
            }
        }

        private void OpenExternal_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    Process.Start(new ProcessStartInfo(_filePath) { UseShellExecute = true });
                }
                else
                {
                    MessageBox.Show("Plik nie istnieje na dysku.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Nie można otworzyć pliku zewnętrznie: {ex.Message}", "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F11)
            {
                ToggleFullscreen();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (_isFullscreen)
                {
                    ToggleFullscreen();
                    e.Handled = true;
                }
                else
                {
                    Close();
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.P && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                Print_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.W && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                Close();
                e.Handled = true;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                PdfWebView?.Dispose();
            }
            catch { }
            base.OnClosed(e);
        }
    }
}
