using System.Windows;
using System.Windows.Threading;

namespace GradebookApp
{
    public partial class App : Application
    {
        public App()
        {
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // Wyciągamy szczegóły błędu (InnerException), które WPF chowa wewnątrz wyjątku XamlParseException
            string errorMessage = e.Exception.Message;
            if (e.Exception.InnerException != null)
            {
                errorMessage += $"\n\nSzczegóły błędu (InnerException):\n{e.Exception.InnerException.Message}";
            }

            MessageBox.Show($"Wystąpił nieoczekiwany błąd:\n\n{errorMessage}\n\nLokalizacja:\n{e.Exception.StackTrace}", 
                            "Błąd aplikacji", 
                            MessageBoxButton.OK, 
                            MessageBoxImage.Error);
            
            e.Handled = true; 
        }
    }
}