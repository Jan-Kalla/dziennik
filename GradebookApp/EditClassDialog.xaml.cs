using System.Windows;

namespace GradebookApp
{
    public partial class EditClassDialog : Window
    {
        // Właściwość, z której okno główne pobierze nową nazwę
        public string NewName { get; private set; } = string.Empty;

        public EditClassDialog(string currentName)
        {
            InitializeComponent();
            
            // Wypełnienie pola tekstowego obecną nazwą
            NameTextBox.Text = currentName;
            
            // Automatyczne zaznaczenie całego tekstu i ustawienie kursora
            NameTextBox.SelectAll();
            NameTextBox.Focus();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // Oczyszczenie tekstu i przypisanie go do publicznej właściwości
            NewName = NameTextBox.Text.Trim();
            
            // Ustawienie DialogResult na true zamyka okno z informacją o sukcesie
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            // Zamyka okno bez zapisu
            DialogResult = false;
        }
    }
}