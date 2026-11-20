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

    public class ClassStatisticsRowViewModel
    {
        public string ClassName { get; set; } = string.Empty;
        public string SuccessFrom1 { get; set; } = string.Empty;
        public string FailFrom1 { get; set; } = string.Empty;
        public string SuccessFrom2 { get; set; } = string.Empty;
        public string FailFrom2 { get; set; } = string.Empty;
        public string Total { get; set; } = string.Empty;
        public bool IsSummary { get; set; }
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

        private void CompareAllClassesCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            bool isCompare = CompareAllClassesCheckBox.IsChecked == true;
            ClassComboBox.IsEnabled = !isCompare;
            ClassLabelText.Opacity = isCompare ? 0.5 : 1.0;
            SingleClassBorder.Visibility = isCompare ? Visibility.Collapsed : Visibility.Visible;
            AllClassesBorder.Visibility = isCompare ? Visibility.Visible : Visibility.Collapsed;
            
            WorkComboBox_SelectionChanged(this, null!);
        }

        private void WorkComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            bool isCompareAll = CompareAllClassesCheckBox.IsChecked == true;
            int? classId = isCompareAll ? null : (ClassComboBox.SelectedItem as ComboItem)?.Id;
            int? workId = (WorkComboBox.SelectedItem as ComboItem)?.Id;
            
            bool includeFailed = IncludeFailedCheckBox.IsChecked == true;
            bool includeStillFailed = IncludeStillFailedCheckBox.IsChecked == true;

            List<int>? targetWorkIds = null;
            if (workId.HasValue) 
            {
                using (var db = new AppDbContext())
                {
                    string targetTitle = db.WrittenWorks.Find(workId.Value)?.Title ?? "";
                    if (!string.IsNullOrEmpty(targetTitle))
                    {
                        targetWorkIds = db.WrittenWorks.Where(w => w.Title == targetTitle).Select(w => w.Id).ToList();
                    }
                    else
                    {
                        targetWorkIds = new List<int> { workId.Value };
                    }
                }
            }

            if (isCompareAll)
            {
                RenderAllClassesTable(targetWorkIds, includeFailed, includeStillFailed);
            }
            else
            {
                if (!classId.HasValue && workId.HasValue && targetWorkIds != null)
                {
                    _lastCalculatedStats = _dataService.CalculateStatistics(null, targetWorkIds, includeFailed, includeStillFailed);
                }
                else
                {
                    var specificWorkIds = workId.HasValue ? (targetWorkIds ?? new List<int> { workId.Value }) : null;
                    _lastCalculatedStats = _dataService.CalculateStatistics(classId, specificWorkIds, includeFailed, includeStillFailed);
                }
                RenderTable();
            }
        }

        private void RenderAllClassesTable(List<int>? targetWorkIds, bool includeFailed, bool includeStillFailed)
        {
            var classes = _dataService.GetAllClasses().OrderBy(c => c.Name).ToList();
            var rows = new List<ClassStatisticsRowViewModel>();

            int sumSuccessFrom1 = 0;
            int sumFailFrom1 = 0;
            int sumSuccessFrom2 = 0;
            int sumFailFrom2 = 0;
            int sumGrandTotal = 0;
            int sumStillFailedFrom1 = 0;

            var classStatsList = new List<(string className, int s1, int f1, int s2, int f2, int tot, int stillF1)>();

            foreach (var cls in classes)
            {
                var stats = _dataService.CalculateStatistics(cls.Id, targetWorkIds, includeFailed, includeStillFailed);
                int s1 = stats.SuccessfulRetakesFrom1To2Plus;
                int f1 = stats.FailedRetakesFrom1 + stats.SuccessfulRetakesFrom1To1;
                int s2 = stats.SuccessfulRetakesFrom2Plus;
                int f2 = stats.FailedRetakesFrom2Plus;
                int tot = s1 + f1 + s2 + f2;
                int stillF1 = stats.SuccessfulRetakesFrom1To1;

                classStatsList.Add((cls.Name, s1, f1, s2, f2, tot, stillF1));

                sumSuccessFrom1 += s1;
                sumFailFrom1 += f1;
                sumSuccessFrom2 += s2;
                sumFailFrom2 += f2;
                sumGrandTotal += tot;
                sumStillFailedFrom1 += stillF1;
            }

            string FormatVal(int val, int total)
            {
                int mode = DisplayModeComboBox.SelectedIndex;
                double pct = total > 0 ? ((double)val / total) * 100.0 : 0;
                if (mode == 0) return $"{val}";
                if (mode == 1) return $"{pct:0}%";
                return $"{val} ({pct:0}%)";
            }

            foreach (var c in classStatsList)
            {
                rows.Add(new ClassStatisticsRowViewModel
                {
                    ClassName = c.className,
                    SuccessFrom1 = FormatVal(c.s1, c.tot),
                    FailFrom1 = FormatVal(c.f1, c.tot),
                    SuccessFrom2 = FormatVal(c.s2, c.tot),
                    FailFrom2 = FormatVal(c.f2, c.tot),
                    Total = FormatVal(c.tot, sumGrandTotal),
                    IsSummary = false
                });
            }

            // Wiersz podsumowujący na dole
            rows.Add(new ClassStatisticsRowViewModel
            {
                ClassName = "Łącznie",
                SuccessFrom1 = FormatVal(sumSuccessFrom1, sumGrandTotal),
                FailFrom1 = FormatVal(sumFailFrom1, sumGrandTotal),
                SuccessFrom2 = FormatVal(sumSuccessFrom2, sumGrandTotal),
                FailFrom2 = FormatVal(sumFailFrom2, sumGrandTotal),
                Total = FormatVal(sumGrandTotal, sumGrandTotal),
                IsSummary = true
            });

            AllClassesDataGrid.ItemsSource = rows;

            TotalRetakesText.Text = $"Liczba poprawionych prac: {sumGrandTotal}";

            StillFailedFrom1Text.Text = $"Poprawy z oceny 1 z wyższym wynikiem, lecz nadal na ocenę 1 (< 40%): {FormatVal(sumStillFailedFrom1, sumGrandTotal)}";

            var overallStats = _dataService.CalculateStatistics(null, targetWorkIds, includeFailed, includeStillFailed);
            AvgGainText.Text = $"Średni całkowity przyrost: +{overallStats.AveragePercentagePointGain:F1} p.p.";
            AvgGain1Text.Text = $"• w grupie ocen niedostatecznych (1): +{overallStats.AveragePercentagePointGainFrom1:F1} p.p.";
            AvgGain2Text.Text = $"• w grupie ocen pozytywnych (2+): +{overallStats.AveragePercentagePointGainFrom2Plus:F1} p.p.";
        }

        private void DisplayModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitializing)
            {
                if (CompareAllClassesCheckBox.IsChecked == true)
                {
                    WorkComboBox_SelectionChanged(this, null!);
                }
                else
                {
                    RenderTable();
                }
            }
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

            StillFailedFrom1Text.Text = $"Poprawy z oceny 1 z wyższym wynikiem, lecz nadal na ocenę 1 (< 40%): {FormatCell(stats.SuccessfulRetakesFrom1To1)}";

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
            
            StillFailedFrom1Text.Text = "Poprawy z oceny 1 z wyższym wynikiem, lecz nadal na ocenę 1 (< 40%): -";

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