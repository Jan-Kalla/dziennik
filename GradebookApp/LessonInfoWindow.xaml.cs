using System;
using System.Collections.Generic;
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
        private List<string> _allOrderedCurriculumTopics = new List<string>();
        private List<string> _allTopics = new List<string>();
        private List<string> _allMaterials = new List<string>();

        private bool _isTopicProgrammaticChange = false;
        private bool _isMaterialProgrammaticChange = false;

        public LessonInfoWindow(DailyRowWrapper rowContext, string dateDisplay, DateTime currentDate)
        {
            InitializeComponent();
            _rowContext = rowContext;
            _currentDate = currentDate;
            _dataService = new GradebookDataService();

            string classNameInfo = string.IsNullOrWhiteSpace(_rowContext.ClassName) ? "[Brak wybranej klasy]" : _rowContext.ClassName;
            InfoHeader.Text = $"INFORMACJE — Klasa: {classNameInfo} | Godz: {_rowContext.TimeSlot} | {dateDisplay}";

            // 1. Pobieramy tematy w ścisłej hierarchicznej kolejności z Rozkładu materiału
            _allOrderedCurriculumTopics = _dataService.GetOrderedCurriculumTopics();

            // 2. Przypisanie wcześniejszego wpisu (Temat: Materiał)
            string savedMaterial = _rowContext.Material ?? "";
            string currentTopic = "";
            string currentMat = "";

            if (savedMaterial.Contains(":"))
            {
                int colonIdx = savedMaterial.IndexOf(':');
                currentTopic = savedMaterial.Substring(0, colonIdx).Trim();
                currentMat = savedMaterial.Substring(colonIdx + 1).Trim();
            }
            else
            {
                string trimmed = savedMaterial.Trim();
                if (!string.IsNullOrEmpty(trimmed) && _allOrderedCurriculumTopics.Any(top => string.Equals(top, trimmed, StringComparison.OrdinalIgnoreCase)))
                {
                    currentTopic = trimmed;
                    currentMat = "";
                }
                else
                {
                    currentTopic = "";
                    currentMat = trimmed;
                }
            }

            // 3. Rozpoznajemy ostatnio realizowany temat dla tej klasy
            string? lastRealizedTopic = _dataService.GetLastRealizedTopicForClass(_rowContext.ClassName, _currentDate, _rowContext.TimeSlot);

            int lastTopicIdx = -1;
            if (!string.IsNullOrEmpty(lastRealizedTopic))
            {
                lastTopicIdx = _allOrderedCurriculumTopics.FindIndex(t => t.Equals(lastRealizedTopic, StringComparison.OrdinalIgnoreCase));
            }

            int currentTopicIdx = -1;
            if (!string.IsNullOrEmpty(currentTopic))
            {
                currentTopicIdx = _allOrderedCurriculumTopics.FindIndex(t => t.Equals(currentTopic, StringComparison.OrdinalIgnoreCase));
            }

            int startIndex = 0;
            if (lastTopicIdx >= 0)
            {
                startIndex = lastTopicIdx;
            }
            if (currentTopicIdx >= 0 && currentTopicIdx < startIndex)
            {
                startIndex = currentTopicIdx;
            }

            // Selektor domyślnie wyświetla tematy od ostatnio realizowanego i wszystkie kolejne
            _allTopics = _allOrderedCurriculumTopics.Skip(startIndex).ToList();
            if (!_allTopics.Any())
            {
                _allTopics = _allOrderedCurriculumTopics.ToList();
            }

            _isTopicProgrammaticChange = true;
            TopicTextBox.Text = currentTopic;
            _isTopicProgrammaticChange = false;

            // 4. Pobieramy historię i wyłuskujemy same "Materiały" (ignorując "Temat")
            var allHist = _dataService.GetAllScheduleMaterials();
            var extractedMats = new List<string>();
            foreach (var item in allHist)
            {
                if (!string.IsNullOrWhiteSpace(item.Material))
                {
                    string mat = item.Material;
                    if (mat.Contains(": ")) mat = mat.Substring(mat.IndexOf(": ") + 2).Trim();
                    else mat = mat.Trim();
                    
                    if (!string.IsNullOrWhiteSpace(mat)) extractedMats.Add(mat);
                }
            }
            _allMaterials = extractedMats.Distinct().OrderBy(m => m).ToList();

            _isMaterialProgrammaticChange = true;
            MaterialTextBox.Text = currentMat;
            _isMaterialProgrammaticChange = false;

            // 5. Zaczytywanie uwag do pola tekstowego
            NotesTextBox.Text = _rowContext.Notes ?? "";
        }

        // ==================== OBSŁUGA TEMATÓW ====================

        private void TopicTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isTopicProgrammaticChange) return;

            string text = TopicTextBox.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                TopicPopup.IsOpen = false;
                return;
            }

            string search = text.Trim();
            var matches = _allOrderedCurriculumTopics
                .Where(t => t.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            if (!matches.Any())
            {
                matches = _allTopics
                    .Where(t => t.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            if (matches.Any())
            {
                TopicListBox.ItemsSource = matches;
                TopicPopup.IsOpen = true;
            }
            else
            {
                TopicPopup.IsOpen = false;
            }
        }

        private void TopicDropDownButton_Click(object sender, RoutedEventArgs e)
        {
            if (TopicPopup.IsOpen)
            {
                PopulateTopicSuggestions();
            }
        }

        private void PopulateTopicSuggestions()
        {
            var itemsToShow = _allTopics.Any() ? _allTopics : _allOrderedCurriculumTopics;
            TopicListBox.ItemsSource = itemsToShow;

            string current = TopicTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(current) && itemsToShow.Contains(current))
            {
                TopicListBox.ScrollIntoView(current);
            }
        }

        private void TopicListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TopicListBox.SelectedItem is string selectedTopic)
            {
                SelectTopic(selectedTopic);
            }
        }

        private void SelectTopic(string topic)
        {
            _isTopicProgrammaticChange = true;
            try
            {
                TopicTextBox.Text = topic;
                TopicTextBox.CaretIndex = topic.Length;
                TopicPopup.IsOpen = false;
                TopicListBox.SelectedItem = null;
            }
            finally
            {
                _isTopicProgrammaticChange = false;
            }
            TopicTextBox.Focus();
        }

        private void TopicTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down)
            {
                if (!TopicPopup.IsOpen)
                {
                    PopulateTopicSuggestions();
                    TopicPopup.IsOpen = true;
                    e.Handled = true;
                }
                else if (TopicListBox.Items.Count > 0)
                {
                    TopicListBox.Focus();
                    if (TopicListBox.SelectedIndex < 0)
                    {
                        TopicListBox.SelectedIndex = 0;
                    }
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape)
            {
                if (TopicPopup.IsOpen)
                {
                    TopicPopup.IsOpen = false;
                    e.Handled = true;
                }
            }
        }

        private void TopicListBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (TopicListBox.SelectedItem is string selectedTopic)
                {
                    SelectTopic(selectedTopic);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape)
            {
                TopicPopup.IsOpen = false;
                TopicTextBox.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Up && TopicListBox.SelectedIndex <= 0)
            {
                TopicTextBox.Focus();
                TopicTextBox.CaretIndex = TopicTextBox.Text.Length;
                e.Handled = true;
            }
        }

        // ==================== OBSŁUGA MATERIAŁÓW ====================

        private void MaterialTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isMaterialProgrammaticChange) return;

            string text = MaterialTextBox.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                MaterialPopup.IsOpen = false;
                return;
            }

            string search = text.Trim();
            var matches = _allMaterials
                .Where(m => m.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            if (matches.Any())
            {
                MaterialListBox.ItemsSource = matches;
                MaterialPopup.IsOpen = true;
            }
            else
            {
                MaterialPopup.IsOpen = false;
            }
        }

        private void MaterialDropDownButton_Click(object sender, RoutedEventArgs e)
        {
            if (MaterialPopup.IsOpen)
            {
                PopulateMaterialSuggestions();
            }
        }

        private void PopulateMaterialSuggestions()
        {
            MaterialListBox.ItemsSource = _allMaterials;
            string current = MaterialTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(current) && _allMaterials.Contains(current))
            {
                MaterialListBox.ScrollIntoView(current);
            }
        }

        private void MaterialListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MaterialListBox.SelectedItem is string selectedMaterial)
            {
                SelectMaterial(selectedMaterial);
            }
        }

        private void SelectMaterial(string material)
        {
            _isMaterialProgrammaticChange = true;
            try
            {
                MaterialTextBox.Text = material;
                MaterialTextBox.CaretIndex = material.Length;
                MaterialPopup.IsOpen = false;
                MaterialListBox.SelectedItem = null;
            }
            finally
            {
                _isMaterialProgrammaticChange = false;
            }
            MaterialTextBox.Focus();
        }

        private void MaterialTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down)
            {
                if (!MaterialPopup.IsOpen)
                {
                    PopulateMaterialSuggestions();
                    MaterialPopup.IsOpen = true;
                    e.Handled = true;
                }
                else if (MaterialListBox.Items.Count > 0)
                {
                    MaterialListBox.Focus();
                    if (MaterialListBox.SelectedIndex < 0)
                    {
                        MaterialListBox.SelectedIndex = 0;
                    }
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape)
            {
                if (MaterialPopup.IsOpen)
                {
                    MaterialPopup.IsOpen = false;
                    e.Handled = true;
                }
            }
        }

        private void MaterialListBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (MaterialListBox.SelectedItem is string selectedMaterial)
                {
                    SelectMaterial(selectedMaterial);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape)
            {
                MaterialPopup.IsOpen = false;
                MaterialTextBox.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Up && MaterialListBox.SelectedIndex <= 0)
            {
                MaterialTextBox.Focus();
                MaterialTextBox.CaretIndex = MaterialTextBox.Text.Length;
                e.Handled = true;
            }
        }

        // ==================== ZAPIS I NAWIGACJA ====================

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string t = TopicTextBox.Text.Trim();
            string m = MaterialTextBox.Text.Trim();
            
            // Scalanie tekstu w jeden ciąg
            if (!string.IsNullOrEmpty(t) && !string.IsNullOrEmpty(m)) _rowContext.Material = $"{t}: {m}";
            else if (!string.IsNullOrEmpty(t)) _rowContext.Material = $"{t}:";
            else _rowContext.Material = m;

            // Zapis uwag
            _rowContext.Notes = NotesTextBox.Text.Trim();

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

        private void GoToClassView_Click(object sender, RoutedEventArgs e)
        {
            var targetClass = TryGetContextClass(); 
            if (targetClass == null) return;
            new ClassDetailsWindow(targetClass) { Owner = Window.GetWindow(this) }.ShowDialog();
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
                if (match.Success) retakeDialog.SelectedTime = match.Value;
            }
            retakeDialog.ShowDialog();
        }

        protected override void OnClosed(EventArgs e) { _dataService.Dispose(); base.OnClosed(e); }
    }
}
