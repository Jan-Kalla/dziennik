using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;

namespace GradebookApp
{
    public partial class StudentWorkDetailsWindow : Window
    {
        private AppDbContext _dbContext;
        private int _studentId;
        private int _workId;
        private WrittenWork? _currentWork;
        private bool _isRetakeActive = false;
        private bool _isLoaded = false;
        
        private List<StudentTaskScore> _existingScores = new List<StudentTaskScore>();
        
        private static string _lastUsedGroup = string.Empty;
        private static string _lastUsedRetakeGroup = string.Empty;

        public event EventHandler? DataSavedEvent;
        public ObservableCollection<TaskScoreViewModel> ScoresCollection { get; set; } = new ObservableCollection<TaskScoreViewModel>();

        public StudentWorkDetailsWindow(int studentId, int workId)
        {
            InitializeComponent();
            _dbContext = new AppDbContext();
            _studentId = studentId;
            _workId = workId;

            LoadData(); 
            _isLoaded = true;
        }

        private void LoadData()
        {
            var student = _dbContext.Students.Include(s => s.SchoolClass).FirstOrDefault(s => s.Id == _studentId);
            _currentWork = _dbContext.WrittenWorks.Include(w => w.Tasks).FirstOrDefault(w => w.Id == _workId);
            
            _existingScores = _dbContext.StudentTaskScores.Where(s => s.StudentId == _studentId).ToList();
            var workRecord = _dbContext.StudentWorkRecords.FirstOrDefault(r => r.StudentId == _studentId && r.WrittenWorkId == _workId);

            if (student == null || _currentWork == null) return;

            ScoresDataGrid.ItemsSource = ScoresCollection;

            if (workRecord != null)
            {
                CustomDateWrittenPicker.SelectedDate = workRecord.CustomDateWritten;
                CustomDateEnteredPicker.SelectedDate = workRecord.CustomDateEntered;
                AbsentCheckBox.IsChecked = workRecord.IsAbsent;
                
                RetakeDeadlinePicker.SelectedDate = workRecord.RetakeDeadline;
                RetakeDateWrittenPicker.SelectedDate = workRecord.RetakeDateWritten;
                RetakeDateEnteredPicker.SelectedDate = workRecord.RetakeDateEntered;
                _isRetakeActive = workRecord.IsRetakeActive;
            }

            if (_currentWork.HasGroups)
            {
                GroupTextBox.Visibility = Visibility.Collapsed;
                RetakeGroupTextBox.Visibility = Visibility.Collapsed;
                
                GroupComboBox.Visibility = Visibility.Visible;
                RetakeGroupComboBox.Visibility = Visibility.Visible;

                var groups = _currentWork.Tasks.Where(t => !string.IsNullOrEmpty(t.GroupName)).Select(t => t.GroupName!).Distinct().OrderBy(g => g).ToList();
                GroupComboBox.ItemsSource = groups;
                RetakeGroupComboBox.ItemsSource = groups;

                string targetGroup = workRecord != null && !string.IsNullOrEmpty(workRecord.Group) && groups.Contains(workRecord.Group)
                    ? workRecord.Group
                    : (groups.Contains(_lastUsedGroup) ? _lastUsedGroup : groups.FirstOrDefault() ?? string.Empty);

                GroupComboBox.SelectedItem = targetGroup;
                
                if (workRecord != null && !string.IsNullOrEmpty(workRecord.RetakeGroup) && groups.Contains(workRecord.RetakeGroup))
                {
                    RetakeGroupComboBox.SelectedItem = workRecord.RetakeGroup;
                }
                else
                {
                    SetRotationalRetakeGroup(groups, targetGroup);
                }
            }
            else
            {
                GroupTextBox.Visibility = Visibility.Visible;
                RetakeGroupTextBox.Visibility = Visibility.Visible;
                
                GroupComboBox.Visibility = Visibility.Collapsed;
                RetakeGroupComboBox.Visibility = Visibility.Collapsed;

                GroupTextBox.Text = string.IsNullOrEmpty(workRecord?.Group) ? _lastUsedGroup : workRecord.Group;
                RetakeGroupTextBox.Text = string.IsNullOrEmpty(workRecord?.RetakeGroup) ? _lastUsedRetakeGroup : workRecord.RetakeGroup;
            }

            RebuildScoresCollection();

            UpdateDeadlinePrompt(); 
            ApplyRetakeVisibility(); 

            WorkTypeAndTitleText.Text = $"{_currentWork.WorkType.ToUpper()}: {_currentWork.Title}";
            ClassGroupText.Text = $"Klasa: {student.SchoolClass.Name} | Uczeń: {student.FullName}";
            
            string defaultWritten = _currentWork.DateWritten?.ToString("dd.MM.yyyy") ?? "Brak";
            string defaultEntered = _currentWork.DateEntered?.ToString("dd.MM.yyyy") ?? "Brak";
            DatesText.Text = $"Terminy całej klasy - Napisano: {defaultWritten} | Wpisano: {defaultEntered}";

            if (_currentWork.WorkType.ToLower() == "aktywność")
            {
                ScoreControlsPanel.Visibility = Visibility.Collapsed; 
                DetailsGrid.Visibility = Visibility.Collapsed;
                RetakePromptPanel.Visibility = Visibility.Collapsed; 
                RetakeEditPanel.Visibility = Visibility.Collapsed;
                
                ActivityScorePanel.Visibility = Visibility.Visible;
                ActivityMaxPointsText.Text = $"/ {_currentWork.MaxFinalPoints} pkt";

                var vm = ScoresCollection.FirstOrDefault();
                if (vm != null) ActivityScoreTextBox.Text = vm.ScoreL3?.ToString() ?? "";
            }
            RecalculateTotalScore(); 
        }

