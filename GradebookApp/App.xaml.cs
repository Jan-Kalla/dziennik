using System;
using System.Windows;
using Microsoft.EntityFrameworkCore;

namespace GradebookApp
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                using (var dbContext = new AppDbContext())
                {
                    dbContext.Database.Migrate();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Błąd podczas aktualizacji bazy danych (Migrate):\n\n{ex.Message}\n\nDetale: {ex.InnerException?.Message}", 
                                "Krytyczny błąd startu", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
        }
    }
}