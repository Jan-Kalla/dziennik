using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GradebookApp.Services;
using GradebookApp.ViewModels;

namespace GradebookApp
{
    // Klasy pomocnicze do poprawnego wyświetlania elementów
    public class ComboItem
    {
        public int? Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
    }

    public partial class StatisticsWindow : Window
    {
        private readonly GradebookDataService _dataService;
        private bool _isInitializing = true;

        public StatisticsWindow()
        {
            InitializeComponent();
            _dataService = new GradebookDataService();
            LoadClasses();
        }

        private void LoadClasses()
        {
            var classes = _dataService.GetAllClasses().Select(c => new ComboItem { Id = c.Id, Name = c.Name }).OrderBy(c => c.Name).ToList();
            classes.Insert(0, new ComboItem { Id = null, Name = "(Wszystkie)" });
            
            _isInitializing = true;
            ClassComboBox.ItemsSource = classes;
            ClassComboBox.SelectedIndex = 0;
            _isInitializing = false;
            
            // Wymuszenie załadowania prac po ustawieniu (Wszystkie)
            ClassComboBox_SelectionChanged(this, null!);
        }

        private void ClassComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            ResetStatsUI();

            int? classId = (ClassComboBox.SelectedItem as ComboItem)?.Id;

            using (var db = new AppDbContext())
            {
                var query = db.WrittenWorks.AsQueryable();
                
                if (classId.HasValue)
                {
                    query = query.Where(w => db.StudentWorkRecords.Any(r => r.WrittenWorkId == w.Id && r.Student.SchoolClassId == classId.Value));
                }

                var works = query.Select(w => new ComboItem { Id = w.Id, Title = w.Title }).OrderByDescending(w => w.Title).ToList();
                
                // Grupowanie unikalnych prac po nazwach dla widoku globalnego
                if (!classId.HasValue) 
                {
                    works = works.GroupBy(w => w.Title).Select(g => g.First()).OrderBy(w => w.Title).ToList();
                }

                works.Insert(0, new ComboItem { Id = null, Title = "(Wszystkie)" });

                _isInitializing = true;
                WorkComboBox.ItemsSource = works;
                WorkComboBox.SelectedIndex = 0;
                _isInitializing = false;
            }

            WorkComboBox_SelectionChanged(this, null!);
        }

        private void WorkComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            int? classId = (ClassComboBox.SelectedItem as ComboItem)?.Id;
            int? workId = (WorkComboBox.SelectedItem as ComboItem)?.Id;
            
            bool includeFailed = IncludeFailedCheckBox.IsChecked == true;
            bool includeStillFailed = IncludeStillFailedCheckBox.IsChecked == true;