        private void GroupComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_currentWork == null || !_currentWork.HasGroups) return;
            
            string? selectedBaseGroup = GroupComboBox.SelectedItem as string;

            if (_isLoaded && selectedBaseGroup != null)
            {
                var groups = _currentWork.Tasks.Where(t => !string.IsNullOrEmpty(t.GroupName)).Select(t => t.GroupName!).Distinct().OrderBy(g => g).ToList();
                SetRotationalRetakeGroup(groups, selectedBaseGroup);
            }

            if (_isLoaded) RebuildScoresCollection();
        }

        private void RetakeGroupComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_currentWork == null || !_currentWork.HasGroups || !_isRetakeActive || !_isLoaded) return;
            
            RebuildScoresCollection();
        }

        private void SetRotationalRetakeGroup(List<string> allGroups, string currentBaseGroup)
        {
            if (allGroups.Count <= 1 || string.IsNullOrEmpty(currentBaseGroup)) return;

            int currentIndex = allGroups.IndexOf(currentBaseGroup);
            int nextIndex = (currentIndex + 1) % allGroups.Count; 
            
            RetakeGroupComboBox.SelectedItem = allGroups[nextIndex];
        }

        private void RebuildScoresCollection()
        {
            if (_currentWork == null) return;

            ScoresCollection.Clear();

            string baseGroupName = GroupComboBox.Visibility == Visibility.Visible ? (GroupComboBox.SelectedItem as string ?? "") : GroupTextBox.Text.Trim();
            string retakeGroupName = RetakeGroupComboBox.Visibility == Visibility.Visible ? (RetakeGroupComboBox.SelectedItem as string ?? "") : RetakeGroupTextBox.Text.Trim();

            var baseTasks = _currentWork.HasGroups && !string.IsNullOrEmpty(baseGroupName)
                ? _currentWork.Tasks.Where(t => t.GroupName == baseGroupName).OrderBy(t => t.TaskNumber).ToList()
                : (!_currentWork.HasGroups ? _currentWork.Tasks.OrderBy(t => t.TaskNumber).ToList() : new List<WrittenWorkTask>());

            var retakeTasks = _currentWork.HasGroups && !string.IsNullOrEmpty(retakeGroupName)
                ? _currentWork.Tasks.Where(t => t.GroupName == retakeGroupName).OrderBy(t => t.TaskNumber).ToList()
                : (!_currentWork.HasGroups ? _currentWork.Tasks.OrderBy(t => t.TaskNumber).ToList() : new List<WrittenWorkTask>());

            int maxTaskNum = Math.Max(
                baseTasks.Any() ? baseTasks.Max(t => t.TaskNumber) : 0,
                retakeTasks.Any() ? retakeTasks.Max(t => t.TaskNumber) : 0
            );

            for (int i = 1; i <= maxTaskNum; i++)
            {
                var bTask = baseTasks.FirstOrDefault(t => t.TaskNumber == i);
                var rTask = retakeTasks.FirstOrDefault(t => t.TaskNumber == i);

                var vm = new TaskScoreViewModel { TaskNumber = i };

                if (bTask != null)
                {
                    vm.BaseTaskId = bTask.Id; 
                    vm.MaxL1 = bTask.MaxPointsLevel1;
                    vm.MaxL2 = bTask.MaxPointsLevel2;
                    vm.MaxL3 = bTask.MaxPointsLevel3;

                    var bScore = _existingScores.FirstOrDefault(s => s.WrittenWorkTaskId == bTask.Id);
                    if (bScore != null)
                    {
                        vm.ScoreL1 = bScore.PointsLevel1;
                        vm.ScoreL2 = bScore.PointsLevel2;
                        vm.ScoreL3 = bScore.PointsLevel3;
                    }
                }
                else
                {
                    vm.MaxL1 = 0; vm.MaxL2 = 0; vm.MaxL3 = 0; 
                }

                if (rTask != null)
                {
                    vm.RetakeTaskId = rTask.Id; 
                    vm.RetakeMaxL1 = rTask.MaxPointsLevel1;
                    vm.RetakeMaxL2 = rTask.MaxPointsLevel2;
                    vm.RetakeMaxL3 = rTask.MaxPointsLevel3;

                    var rScore = _existingScores.FirstOrDefault(s => s.WrittenWorkTaskId == rTask.Id);
                    if (rScore != null)
                    {
                        vm.RetakeScoreL1 = rScore.RetakePointsLevel1;
                        vm.RetakeScoreL2 = rScore.RetakePointsLevel2;
                        vm.RetakeScoreL3 = rScore.RetakePointsLevel3;
                    }
                }
                else
                {
                    vm.RetakeMaxL1 = 0; vm.RetakeMaxL2 = 0; vm.RetakeMaxL3 = 0; 
                }

                vm.PropertyChanged += (s, e) => RecalculateTotalScore();
                ScoresCollection.Add(vm);
            }
            RecalculateTotalScore();
        }

        private void UpdateDeadlinePrompt()
        {
            DateTime? baseDate = CustomDateEnteredPicker.SelectedDate ?? _currentWork?.DateEntered;
            if (baseDate.HasValue)
            {
                DateTime deadline = baseDate.Value.AddDays(14);
                if (_isLoaded) RetakeDeadlinePicker.SelectedDate = deadline; 
                DateTime? displayDate = RetakeDeadlinePicker.SelectedDate ?? deadline;
                RetakeDeadlinePromptText.Text = $"Termin na poprawę: do {displayDate.Value:dd.MM.yyyy}";
            }
            else
            {
                RetakeDeadlinePromptText.Text = "Termin na poprawę: (Brak daty wpisania)";
                if (_isLoaded) RetakeDeadlinePicker.SelectedDate = null;
            }
        }

        private void CustomDateEnteredPicker_SelectedDateChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateDeadlinePrompt();
        }

        private void ActivateRetakeButton_Click(object sender, RoutedEventArgs e)
        {
            _isRetakeActive = true;
            ApplyRetakeVisibility();
            RecalculateTotalScore();
        }

        private void DeactivateRetakeButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Czy na pewno chcesz usunąć poprawę? Spowoduje to skasowanie ewentualnych punktów, grup i dat wpisanych w tym panelu podczas zapisu.", 
                                         "Potwierdzenie", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                _isRetakeActive = false;
                foreach (var vm in ScoresCollection)
                {
                    vm.RetakeScoreL1 = null; vm.RetakeScoreL2 = null; vm.RetakeScoreL3 = null;
                }
                
                if (_currentWork != null && _currentWork.HasGroups) 
                {
                    var groups = _currentWork.Tasks.Where(t => !string.IsNullOrEmpty(t.GroupName)).Select(t => t.GroupName!).Distinct().OrderBy(g => g).ToList();
                    SetRotationalRetakeGroup(groups, GroupComboBox.SelectedItem as string ?? "");
                }
                else 
                {
                    RetakeGroupTextBox.Text = string.Empty; 
                }
                    
                RetakeDateWrittenPicker.SelectedDate = null;
                RetakeDateEnteredPicker.SelectedDate = null;

                ApplyRetakeVisibility();
                RecalculateTotalScore();
            }
        }

        private void ApplyRetakeVisibility()
        {
            var converter = new System.Windows.Media.BrushConverter();

            if (_isRetakeActive)
            {
                RetakePromptPanel.Visibility = Visibility.Collapsed;
                RetakeEditPanel.Visibility = Visibility.Visible;
                RetakeCol1.Visibility = Visibility.Visible;
                RetakeCol2.Visibility = Visibility.Visible;
                RetakeCol3.Visibility = Visibility.Visible;

                FillMaxPointsButton.Background = (System.Windows.Media.Brush)converter.ConvertFromString("#2E4A2E")!;
                FillMaxPointsButton.BorderBrush = (System.Windows.Media.Brush)converter.ConvertFromString("#4CAF50")!;
            }
            else
            {
                RetakePromptPanel.Visibility = Visibility.Visible;
                RetakeEditPanel.Visibility = Visibility.Collapsed;
                RetakeCol1.Visibility = Visibility.Collapsed;
                RetakeCol2.Visibility = Visibility.Collapsed;
                RetakeCol3.Visibility = Visibility.Collapsed;

                FillMaxPointsButton.Background = (System.Windows.Media.Brush)converter.ConvertFromString("#3E3E42")!;
                FillMaxPointsButton.BorderBrush = (System.Windows.Media.Brush)converter.ConvertFromString("#555555")!;
            }
        }

        private void AbsentCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            ScoresDataGrid.IsEnabled = AbsentCheckBox.IsChecked != true; 
            RecalculateTotalScore();
        }

        private void ToggleDetailsButton_Click(object sender, RoutedEventArgs e)
        {
            if (DetailsGrid.Visibility == Visibility.Collapsed)
            {
                DetailsGrid.Visibility = Visibility.Visible;
                ToggleDetailsButton.Content = "Ukryj szczegóły punktacji ↑";
            }
            else
            {
                DetailsGrid.Visibility = Visibility.Collapsed;
                ToggleDetailsButton.Content = "Pokaż / Edytuj szczegóły punktacji ↓";
            }
        }

        private void FillMaxPoints_Click(object sender, RoutedEventArgs e)
        {
            if (AbsentCheckBox.IsChecked == true) return;

            foreach (var vm in ScoresCollection)
            {
                if (_isRetakeActive)
                {
                    if (vm.RetakeTaskId.HasValue) 
                    {
                        if (vm.RetakeMaxL3 > 0) vm.RetakeScoreL3 = vm.RetakeMaxL3;
                        else if (vm.RetakeMaxL2 > 0) vm.RetakeScoreL2 = vm.RetakeMaxL2;
                        else if (vm.RetakeMaxL1 > 0) vm.RetakeScoreL1 = vm.RetakeMaxL1;
                    }
                }
                else
                {
                    if (vm.BaseTaskId.HasValue)
                    {
                        if (vm.MaxL3 > 0) vm.ScoreL3 = vm.MaxL3;
                        else if (vm.MaxL2 > 0) vm.ScoreL2 = vm.MaxL2;
                        else if (vm.MaxL1 > 0) vm.ScoreL1 = vm.MaxL1;
                    }
                }
            }
        }

        private void ActivityScoreTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_currentWork?.WorkType.ToLower() == "aktywność")
            {
                var vm = ScoresCollection.FirstOrDefault();
                if (vm != null)
                {
                    if (double.TryParse(ActivityScoreTextBox.Text.Trim(), out double score))
                        vm.ScoreL3 = score;
                    else
                        vm.ScoreL3 = null;
                }
            }
        }

        private double CalculateScoreFromViewModels(double M, bool isRetake)
        {
            var validVms = isRetake ? ScoresCollection.Where(vm => vm.RetakeTaskId.HasValue).ToList() : ScoresCollection.Where(vm => vm.BaseTaskId.HasValue).ToList();
            
            if (M <= 0 || validVms.Count == 0) return 0;
            
            int n = validVms.Count; 
            double sumQ1 = 0, sumQ2 = 0, sumQ3 = 0;

            foreach (var vm in validVms)
            {
                double p1 = isRetake ? (vm.RetakeScoreL1 ?? 0) : (vm.ScoreL1 ?? 0);
                double p2 = isRetake ? (vm.RetakeScoreL2 ?? 0) : (vm.ScoreL2 ?? 0);
                double p3 = isRetake ? (vm.RetakeScoreL3 ?? 0) : (vm.ScoreL3 ?? 0);

                double P1 = isRetake ? (vm.RetakeMaxL1 ?? 0) : (vm.MaxL1 ?? 0);
                double P2 = isRetake ? (vm.RetakeMaxL2 ?? 0) : (vm.MaxL2 ?? 0);
                double P3 = isRetake ? (vm.RetakeMaxL3 ?? 0) : (vm.MaxL3 ?? 0);

                sumQ1 += (P1 > 0) ? (p1 / P1) : 0;
                sumQ2 += (P2 > 0) ? (p2 / P2) : 0;
                sumQ3 += (P3 > 0) ? (p3 / P3) : 0;
            }

            return Math.Round(M * (0.54 * (sumQ1 / n) + 0.9 * (sumQ2 / n) + (sumQ3 / n)), 0, MidpointRounding.AwayFromZero);
        }

        private void RecalculateTotalScore()
        {
            if (AbsentCheckBox.IsChecked == true)
            {
                RetakePromptPanel.IsEnabled = false; 
                TotalResultText.Text = "NB";
                TotalResultText.Foreground = System.Windows.Media.Brushes.IndianRed; 
                PercentageResultText.Text = string.Empty; 
                BasePanelScoreText.Text = "NB";
                RetakePanelScoreText.Text = string.Empty;
                return;
            }

            RetakePromptPanel.IsEnabled = true;
            double M = _currentWork?.MaxFinalPoints ?? 0;
            
            double totalBase = CalculateScoreFromViewModels(M, false);
            double totalRetake = CalculateScoreFromViewModels(M, true);

            bool hasBaseScores = ScoresCollection.Any(vm => vm.BaseTaskId.HasValue && (vm.ScoreL1 != null || vm.ScoreL2 != null || vm.ScoreL3 != null));
            bool hasRetakeScores = ScoresCollection.Any(vm => vm.RetakeTaskId.HasValue && (vm.RetakeScoreL1 != null || vm.RetakeScoreL2 != null || vm.RetakeScoreL3 != null));

            BasePanelScoreText.Text = hasBaseScores ? $"{totalBase} / {M} pkt ({Math.Floor(M > 0 ? (totalBase / M) * 100 : 0)}%)" : "Brak ocen";
            RetakePanelScoreText.Text = hasRetakeScores ? $"{totalRetake} / {M} pkt ({Math.Floor(M > 0 ? (totalRetake / M) * 100 : 0)}%)" : "Brak ocen";

            if (_isRetakeActive)
            {
                if (hasRetakeScores && totalRetake > totalBase)
                {
                    TotalResultText.Text = $"{totalRetake} / {M} pkt (popr.)";
                    TotalResultText.Foreground = System.Windows.Media.Brushes.LightGreen;
                    PercentageResultText.Text = M > 0 ? $"{Math.Floor((totalRetake / M) * 100)}%" : "0%";
                }
                else if (hasBaseScores)
                {
                    TotalResultText.Text = $"{totalBase} / {M} pkt";
                    TotalResultText.Foreground = System.Windows.Media.Brushes.LightSkyBlue;
                    PercentageResultText.Text = M > 0 ? $"{Math.Floor((totalBase / M) * 100)}%" : "0%";
                }
                else
                {
                    TotalResultText.Text = "Brak ocen";
                    TotalResultText.Foreground = System.Windows.Media.Brushes.LightSkyBlue;
                    PercentageResultText.Text = string.Empty;
                }
            }
            else
            {
                if (hasBaseScores)
                {
                    TotalResultText.Text = $"{totalBase} / {M} pkt";
                    TotalResultText.Foreground = System.Windows.Media.Brushes.LightSkyBlue;
                    PercentageResultText.Text = M > 0 ? $"{Math.Floor((totalBase / M) * 100)}%" : "0%";
                }
                else
                {
                    TotalResultText.Text = "Brak ocen";
                    TotalResultText.Foreground = System.Windows.Media.Brushes.LightSkyBlue;
                    PercentageResultText.Text = string.Empty;
                }
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Keyboard.ClearFocus();

            if (_currentWork != null && _currentWork.HasGroups && GroupComboBox.SelectedItem == null && AbsentCheckBox.IsChecked == false)
            {
                MessageBox.Show("Wybrana praca korzysta ze zmiennych grup. Musisz wybrać grupę, aby poprawnie zapisać punkty.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool hasAnyBaseScore = ScoresCollection.Any(vm => vm.BaseTaskId.HasValue && (vm.ScoreL1 != null || vm.ScoreL2 != null || vm.ScoreL3 != null));
            bool hasAllBaseScores = ScoresCollection.Where(vm => vm.BaseTaskId.HasValue).All(vm => vm.ScoreL1 != null || vm.ScoreL2 != null || vm.ScoreL3 != null);

            if (AbsentCheckBox.IsChecked == false && hasAnyBaseScore && !hasAllBaseScores)
            {
                MessageBox.Show("Narzędzie bezpieczeństwa PSO: Rozpoczęto wpisywanie ocen dla terminu bazowego. Musisz uzupełnić oceny we wszystkich zadaniach, aby wynik mógł zostać poprawnie uśredniony.", 
                                "Ajajajaj! Brakujące wpisy...", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_isRetakeActive)
            {
                bool hasAnyRetakeScore = ScoresCollection.Any(vm => vm.RetakeTaskId.HasValue && (vm.RetakeScoreL1 != null || vm.RetakeScoreL2 != null || vm.RetakeScoreL3 != null));
                bool hasAllRetakeScores = ScoresCollection.Where(vm => vm.RetakeTaskId.HasValue).All(vm => vm.RetakeScoreL1 != null || vm.RetakeScoreL2 != null || vm.RetakeScoreL3 != null);

                if (hasAnyRetakeScore && !hasAllRetakeScores)
                {
                    MessageBox.Show("Narzędzie bezpieczeństwa PSO: Rozpoczęto wpisywanie ocen w trybie POPRAWY. Musisz uzupełnić poprawione oceny we wszystkich zadaniach.", 
                                    "Ajajajaj! Brakujące wpisy...", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            foreach (var vm in ScoresCollection)
            {
                if (vm.BaseTaskId.HasValue)
                {
                    if (vm.ScoreL1 > vm.MaxL1) { ShowError(vm.TaskNumber, 1, vm.MaxL1, "Baza"); return; }
                    if (vm.ScoreL2 > vm.MaxL2) { ShowError(vm.TaskNumber, 2, vm.MaxL2, "Baza"); return; }
                    if (vm.ScoreL3 > vm.MaxL3) { ShowError(vm.TaskNumber, 3, vm.MaxL3, "Baza"); return; }
                }
                
                if (_isRetakeActive && vm.RetakeTaskId.HasValue)
                {
                    if (vm.RetakeScoreL1 > vm.RetakeMaxL1) { ShowError(vm.TaskNumber, 1, vm.RetakeMaxL1, "Poprawa"); return; }
                    if (vm.RetakeScoreL2 > vm.RetakeMaxL2) { ShowError(vm.TaskNumber, 2, vm.RetakeMaxL2, "Poprawa"); return; }
                    if (vm.RetakeScoreL3 > vm.RetakeMaxL3) { ShowError(vm.TaskNumber, 3, vm.RetakeMaxL3, "Poprawa"); return; }
                }
            }
            
            var workRecord = _dbContext.StudentWorkRecords.FirstOrDefault(r => r.StudentId == _studentId && r.WrittenWorkId == _workId);
            if (workRecord == null)
            {
                workRecord = new StudentWorkRecord { StudentId = _studentId, WrittenWorkId = _workId };
                _dbContext.StudentWorkRecords.Add(workRecord);
            }
            
            workRecord.Group = _currentWork != null && _currentWork.HasGroups ? (GroupComboBox.SelectedItem as string ?? string.Empty) : GroupTextBox.Text.Trim();
            workRecord.CustomDateWritten = CustomDateWrittenPicker.SelectedDate;
            workRecord.CustomDateEntered = CustomDateEnteredPicker.SelectedDate;
            workRecord.IsAbsent = AbsentCheckBox.IsChecked == true;
            
            workRecord.RetakeGroup = _isRetakeActive ? (_currentWork != null && _currentWork.HasGroups ? (RetakeGroupComboBox.SelectedItem as string ?? string.Empty) : RetakeGroupTextBox.Text.Trim()) : string.Empty;
            workRecord.IsRetakeActive = _isRetakeActive;
            workRecord.RetakeDeadline = RetakeDeadlinePicker.SelectedDate;
            workRecord.RetakeDateWritten = _isRetakeActive ? RetakeDateWrittenPicker.SelectedDate : null;
            workRecord.RetakeDateEntered = _isRetakeActive ? RetakeDateEnteredPicker.SelectedDate : null;

            _lastUsedGroup = workRecord.Group;
            if (_isRetakeActive) _lastUsedRetakeGroup = workRecord.RetakeGroup;

            foreach (var vm in ScoresCollection)
            {
                StudentTaskScore? baseRecord = null;
                if (vm.BaseTaskId.HasValue)
                {
                    baseRecord = _dbContext.StudentTaskScores.FirstOrDefault(s => s.StudentId == _studentId && s.WrittenWorkTaskId == vm.BaseTaskId.Value);
                    if (baseRecord == null)
                    {
                        baseRecord = new StudentTaskScore { StudentId = _studentId, WrittenWorkTaskId = vm.BaseTaskId.Value };
                        _dbContext.StudentTaskScores.Add(baseRecord);
                    }
                    baseRecord.PointsLevel1 = AbsentCheckBox.IsChecked == true ? 0 : vm.ScoreL1;
                    baseRecord.PointsLevel2 = AbsentCheckBox.IsChecked == true ? 0 : vm.ScoreL2;
                    baseRecord.PointsLevel3 = AbsentCheckBox.IsChecked == true ? 0 : vm.ScoreL3;

                    if (!_isRetakeActive)
                    {
                        baseRecord.RetakePointsLevel1 = null;
                        baseRecord.RetakePointsLevel2 = null;
                        baseRecord.RetakePointsLevel3 = null;
                    }
                }

                if (vm.RetakeTaskId.HasValue)
                {
                    StudentTaskScore? retakeRecord = _dbContext.StudentTaskScores.FirstOrDefault(s => s.StudentId == _studentId && s.WrittenWorkTaskId == vm.RetakeTaskId.Value);
                    if (retakeRecord == null)
                    {
                        retakeRecord = new StudentTaskScore { StudentId = _studentId, WrittenWorkTaskId = vm.RetakeTaskId.Value };
                        _dbContext.StudentTaskScores.Add(retakeRecord);
                    }

                    if (_isRetakeActive)
                    {
                        retakeRecord.RetakePointsLevel1 = vm.RetakeScoreL1;
                        retakeRecord.RetakePointsLevel2 = vm.RetakeScoreL2;
                        retakeRecord.RetakePointsLevel3 = vm.RetakeScoreL3;
                    }
                    else
                    {
                        retakeRecord.RetakePointsLevel1 = null;
                        retakeRecord.RetakePointsLevel2 = null;
                        retakeRecord.RetakePointsLevel3 = null;
                    }
                }
            }

            _dbContext.SaveChanges();
            
            DataSavedEvent?.Invoke(this, EventArgs.Empty);
            this.Close();
        }

        private void ShowError(int taskNum, int level, double? max, string attempt)
        {
            MessageBox.Show($"Błąd w zadaniu {taskNum} ({attempt}): wpisano więcej punktów niż przewidziano (Max: {max}).", 
                            "Ajajajaj! Przekroczono limit...", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close(); 
        }

        private void ScoreTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right)
            {
                FocusNavigationDirection direction = FocusNavigationDirection.Next;
                switch (e.Key)
                {
                    case Key.Up: direction = FocusNavigationDirection.Up; break;
                    case Key.Down: direction = FocusNavigationDirection.Down; break;
                    case Key.Left: direction = FocusNavigationDirection.Previous; break; 
                    case Key.Right: direction = FocusNavigationDirection.Next; break; 
                }

                if (sender is TextBox textBox)
                {
                    textBox.MoveFocus(new TraversalRequest(direction));
                    e.Handled = true; 
                }
            }
        }
        private void DataGridCell_GotFocus(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is DataGridCell cell)
            {
                var textBox = FindVisualChild<TextBox>(cell);
                if (textBox != null)
                {
                    textBox.Focus();
                    textBox.SelectAll(); 
                }
            }
        }

        private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var found = FindVisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }
    }

    public class TaskScoreViewModel : INotifyPropertyChanged
    {
        public int? BaseTaskId { get; set; }
        public int? RetakeTaskId { get; set; }
        public int TaskNumber { get; set; }

        public Visibility BaseVisibility => BaseTaskId.HasValue ? Visibility.Visible : Visibility.Collapsed;
        public Visibility BaseDashVisibility => BaseTaskId.HasValue ? Visibility.Collapsed : Visibility.Visible;
        
        public Visibility RetakeVisibility => RetakeTaskId.HasValue ? Visibility.Visible : Visibility.Collapsed;
        public Visibility RetakeDashVisibility => RetakeTaskId.HasValue ? Visibility.Collapsed : Visibility.Visible;

        private double? _maxL1;
        public double? MaxL1 { get => _maxL1; set { _maxL1 = value; OnPropertyChanged(); } }
        private double? _maxL2;
        public double? MaxL2 { get => _maxL2; set { _maxL2 = value; OnPropertyChanged(); } }
        private double? _maxL3;
        public double? MaxL3 { get => _maxL3; set { _maxL3 = value; OnPropertyChanged(); } }
        
        private double? _retakeMaxL1;
        public double? RetakeMaxL1 { get => _retakeMaxL1; set { _retakeMaxL1 = value; OnPropertyChanged(); } }
        private double? _retakeMaxL2;
        public double? RetakeMaxL2 { get => _retakeMaxL2; set { _retakeMaxL2 = value; OnPropertyChanged(); } }
        private double? _retakeMaxL3;
        public double? RetakeMaxL3 { get => _retakeMaxL3; set { _retakeMaxL3 = value; OnPropertyChanged(); } }

        private double? _scoreL1;
        public double? ScoreL1 
        { 
            get => _scoreL1; 
            set 
            { 
                if (value.HasValue && value.Value > (MaxL1 ?? 0)) { _scoreL1 = null; } 
                else { _scoreL1 = value; if(value != null) { _scoreL2 = null; _scoreL3 = null; OnBothChangedT1(); } } 
                OnPropertyChanged(); 
            } 
        }
        
        private double? _scoreL2;
        public double? ScoreL2 
        { 
            get => _scoreL2; 
            set 
            { 
                if (value.HasValue && value.Value > (MaxL2 ?? 0)) { _scoreL2 = null; } 
                else { _scoreL2 = value; if(value != null) { _scoreL1 = null; _scoreL3 = null; OnBothChangedT1(); } } 
                OnPropertyChanged(); 
            } 
        }
        
        private double? _scoreL3;
        public double? ScoreL3 
        { 
            get => _scoreL3; 
            set 
            { 
                if (value.HasValue && value.Value > (MaxL3 ?? 0)) { _scoreL3 = null; } 
                else { _scoreL3 = value; if(value != null) { _scoreL1 = null; _scoreL2 = null; OnBothChangedT1(); } } 
                OnPropertyChanged(); 
            } 
        }

        private void OnBothChangedT1() { OnPropertyChanged(nameof(ScoreL1)); OnPropertyChanged(nameof(ScoreL2)); OnPropertyChanged(nameof(ScoreL3)); }

        private double? _retakeScoreL1;
        public double? RetakeScoreL1 
        { 
            get => _retakeScoreL1; 
            set 
            { 
                if (value.HasValue && value.Value > (RetakeMaxL1 ?? 0)) { _retakeScoreL1 = null; } 
                else { _retakeScoreL1 = value; if(value != null) { _retakeScoreL2 = null; _retakeScoreL3 = null; OnBothChangedRetake(); } } 
                OnPropertyChanged(); 
            } 
        }
        
        private double? _retakeScoreL2;
        public double? RetakeScoreL2 
        { 
            get => _retakeScoreL2; 
            set 
            { 
                if (value.HasValue && value.Value > (RetakeMaxL2 ?? 0)) { _retakeScoreL2 = null; } 
                else { _retakeScoreL2 = value; if(value != null) { _retakeScoreL1 = null; _retakeScoreL3 = null; OnBothChangedRetake(); } } 
                OnPropertyChanged(); 
            } 
        }
        
        private double? _retakeScoreL3;
        public double? RetakeScoreL3 
        { 
            get => _retakeScoreL3; 
            set 
            { 
                if (value.HasValue && value.Value > (RetakeMaxL3 ?? 0)) { _retakeScoreL3 = null; } 
                else { _retakeScoreL3 = value; if(value != null) { _retakeScoreL1 = null; _retakeScoreL2 = null; OnBothChangedRetake(); } } 
                OnPropertyChanged(); 
            } 
        }

        private void OnBothChangedRetake() { OnPropertyChanged(nameof(RetakeScoreL1)); OnPropertyChanged(nameof(RetakeScoreL2)); OnPropertyChanged(nameof(RetakeScoreL3)); }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}