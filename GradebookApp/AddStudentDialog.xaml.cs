using System.Collections.Generic;
using System.Windows;

namespace GradebookApp
{
    public partial class AddStudentDialog : Window
    {
        public string FirstName { get; private set; } = string.Empty;
        public string LastName { get; private set; } = string.Empty;
        
        // Zmienna przechowująca wpisany numerek (może być null, jeśli pole zostało puste)
        public int? JournalNumber { get; private set; } = null;
        
        private List<int> _existingNumbers;

        // Konstruktor przyjmuje listę zajętych numerków
        public AddStudentDialog(List<int> existingNumbers)
        {
            InitializeComponent();
            _existingNumbers = existingNumbers;
            FirstNameTextBox.Focus();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string numText = JournalNumberTextBox.Text.Trim();
            
            // Walidacja numerku, jeśli cokolwiek wpisano
            if (!string.IsNullOrEmpty(numText))
            {
                if (int.TryParse(numText, out int parsedNum))
                {
                    if (_existingNumbers.Contains(parsedNum))
                    {
                        MessageBox.Show($"Numerek {parsedNum} jest już przypisany do innego ucznia w tej klasie.", "Konflikt", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return; // Przerywamy zapis, okno zostaje otwarte do poprawy
                    }
                    JournalNumber = parsedNum;
                }
                else
                {
                    MessageBox.Show("Numerek musi być liczbą całkowitą.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            FirstName = FirstNameTextBox.Text.Trim();
            LastName = LastNameTextBox.Text.Trim();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}