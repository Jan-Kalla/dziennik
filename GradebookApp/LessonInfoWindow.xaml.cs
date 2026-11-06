using System.Windows;

namespace GradebookApp
{
    public partial class LessonInfoWindow : Window
    {
        private DailyRowWrapper _rowContext;

        public LessonInfoWindow(DailyRowWrapper rowContext, string dateDisplay)
        {
            InitializeComponent();
            _rowContext = rowContext;

            // Ustawienie nagłówka zgodnie z: -> "INFORMACJE" -> klasa, godz, itp.
            string classNameInfo = string.IsNullOrWhiteSpace(_rowContext.ClassName) ? "[Brak wybranej klasy]" : _rowContext.ClassName;
            InfoHeader.Text = $"INFORMACJE — Klasa: {classNameInfo} | Godz: {_rowContext.TimeSlot} | {dateDisplay}";

            // Wypełnienie wstępnymi danymi (w przyszłości dojdzie pobieranie z bazy Tematu i Uwag)
            MaterialComboBox.Text = _rowContext.Material;
            
            // TODO: Pobierz listę tematów i materiałów z "rozkładu materiału" do ItemsSource obu ComboBoxów
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // Zapis materiału z powrotem do kontekstu DataGrida.
            // Dzięki bindingowi INotifyPropertyChanged w klasie ScheduleRow/DailyRowWrapper wywoła to automatycznie zapis do bazy.
            _rowContext.Material = MaterialComboBox.Text;
            
            // TODO: Zapis Tematu (TopicComboBox.Text) i Uwag (NotesTextBox.Text) do bazy danych

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void PlanQuiz_Click(object sender, RoutedEventArgs e)
        {
            // Logika "Przejście do okna planowania dla danej klasy"
            if (string.IsNullOrWhiteSpace(_rowContext.ClassName))
            {
                MessageBox.Show("Najpierw przypisz klasę do tej lekcji (w tabeli lub oknie głównym), aby zaplanować dla niej kartkówkę.", "Brak klasy", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MessageBox.Show($"Tu w przyszłości otworzy się dedykowane okno planowania sprawdzianu/kartkówki dla klasy: {_rowContext.ClassName}.", "Zaplanuj kartkówkę", MessageBoxButton.OK, MessageBoxImage.Information);
            
            // Przykład integracji, jeśli będziesz chciał to spiąć z istniejącymi oknami:
            // var planDialog = new PlanWorkDialog(_rowContext.ClassName);
            // planDialog.Owner = this;
            // planDialog.ShowDialog();
        }
    }
}
