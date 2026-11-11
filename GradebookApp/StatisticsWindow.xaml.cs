using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GradebookApp.Services;
using GradebookApp.ViewModels;

namespace GradebookApp
{
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
        private WorkStatisticsResult? _lastCalculatedStats; 

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

        // PRZYWRÓCONA METODA DLA CHECKBOXÓW
        private void OptionsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitializing) WorkComboBox_SelectionChanged(this, null!);
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
                    var allWorksWithSameTitle = db.WrittenWorks.Where(w => w.Title == targetTitle).Select(w => w.Id).ToList();
                    
                    _lastCalculatedStats = _dataService.CalculateStatistics(null, allWorksWithSameTitle, includeFailed, includeStillFailed);
                }
            }
            else
            {
                var targetWorkIds = workId.HasValue ? new List<int> { workId.Value } : null;
                _lastCalculatedStats = _dataService.CalculateStatistics(classId, targetWorkIds, includeFailed, includeStillFailed);
            }
            
            RenderTable();
        }

        private void DisplayModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitializing) RenderTable();
        }

        private void RenderTable()
        {
            if (_lastCalculatedStats == null) return;
            var stats = _lastCalculatedStats;
            
            TotalRetakesText.Text = $"Liczba poprawionych prac: {stats.TotalRetakes}";
            
            int tableSuccessFrom1 = stats.SuccessfulRetakesFrom1To2Plus;
            int tableFailFrom1 = stats.FailedRetakesFrom1 + stats.SuccessfulRetakesFrom1To1;
            int totalFrom1 = tableSuccessFrom1 + tableFailFrom1;
            
            int tableSuccessFrom2 = stats.SuccessfulRetakesFrom2Plus;
            int tableFailFrom2 = stats.FailedRetakesFrom2Plus;
            int totalFrom2 = tableSuccessFrom2 + tableFailFrom2;

            int totalSuccess = tableSuccessFrom1 + tableSuccessFrom2;
            int totalFail = tableFailFrom1 + tableFailFrom2;
            int grandTotal = totalSuccess + totalFail; 

            double GetPct(int val) => grandTotal > 0 ? ((double)val / grandTotal) * 100 : 0;

            string FormatCell(int val)
            {
                int mode = DisplayModeComboBox.SelectedIndex;
                if (mode == 0) return $"{val}";                      
                if (mode == 1) return $"{GetPct(val):0}%";           
                return $"{val} ({GetPct(val):0}%)";                  
            }

            SuccessFrom1Text.Text = FormatCell(tableSuccessFrom1);
            FailFrom1Text.Text = FormatCell(tableFailFrom1);
            TotalFrom1Text.Text = FormatCell(totalFrom1);

            SuccessFrom2Text.Text = FormatCell(tableSuccessFrom2);
            FailFrom2Text.Text = FormatCell(tableFailFrom2);
            TotalFrom2Text.Text = FormatCell(totalFrom2);

            TotalSuccessText.Text = FormatCell(totalSuccess);
            TotalFailText.Text = FormatCell(totalFail);
            GrandTotalText.Text = FormatCell(grandTotal);

            AvgGainText.Text = $"Średni całkowity przyrost: +{stats.AveragePercentagePointGain:F1} p.p.";
            AvgGain1Text.Text = $"• w grupie ocen niedostatecznych (1): +{stats.AveragePercentagePointGainFrom1:F1} p.p.";
            AvgGain2Text.Text = $"• w grupie ocen pozytywnych (2+): +{stats.AveragePercentagePointGainFrom2Plus:F1} p.p.";
        }

        private void ResetStatsUI()
        {
            TotalRetakesText.Text = "Liczba poprawionych prac: -";
            SuccessFrom1Text.Text = "-";
            FailFrom1Text.Text = "-";
            TotalFrom1Text.Text = "-";
            SuccessFrom2Text.Text = "-";
            FailFrom2Text.Text = "-";
            TotalFrom2Text.Text = "-";
            TotalSuccessText.Text = "-";
            TotalFailText.Text = "-";
            GrandTotalText.Text = "-";
            
            AvgGainText.Text = "Średni całkowity przyrost: - p.p.";
            AvgGain1Text.Text = "• w grupie ocen niedostatecznych (1): - p.p.";
            AvgGain2Text.Text = "• w grupie ocen pozytywnych (2+): - p.p.";
        }

        protected override void OnClosed(System.EventArgs e)
        {
            _dataService.Dispose();
            base.OnClosed(e);
        }
    }
}