using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GradebookApp.Services;

namespace GradebookApp
{
    public partial class LessonInfoWindow : Window
    {
        private DailyRowWrapper _rowContext;
        private DateTime _currentDate; 
        private readonly GradebookDataService _dataService;
        private System.Collections.Generic.List<string> _allTopics = new System.Collections.Generic.List<string>();

        public LessonInfoWindow(DailyRowWrapper rowContext, string dateDisplay, DateTime currentDate)
        {
            InitializeComponent();
            _rowContext = rowContext;
            _currentDate = currentDate;
            _dataService = new GradebookDataService();

            string classNameInfo = string.IsNullOrWhiteSpace(_rowContext.ClassName) ? "[Brak wybranej klasy]" : _rowContext.ClassName;
            InfoHeader.Text = $"INFORMACJE — Klasa: {classNameInfo} | Godz: {_rowContext.TimeSlot} | {dateDisplay}";

            // Ładowanie tematów z bazy do listy rozwijanej
            _allTopics = _dataService.GetAllCurriculumNodes().Where(n => n.Level == 2).Select(n => n.Name).ToList();
            TopicComboBox.ItemsSource = _allTopics;

            // Sprytne rozdzielanie zapisanej wcześniej frazy (np. "Temat 1: Zadania testowe") na dwa pola
            string savedMaterial = _rowContext.Material ?? "";
            if (savedMaterial.Contains(": "))
            {
                var parts = savedMaterial.Split(new[] { ": " }, 2, StringSplitOptions.None);
                TopicComboBox.Text = parts[0];
                MaterialComboBox.Text = parts[1];
            }
            else
            {
                MaterialComboBox.Text = savedMaterial;
            }
        }

        private void TopicComboBox_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Enter || e.Key == Key.Escape || e.Key == Key.Tab) return;

            if (sender is ComboBox cmb)
            {
                string search = cmb.Text.ToLower();
                var filtered = _allTopics.Where(c => c.ToLower().Contains(search)).ToList();
                cmb.ItemsSource = filtered;
                
                cmb.IsDropDownOpen = filtered.Any() && !string.IsNullOrWhiteSpace(search);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string t = TopicComboBox.Text.Trim();
            string m = MaterialComboBox.Text.Trim();
            
            // Scalanie tekstu w jeden ciąg, jeśli podano Temat
            if (!string.IsNullOrEmpty(t) && !string.IsNullOrEmpty(m))
            {
                _rowContext.Material = $"{t}: {m}";
            }
            else if (!string.IsNullOrEmpty(t))
            {
                _rowContext.Material = $"{t}:"; // Sam temat
            }
            else
            {
                _rowContext.Material = m; // Tylko materiał
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

        private SchoolClass? TryGetContextClass()
        {
            if (string.IsNullOrWhiteSpace(_rowContext.ClassName)) { MessageBox.Show("Najpierw przypisz klasę do tej lekcji.", "Brak klasy", MessageBoxButton.OK, MessageBoxImage.Warning); return null; }
            var targetClass = _dataService.GetAllClasses().FirstOrDefault(c => c.Name.Equals(_rowContext.ClassName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (targetClass == null) { MessageBox.Show($"Nie odnaleziono klasy '{_rowContext.ClassName}'.", "Nieznana klasa", MessageBoxButton.OK, MessageBoxImage.Warning); return null; }
            return targetClass;
        }

        private void PlanWork_Click(object sender, RoutedEventArgs e)
        {
            var targetClass = TryGetContextClass(); if (targetClass == null) return;
            new WorkEditorDialog(null, targetClass.Id, null, false) { Owner = Window.GetWindow(this) }.ShowDialog();
        }

        private void PlanRetake_Click(object sender, RoutedEventArgs e)
        {
            var targetClass = TryGetContextClass(); if (targetClass == null) return;
            var retakeDialog = new PlanRetakeDialog(targetClass.Id) { Owner = Window.GetWindow(this) };

            retakeDialog.DatePicker.SelectedDate = _currentDate;
            if (!string.IsNullOrWhiteSpace(_rowContext.TimeSlot))
            {
                var match = Regex.Match(_rowContext.TimeSlot, @"\d{1,2}:\d{2}");
                if (match.Success) retakeDialog.TimeTextBox.Text = match.Value;
            }
            retakeDialog.ShowDialog();
        }
        private void GoToClassView_Click(object sender, RoutedEventArgs e)
        {
            var targetClass = TryGetContextClass(); 
            if (targetClass == null) return;
            
            // Tworzy i otwiera nowe okno z detalami przypisanej klasy
            var classDetailsWindow = new ClassDetailsWindow(targetClass) { Owner = Window.GetWindow(this) };
            classDetailsWindow.ShowDialog();
        }

        protected override void OnClosed(EventArgs e) { _dataService.Dispose(); base.OnClosed(e); }
    }
}