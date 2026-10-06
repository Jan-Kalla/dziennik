using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GradebookApp.ViewModels;
using GradebookApp.Services;

namespace GradebookApp
{
    public partial class StudentWorkDetailsWindow : Window
    {
        private readonly GradebookDataService _dataService;
        private int _studentId, _workId;
        private WrittenWork? _currentWork;
        private bool _isRetakeActive = false, _isLoaded = false;
        
        private List<StudentTaskScore> _existingScores = new List<StudentTaskScore>();
        private static string _lastUsedGroup = string.Empty, _lastUsedRetakeGroup = string.Empty;

        public event EventHandler? DataSavedEvent;
        public ObservableCollection<TaskScoreViewModel> ScoresCollection { get; set; } = new ObservableCollection<TaskScoreViewModel>();

        public StudentWorkDetailsWindow(int studentId, int workId)
        {
            InitializeComponent();
            _dataService = new GradebookDataService();
            _studentId = studentId; _workId = workId;
            LoadData(); _isLoaded = true;
        }

        private void LoadData()
        {
            var student = _dataService.GetStudentById(_studentId);
            _currentWork = _dataService.GetWorkByIdWithTasks(_workId);
            _existingScores = _dataService.GetStudentScores(_studentId);
            var workRecord = _dataService.GetWorkRecord(_studentId, _workId);

            if (student == null || _currentWork == null) return;
            ScoresDataGrid.ItemsSource = ScoresCollection;

            if (workRecord != null)
            {
                CustomDateWrittenPicker.SelectedDate = workRecord.CustomDateWritten; CustomDateEnteredPicker.SelectedDate = workRecord.CustomDateEntered; AbsentCheckBox.IsChecked = workRecord.IsAbsent;
                RetakeDeadlinePicker.SelectedDate = workRecord.RetakeDeadline; RetakeDateWrittenPicker.SelectedDate = workRecord.RetakeDateWritten; RetakeDateEnteredPicker.SelectedDate = workRecord.RetakeDateEntered; _isRetakeActive = workRecord.IsRetakeActive;
            }

            if (_currentWork.HasGroups)
            {
                GroupTextBox.Visibility = RetakeGroupTextBox.Visibility = Visibility.Collapsed; GroupComboBox.Visibility = RetakeGroupComboBox.Visibility = Visibility.Visible;
                var groups = _currentWork.Tasks.Where(t => !string.IsNullOrEmpty(t.GroupName)).Select(t => t.GroupName!).Distinct().OrderBy(g => g).ToList();
                GroupComboBox.ItemsSource = RetakeGroupComboBox.ItemsSource = groups;

                string targetGroup = workRecord != null && groups.Contains(workRecord.Group ?? "") ? workRecord.Group! : (groups.Contains(_lastUsedGroup) ? _lastUsedGroup : groups.FirstOrDefault() ?? "");
                GroupComboBox.SelectedItem = targetGroup;
                
                if (workRecord != null && groups.Contains(workRecord.RetakeGroup ?? "")) RetakeGroupComboBox.SelectedItem = workRecord.RetakeGroup;
                else if (groups.Count > 1 && !string.IsNullOrEmpty(targetGroup)) RetakeGroupComboBox.SelectedItem = groups[(groups.IndexOf(targetGroup) + 1) % groups.Count];
            }
            else
            {
                GroupTextBox.Visibility = RetakeGroupTextBox.Visibility = Visibility.Visible; GroupComboBox.Visibility = RetakeGroupComboBox.Visibility = Visibility.Collapsed;
                GroupTextBox.Text = string.IsNullOrEmpty(workRecord?.Group) ? _lastUsedGroup : workRecord.Group; RetakeGroupTextBox.Text = string.IsNullOrEmpty(workRecord?.RetakeGroup) ? _lastUsedRetakeGroup : workRecord.RetakeGroup;
            }

            RebuildScoresCollection(); UpdateDeadlinePrompt(); ApplyRetakeVisibility(); 

            WorkTypeAndTitleText.Text = $"{_currentWork.WorkType.ToUpper()}: {_currentWork.Title}";
            ClassGroupText.Text = $"Klasa: {student.SchoolClass?.Name} | Uczeń: {student.FullName}";
            DateTime? actualDateWritten = workRecord?.CustomDateWritten ?? _currentWork.DateWritten;
            DateTime? actualDateEntered = workRecord?.CustomDateEntered ?? _currentWork.DateEntered;
            DatesText.Text = $"Napisano: {actualDateWritten?.ToString("dd.MM.yyyy") ?? "Brak"} | Wpisano: {actualDateEntered?.ToString("dd.MM.yyyy") ?? "Brak"}";

            if (_currentWork.WorkType.ToLower() == "aktywność")
            {
                ScoreControlsPanel.Visibility = DetailsGrid.Visibility = RetakePromptPanel.Visibility = RetakeEditPanel.Visibility = Visibility.Collapsed; 
                ActivityScorePanel.Visibility = Visibility.Visible; ActivityMaxPointsText.Text = $"/ {_currentWork.MaxFinalPoints} pkt";
                var vm = ScoresCollection.FirstOrDefault(); if (vm != null) ActivityScoreTextBox.Text = vm.ScoreL3?.ToString() ?? "";
            }
            RecalculateTotalScore(); 
        }

