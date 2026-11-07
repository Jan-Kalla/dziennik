using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using GradebookApp.Services;

namespace GradebookApp
{
    public partial class LessonInfoWindow : Window
    {
        private DailyRowWrapper _rowContext;
        private DateTime _currentDate; // Trzymamy datę dla kalendarza
        private readonly GradebookDataService _dataService;

        public LessonInfoWindow(DailyRowWrapper rowContext, string dateDisplay, DateTime currentDate)
        {
            InitializeComponent();
            _rowContext = rowContext;
            _currentDate = currentDate;
            _dataService = new GradebookDataService();

            string classNameInfo = string.IsNullOrWhiteSpace(_rowContext.ClassName) ? "[Brak wybranej klasy]" : _rowContext.ClassName;
            InfoHeader.Text = $"INFORMACJE — Klasa: {classNameInfo} | Godz: {_rowContext.TimeSlot} | {dateDisplay}";

            MaterialComboBox.Text = _rowContext.Material;
            
            // TODO: Pobierz listę tematów i materiałów z "rozkładu materiału" do ItemsSource obu ComboBoxów
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _rowContext.Material = MaterialComboBox.Text;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private SchoolClass? TryGetContextClass()
        {
            if (string.IsNullOrWhiteSpace(_rowContext.ClassName))
            {
                MessageBox.Show("Najpierw przypisz klasę do tej lekcji (w tabeli), aby móc zaplanować z nią działanie.", "Brak klasy", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            var allClasses = _dataService.GetAllClasses();
            var targetClass = allClasses.FirstOrDefault(c => c.Name.Equals(_rowContext.ClassName.Trim(), StringComparison.OrdinalIgnoreCase));

            if (targetClass == null)
            {
                MessageBox.Show($"Nie odnaleziono w bazie klasy o nazwie '{_rowContext.ClassName}'. Upewnij się, że wpisana nazwa jest poprawna, lub najpierw utwórz taką klasę.", "Nieznana klasa", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            return targetClass;
        }

        private void PlanWork_Click(object sender, RoutedEventArgs e)
        {
            var targetClass = TryGetContextClass();
            if (targetClass == null) return;

            var planDialog = new WorkEditorDialog(null, targetClass.Id, null, false)
            {
                Owner = Window.GetWindow(this)
            };
            planDialog.ShowDialog();
        }

        private void PlanRetake_Click(object sender, RoutedEventArgs e)
        {
            var targetClass = TryGetContextClass();
            if (targetClass == null) return;

            var retakeDialog = new PlanRetakeDialog(targetClass.Id)
            {
                Owner = Window.GetWindow(this)
            };

            // Uzupełnienie daty (z widoku dziennego)
            retakeDialog.DatePicker.SelectedDate = _currentDate;

            // Wyciąganie pierwszej prawidłowej godziny z paska (np. wyciągnie 09:30 z przedziału "09:30 - 10:15")
            string timeStr = _rowContext.TimeSlot;
            if (!string.IsNullOrWhiteSpace(timeStr))
            {
                var match = Regex.Match(timeStr, @"\d{1,2}:\d{2}");
                if (match.Success)
                {
                    retakeDialog.TimeTextBox.Text = match.Value;
                }
            }

            retakeDialog.ShowDialog();
        }

        protected override void OnClosed(EventArgs e)
        {
            _dataService.Dispose();
            base.OnClosed(e);
        }
    }
}