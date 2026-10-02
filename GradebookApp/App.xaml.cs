using System.Windows;
using System.Windows.Threading;

namespace GradebookApp
{
    public partial class App : Application
    {
        public App()
        {
            // Globalny przechwytywacz błędów dla wątku interfejsu (UI)
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"Wystąpił nieoczekiwany błąd:\n\n{e.Exception.Message}\n\nLokalizacja:\n{e.Exception.StackTrace}", 
                            "Błąd aplikacji", 
                            MessageBoxButton.OK, 
                            MessageBoxImage.Error);
            
            // Handled = true zapobiega twardemu zamknięciu aplikacji do pulpitu
            e.Handled = true; 
        }
    }
}