        private void GroupComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_currentWork == null || !_currentWork.HasGroups || !_isLoaded) return;
            string? selGroup = GroupComboBox.SelectedItem as string;
            var groups = _currentWork.Tasks.Where(t => !string.IsNullOrEmpty(t.GroupName)).Select(t => t.GroupName!).Distinct().OrderBy(g => g).ToList();
            if (selGroup != null && groups.Count > 1) RetakeGroupComboBox.SelectedItem = groups[(groups.IndexOf(selGroup) + 1) % groups.Count];
            RebuildScoresCollection();
        }

        private void RetakeGroupComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_isLoaded && _currentWork != null && _currentWork.HasGroups && _isRetakeActive) RebuildScoresCollection(); }

        private void RebuildScoresCollection()
        {
            if (_currentWork == null) return;
            ScoresCollection.Clear();

            string baseGroup = GroupComboBox.Visibility == Visibility.Visible ? (GroupComboBox.SelectedItem as string ?? "") : GroupTextBox.Text.Trim();
            string retakeGroup = RetakeGroupComboBox.Visibility == Visibility.Visible ? (RetakeGroupComboBox.SelectedItem as string ?? "") : RetakeGroupTextBox.Text.Trim();

            var baseTasks = _currentWork.HasGroups ? _currentWork.Tasks.Where(t => t.GroupName == baseGroup).ToList() : _currentWork.Tasks.ToList();
            var retakeTasks = _currentWork.HasGroups ? _currentWork.Tasks.Where(t => t.GroupName == retakeGroup).ToList() : _currentWork.Tasks.ToList();

            int maxTaskNum = Math.Max(baseTasks.Any() ? baseTasks.Max(t => t.TaskNumber) : 0, retakeTasks.Any() ? retakeTasks.Max(t => t.TaskNumber) : 0);

            for (int i = 1; i <= maxTaskNum; i++)
            {
                var bTask = baseTasks.FirstOrDefault(t => t.TaskNumber == i); var rTask = retakeTasks.FirstOrDefault(t => t.TaskNumber == i);
                var vm = new TaskScoreViewModel { TaskNumber = i };

                if (bTask != null)
                {
                    vm.BaseTaskId = bTask.Id; vm.MaxL1 = bTask.MaxPointsLevel1; vm.MaxL2 = bTask.MaxPointsLevel2; vm.MaxL3 = bTask.MaxPointsLevel3;
                    var bScore = _existingScores.FirstOrDefault(s => s.WrittenWorkTaskId == bTask.Id);
                    if (bScore != null) { vm.ScoreL1 = bScore.PointsLevel1; vm.ScoreL2 = bScore.PointsLevel2; vm.ScoreL3 = bScore.PointsLevel3; }
                }

                if (rTask != null)
                {
                    vm.RetakeTaskId = rTask.Id; vm.RetakeMaxL1 = rTask.MaxPointsLevel1; vm.RetakeMaxL2 = rTask.MaxPointsLevel2; vm.RetakeMaxL3 = rTask.MaxPointsLevel3;
                    var rScore = _existingScores.FirstOrDefault(s => s.WrittenWorkTaskId == rTask.Id);
                    if (rScore != null) { vm.RetakeScoreL1 = rScore.RetakePointsLevel1; vm.RetakeScoreL2 = rScore.RetakePointsLevel2; vm.RetakeScoreL3 = rScore.RetakePointsLevel3; }
                }

                vm.PropertyChanged += (s, e) => RecalculateTotalScore(); ScoresCollection.Add(vm);
            }
            RecalculateTotalScore();
        }

        private void UpdateDeadlinePrompt()
        {
            DateTime? baseDate = CustomDateEnteredPicker.SelectedDate ?? _currentWork?.DateEntered;
            if (baseDate.HasValue) { if (_isLoaded) RetakeDeadlinePicker.SelectedDate = baseDate.Value.AddDays(14); RetakeDeadlinePromptText.Text = $"Termin na poprawę: do {(RetakeDeadlinePicker.SelectedDate ?? baseDate.Value.AddDays(14)):dd.MM.yyyy}"; }
            else { RetakeDeadlinePromptText.Text = "Termin na poprawę: (Brak daty wpisania)"; if (_isLoaded) RetakeDeadlinePicker.SelectedDate = null; }
        }

        private void CustomDateEnteredPicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e) => UpdateDeadlinePrompt();

        private void ActivateRetakeButton_Click(object sender, RoutedEventArgs e) { _isRetakeActive = true; ApplyRetakeVisibility(); RecalculateTotalScore(); }
        private void DeactivateRetakeButton_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Usunąć poprawę i wyczyścić wpisane oceny z panelu?", "Potwierdzenie", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                _isRetakeActive = false; foreach (var vm in ScoresCollection) { vm.RetakeScoreL1 = vm.RetakeScoreL2 = vm.RetakeScoreL3 = null; }
                if (_currentWork != null && _currentWork.HasGroups) { var g = _currentWork.Tasks.Where(t => !string.IsNullOrEmpty(t.GroupName)).Select(t => t.GroupName!).Distinct().OrderBy(g => g).ToList(); if (g.Count > 1) RetakeGroupComboBox.SelectedItem = g[(g.IndexOf(GroupComboBox.SelectedItem as string ?? "") + 1) % g.Count]; } else RetakeGroupTextBox.Text = string.Empty;
                RetakeDateWrittenPicker.SelectedDate = RetakeDateEnteredPicker.SelectedDate = null; ApplyRetakeVisibility(); RecalculateTotalScore();
            }
        }

        private void ApplyRetakeVisibility()
        {
            var converter = new System.Windows.Media.BrushConverter();
            RetakePromptPanel.Visibility = _isRetakeActive ? Visibility.Collapsed : Visibility.Visible;
            RetakeEditPanel.Visibility = RetakeCol1.Visibility = RetakeCol2.Visibility = RetakeCol3.Visibility = _isRetakeActive ? Visibility.Visible : Visibility.Collapsed;
            FillMaxPointsButton.Background = (System.Windows.Media.Brush)converter.ConvertFromString(_isRetakeActive ? "#2E4A2E" : "#3E3E42")!;
            FillMaxPointsButton.BorderBrush = (System.Windows.Media.Brush)converter.ConvertFromString(_isRetakeActive ? "#4CAF50" : "#555555")!;
        }

        private void AbsentCheckBox_Changed(object sender, RoutedEventArgs e) { ScoresDataGrid.IsEnabled = AbsentCheckBox.IsChecked != true; RecalculateTotalScore(); }
        private void ToggleDetailsButton_Click(object sender, RoutedEventArgs e) { DetailsGrid.Visibility = DetailsGrid.Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed; ToggleDetailsButton.Content = DetailsGrid.Visibility == Visibility.Visible ? "Ukryj szczegóły punktacji ↑" : "Pokaż / Edytuj szczegóły punktacji ↓"; }

        private void FillMaxPoints_Click(object sender, RoutedEventArgs e)
        {
            if (AbsentCheckBox.IsChecked == true) return;
            foreach (var vm in ScoresCollection)
            {
                if (_isRetakeActive && vm.RetakeTaskId.HasValue) { if (vm.RetakeMaxL3 > 0) vm.RetakeScoreL3 = vm.RetakeMaxL3; else if (vm.RetakeMaxL2 > 0) vm.RetakeScoreL2 = vm.RetakeMaxL2; else if (vm.RetakeMaxL1 > 0) vm.RetakeScoreL1 = vm.RetakeMaxL1; }
                else if (!_isRetakeActive && vm.BaseTaskId.HasValue) { if (vm.MaxL3 > 0) vm.ScoreL3 = vm.MaxL3; else if (vm.MaxL2 > 0) vm.ScoreL2 = vm.MaxL2; else if (vm.MaxL1 > 0) vm.ScoreL1 = vm.MaxL1; }
            }
        }

        private void ActivityScoreTextBox_TextChanged(object sender, TextChangedEventArgs e) { if (_currentWork?.WorkType.ToLower() == "aktywność" && ScoresCollection.FirstOrDefault() is TaskScoreViewModel vm) vm.ScoreL3 = double.TryParse(ActivityScoreTextBox.Text.Trim(), out double s) ? s : null; }

        private void RecalculateTotalScore()
        {
            if (AbsentCheckBox.IsChecked == true) { RetakePromptPanel.IsEnabled = false; TotalResultText.Text = "NB"; TotalResultText.Foreground = System.Windows.Media.Brushes.IndianRed; PercentageResultText.Text = BasePanelScoreText.Text = RetakePanelScoreText.Text = string.Empty; return; }
            RetakePromptPanel.IsEnabled = true; double M = _currentWork?.MaxFinalPoints ?? 0;
            
            double totalBase = 0, totalRetake = 0;
            var baseVms = ScoresCollection.Where(vm => vm.BaseTaskId.HasValue).ToList(); if (baseVms.Count > 0 && M > 0) totalBase = Math.Round(M * (0.54 * (baseVms.Sum(v => (v.ScoreL1 ?? 0) / (v.MaxL1 > 0 ? v.MaxL1.Value : 1)) / baseVms.Count) + 0.9 * (baseVms.Sum(v => (v.ScoreL2 ?? 0) / (v.MaxL2 > 0 ? v.MaxL2.Value : 1)) / baseVms.Count) + (baseVms.Sum(v => (v.ScoreL3 ?? 0) / (v.MaxL3 > 0 ? v.MaxL3.Value : 1)) / baseVms.Count)), 0, MidpointRounding.AwayFromZero);
            var retVms = ScoresCollection.Where(vm => vm.RetakeTaskId.HasValue).ToList(); if (retVms.Count > 0 && M > 0) totalRetake = Math.Round(M * (0.54 * (retVms.Sum(v => (v.RetakeScoreL1 ?? 0) / (v.RetakeMaxL1 > 0 ? v.RetakeMaxL1.Value : 1)) / retVms.Count) + 0.9 * (retVms.Sum(v => (v.RetakeScoreL2 ?? 0) / (v.RetakeMaxL2 > 0 ? v.RetakeMaxL2.Value : 1)) / retVms.Count) + (retVms.Sum(v => (v.RetakeScoreL3 ?? 0) / (v.RetakeMaxL3 > 0 ? v.RetakeMaxL3.Value : 1)) / retVms.Count)), 0, MidpointRounding.AwayFromZero);

            bool hb = baseVms.Any(v => v.ScoreL1 != null || v.ScoreL2 != null || v.ScoreL3 != null), hr = retVms.Any(v => v.RetakeScoreL1 != null || v.RetakeScoreL2 != null || v.RetakeScoreL3 != null);
            BasePanelScoreText.Text = hb ? $"{totalBase} / {M} pkt ({Math.Floor(M > 0 ? (totalBase / M) * 100 : 0)}%)" : "Brak ocen"; RetakePanelScoreText.Text = hr ? $"{totalRetake} / {M} pkt ({Math.Floor(M > 0 ? (totalRetake / M) * 100 : 0)}%)" : "Brak ocen";

            if (_isRetakeActive && hr && totalRetake > totalBase) { TotalResultText.Text = $"{totalRetake} / {M} pkt (popr.)"; TotalResultText.Foreground = System.Windows.Media.Brushes.LightGreen; PercentageResultText.Text = M > 0 ? $"{Math.Floor((totalRetake / M) * 100)}%" : "0%"; }
            else if (hb) { TotalResultText.Text = $"{totalBase} / {M} pkt"; TotalResultText.Foreground = System.Windows.Media.Brushes.LightSkyBlue; PercentageResultText.Text = M > 0 ? $"{Math.Floor((totalBase / M) * 100)}%" : "0%"; }
            else { TotalResultText.Text = "Brak ocen"; TotalResultText.Foreground = System.Windows.Media.Brushes.LightSkyBlue; PercentageResultText.Text = string.Empty; }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Keyboard.ClearFocus();
            if (_currentWork != null && _currentWork.HasGroups && GroupComboBox.SelectedItem == null && AbsentCheckBox.IsChecked == false) { MessageBox.Show("Wybierz grupę.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (AbsentCheckBox.IsChecked == false && ScoresCollection.Any(v => v.BaseTaskId.HasValue && (v.ScoreL1 != null || v.ScoreL2 != null || v.ScoreL3 != null)) && !ScoresCollection.Where(v => v.BaseTaskId.HasValue).All(v => v.ScoreL1 != null || v.ScoreL2 != null || v.ScoreL3 != null)) { MessageBox.Show("Uzupełnij bazowe oceny we wszystkich zadaniach.", "Brakujące wpisy", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (_isRetakeActive && ScoresCollection.Any(v => v.RetakeTaskId.HasValue && (v.RetakeScoreL1 != null || v.RetakeScoreL2 != null || v.RetakeScoreL3 != null)) && !ScoresCollection.Where(v => v.RetakeTaskId.HasValue).All(v => v.RetakeScoreL1 != null || v.RetakeScoreL2 != null || v.RetakeScoreL3 != null)) { MessageBox.Show("Uzupełnij poprawione oceny we wszystkich zadaniach.", "Brakujące wpisy", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            var record = new StudentWorkRecord
            {
                StudentId = _studentId, WrittenWorkId = _workId, Group = _currentWork != null && _currentWork.HasGroups ? (GroupComboBox.SelectedItem as string ?? "") : GroupTextBox.Text.Trim(),
                CustomDateWritten = CustomDateWrittenPicker.SelectedDate, CustomDateEntered = CustomDateEnteredPicker.SelectedDate, IsAbsent = AbsentCheckBox.IsChecked == true,
                RetakeGroup = _isRetakeActive ? (_currentWork != null && _currentWork.HasGroups ? (RetakeGroupComboBox.SelectedItem as string ?? "") : RetakeGroupTextBox.Text.Trim()) : string.Empty,
                IsRetakeActive = _isRetakeActive, RetakeDeadline = RetakeDeadlinePicker.SelectedDate, RetakeDateWritten = _isRetakeActive ? RetakeDateWrittenPicker.SelectedDate : null, RetakeDateEntered = _isRetakeActive ? RetakeDateEnteredPicker.SelectedDate : null
            };

            var scoresToSave = new List<StudentTaskScore>();
            foreach (var vm in ScoresCollection)
            {
                if (vm.BaseTaskId.HasValue) scoresToSave.Add(new StudentTaskScore { StudentId = _studentId, WrittenWorkTaskId = vm.BaseTaskId.Value, PointsLevel1 = AbsentCheckBox.IsChecked == true ? 0 : vm.ScoreL1, PointsLevel2 = AbsentCheckBox.IsChecked == true ? 0 : vm.ScoreL2, PointsLevel3 = AbsentCheckBox.IsChecked == true ? 0 : vm.ScoreL3 });
                if (vm.RetakeTaskId.HasValue && _isRetakeActive)
                {
                    var existing = scoresToSave.FirstOrDefault(s => s.WrittenWorkTaskId == vm.RetakeTaskId.Value);
                    if (existing != null) { existing.RetakePointsLevel1 = vm.RetakeScoreL1; existing.RetakePointsLevel2 = vm.RetakeScoreL2; existing.RetakePointsLevel3 = vm.RetakeScoreL3; }
                    else scoresToSave.Add(new StudentTaskScore { StudentId = _studentId, WrittenWorkTaskId = vm.RetakeTaskId.Value, RetakePointsLevel1 = vm.RetakeScoreL1, RetakePointsLevel2 = vm.RetakeScoreL2, RetakePointsLevel3 = vm.RetakeScoreL3 });
                }
            }

            _lastUsedGroup = record.Group; if (_isRetakeActive) _lastUsedRetakeGroup = record.RetakeGroup;
            _dataService.SaveStudentWorkRecordAndScores(record, scoresToSave, _isRetakeActive);
            
            DataSavedEvent?.Invoke(this, EventArgs.Empty); Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
        private void ScoreTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right)
            {
                if (sender is TextBox textBox)
                {
                    // Omijamy konflikt, pozwalając na edycję tekstu wewnątrz komórki
                    if ((e.Key == Key.Left && textBox.CaretIndex > 0) || 
                        (e.Key == Key.Right && textBox.CaretIndex < textBox.Text.Length))
                        return;

                    e.Handled = true;
                    FocusNavigationDirection direction = e.Key == Key.Up ? FocusNavigationDirection.Up : 
                                                        e.Key == Key.Down ? FocusNavigationDirection.Down : 
                                                        e.Key == Key.Left ? FocusNavigationDirection.Left : FocusNavigationDirection.Right;
                    
                    var request = new TraversalRequest(direction);
                    
                    // System z góry szuka najbliższego pola tekstowego i wymusza na nim fokus
                    if (textBox.PredictFocus(direction) is TextBox predicted)
                    {
                        predicted.Focus();
                        predicted.SelectAll();
                    }
                    else
                    {
                        textBox.MoveFocus(request);
                    }
                }
            }
        }
        private void DataGridCell_GotFocus(object sender, RoutedEventArgs e) { if (e.OriginalSource is DataGridCell c) { var t = FindVisualChild<TextBox>(c); if (t != null) { t.Focus(); t.SelectAll(); } } }
        private T? FindVisualChild<T>(DependencyObject p) where T : DependencyObject { for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(p); i++) { var c = System.Windows.Media.VisualTreeHelper.GetChild(p, i); if (c is T t) return t; var f = FindVisualChild<T>(c); if (f != null) return f; } return null; }
        protected override void OnClosed(EventArgs e) { _dataService.Dispose(); base.OnClosed(e); }
    }
}