            if (!classId.HasValue && workId.HasValue) 
            {
                using (var db = new AppDbContext())
                {
                    string targetTitle = db.WrittenWorks.Find(workId.Value)?.Title ?? "";
                    
                    // Zbieramy wszystkie ID tej samej pracy do paczki
                    var allWorksWithSameTitle = db.WrittenWorks.Where(w => w.Title == targetTitle).Select(w => w.Id).ToList();
                    
                    // Odpytujemy serwis jednym, masowym zapytaniem (żadnego uśredniania średnich!)
                    var stats = _dataService.CalculateStatistics(null, allWorksWithSameTitle, includeFailed, includeStillFailed);
                    UpdateStatsUI(stats);
                }
            }
            else
            {
                // Tworzymy paczkę z jednym elementem (lub null, jeśli wybrano "(Wszystkie)" prace)
                var targetWorkIds = workId.HasValue ? new List<int> { workId.Value } : null;
                var stats = _dataService.CalculateStatistics(classId, targetWorkIds, includeFailed, includeStillFailed);
                UpdateStatsUI(stats);
            }
        }

        private void OptionsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitializing)
            {
                WorkComboBox_SelectionChanged(this, null!);
            }
        }

        private void UpdateStatsUI(WorkStatisticsResult stats)
        {
            TotalRetakesText.Text = $"Liczba poprawionych prac: {stats.TotalRetakes}";
            
            // Przygotowanie tekstów (procenty nadal liczone wewnątrz grupy)
            int totalFrom1 = stats.SuccessfulRetakesFrom1To2Plus + stats.SuccessfulRetakesFrom1To1 + stats.FailedRetakesFrom1;
            double success1To2Pct = totalFrom1 > 0 ? (double)stats.SuccessfulRetakesFrom1To2Plus / totalFrom1 * 100 : 0;
            double success1To1Pct = totalFrom1 > 0 ? (double)stats.SuccessfulRetakesFrom1To1 / totalFrom1 * 100 : 0;
            double fail1Pct = totalFrom1 > 0 ? (double)stats.FailedRetakesFrom1 / totalFrom1 * 100 : 0;
            
            SuccessFrom1To2Text.Text = $"• Udane (ocena 2+): {stats.SuccessfulRetakesFrom1To2Plus} ({success1To2Pct:0}%)";
            SuccessFrom1To1Text.Text = $"• Częściowo udane (nadal 1): {stats.SuccessfulRetakesFrom1To1} ({success1To1Pct:0}%)";
            FailFrom1Text.Text = $"• Nieudane (ten sam lub gorszy wynik): {stats.FailedRetakesFrom1} ({fail1Pct:0}%)";
            
            int totalFrom2 = stats.SuccessfulRetakesFrom2Plus + stats.FailedRetakesFrom2Plus;
            double success2Pct = totalFrom2 > 0 ? (double)stats.SuccessfulRetakesFrom2Plus / totalFrom2 * 100 : 0;
            double fail2Pct = totalFrom2 > 0 ? (double)stats.FailedRetakesFrom2Plus / totalFrom2 * 100 : 0;

            SuccessFrom2Text.Text = $"• Udane (ocena wyższa): {stats.SuccessfulRetakesFrom2Plus} ({success2Pct:0}%)";
            FailFrom2Text.Text = $"• Nieudane (brak poprawy): {stats.FailedRetakesFrom2Plus} ({fail2Pct:0}%)";

            // WSPÓLNE SKALOWANIE: Wyciągamy absolutnie największą liczbę ze wszystkich 5 kategorii
            int maxCount = new[] { 
                stats.SuccessfulRetakesFrom1To2Plus, 
                stats.SuccessfulRetakesFrom1To1, 
                stats.FailedRetakesFrom1, 
                stats.SuccessfulRetakesFrom2Plus, 
                stats.FailedRetakesFrom2Plus 
            }.Max();

            // Maksymalna szerokość paska dla okna o szerokości 720 (340px kolumna tekstu = zostaje ok. 280-300px na sam pasek)
            double maxBarWidth = 280.0; 

            // Renderowanie pasków względem maxCount (26 będzie wizualnie dłuższe niż 18)
            SuccessFrom1To2Bar.Width = maxCount > 0 ? ((double)stats.SuccessfulRetakesFrom1To2Plus / maxCount) * maxBarWidth : 0;
            SuccessFrom1To1Bar.Width = maxCount > 0 ? ((double)stats.SuccessfulRetakesFrom1To1 / maxCount) * maxBarWidth : 0;
            FailFrom1Bar.Width = maxCount > 0 ? ((double)stats.FailedRetakesFrom1 / maxCount) * maxBarWidth : 0;
            
            SuccessFrom2Bar.Width = maxCount > 0 ? ((double)stats.SuccessfulRetakesFrom2Plus / maxCount) * maxBarWidth : 0;
            FailFrom2Bar.Width = maxCount > 0 ? ((double)stats.FailedRetakesFrom2Plus / maxCount) * maxBarWidth : 0;

            AvgGainText.Text = $"Średni całkowity przyrost: +{stats.AveragePercentagePointGain:F1} p.p.";
            AvgGain1Text.Text = $"• w grupie ocen niedostatecznych (1): +{stats.AveragePercentagePointGainFrom1:F1} p.p.";
            AvgGain2Text.Text = $"• w grupie ocen pozytywnych (2+): +{stats.AveragePercentagePointGainFrom2Plus:F1} p.p.";
        }

        private void ResetStatsUI()
        {
            TotalRetakesText.Text = "Liczba poprawionych prac: -";
            SuccessFrom1To2Text.Text = "• Udane: -";
            SuccessFrom1To1Text.Text = "• Częściowo udane (nadal 1): -";
            FailFrom1Text.Text = "• Nieudane (brak poprawy): -";
            
            SuccessFrom2Text.Text = "• Udane: -";
            FailFrom2Text.Text = "• Nieudane (brak poprawy): -";
            
            AvgGainText.Text = "Średni całkowity przyrost: - p.p.";
            AvgGain1Text.Text = "• w grupie ocen niedostatecznych (1): - p.p.";
            AvgGain2Text.Text = "• w grupie ocen pozytywnych (2+): - p.p.";
            
            SuccessFrom1To2Bar.Width = 0;
            SuccessFrom1To1Bar.Width = 0;
            FailFrom1Bar.Width = 0;
            SuccessFrom2Bar.Width = 0;
            FailFrom2Bar.Width = 0;
        }

        protected override void OnClosed(System.EventArgs e)
        {
            _dataService.Dispose();
            base.OnClosed(e);
        }
    }
}