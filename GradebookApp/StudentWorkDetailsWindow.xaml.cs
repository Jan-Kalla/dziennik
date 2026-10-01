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

            // ====================================================================
            // INTELIGENTNY PRZEŁĄCZNIK WIDOKÓW I ZABEZPIECZEŃ
            // ====================================================================
            if (_currentWork.HasGroups)
            {
                // Praca skomplikowana z różnymi limitami: blokujemy ręczne wpisywanie
                GroupTextBox.Visibility = Visibility.Collapsed;
                RetakeGroupTextBox.Visibility = Visibility.Collapsed;
                
                GroupComboBox.Visibility = Visibility.Visible;
                RetakeGroupComboBox.Visibility = Visibility.Visible;

                var groups = _currentWork.Tasks.Where(t => !string.IsNullOrEmpty(t.GroupName)).Select(t => t.GroupName!).Distinct().OrderBy(g => g).ToList();
                GroupComboBox.ItemsSource = groups;
                RetakeGroupComboBox.ItemsSource = groups;

                // Autouzupełnianie na bazie ostatnio wybranej z listy poprawnej grupy
                string targetGroup = workRecord != null && !string.IsNullOrEmpty(workRecord.Group) && groups.Contains(workRecord.Group)
                    ? workRecord.Group
                    : (groups.Contains(_lastUsedGroup) ? _lastUsedGroup : groups.FirstOrDefault() ?? string.Empty);

                GroupComboBox.SelectedItem = targetGroup;
                RetakeGroupComboBox.SelectedItem = workRecord != null && !string.IsNullOrEmpty(workRecord.RetakeGroup) && groups.Contains(workRecord.RetakeGroup) ? workRecord.RetakeGroup : targetGroup;
            }
            else
            {
                // Praca standardowa: swoboda działania
                GroupTextBox.Visibility = Visibility.Visible;
                RetakeGroupTextBox.Visibility = Visibility.Visible;
                
                GroupComboBox.Visibility = Visibility.Collapsed;
                RetakeGroupComboBox.Visibility = Visibility.Collapsed;

                GroupTextBox.Text = string.IsNullOrEmpty(workRecord?.Group) ? _lastUsedGroup : workRecord.Group;
                RetakeGroupTextBox.Text = string.IsNullOrEmpty(workRecord?.RetakeGroup) ? _lastUsedRetakeGroup : workRecord.RetakeGroup;
                
                // Ładujemy wiersze standardowej pracy (wszyscy mają te same zadania)
                RebuildScoresCollectionForBase();
            }

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

        private void RebuildScoresCollectionForBase()
        {
            ScoresCollection.Clear();
            var tasksForBase = _currentWork!.Tasks.OrderBy(t => t.TaskNumber).ToList();
            
            foreach (var task in tasksForBase)
            {
                var vm = new TaskScoreViewModel 
                { 
                    TaskId = task.Id, 
                    TaskNumber = task.TaskNumber, 
                    MaxL1 = task.MaxPointsLevel1, 
                    MaxL2 = task.MaxPointsLevel2, 
                    MaxL3 = task.MaxPointsLevel3 
                };
                
                var scoreRecord = _existingScores.FirstOrDefault(s => s.WrittenWorkTaskId == task.Id);
                vm.ScoreL1 = scoreRecord?.PointsLevel1;
                vm.ScoreL2 = scoreRecord?.PointsLevel2;
                vm.ScoreL3 = scoreRecord?.PointsLevel3;
                vm.RetakeScoreL1 = scoreRecord?.RetakePointsLevel1;
                vm.RetakeScoreL2 = scoreRecord?.RetakePointsLevel2;
                vm.RetakeScoreL3 = scoreRecord?.RetakePointsLevel3;
                
                vm.PropertyChanged += (s, e) => RecalculateTotalScore();
                ScoresCollection.Add(vm);
            }
        }

        private void GroupComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_currentWork == null || !_currentWork.HasGroups) return;
            
            // Sztywne związanie grup. To zapobiega sytuacji, gdzie termin bazowy liczony jest 
            // w limitach Grupy A, a poprawa ucznia w limitach Grupy B w ramach jednego okna.
            RetakeGroupComboBox.SelectedItem = GroupComboBox.SelectedItem; 
            
            UpdateLimitsForSelectedGroup(GroupComboBox.SelectedItem as string);
        }

        private void RetakeGroupComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_currentWork == null || !_currentWork.HasGroups || !_isRetakeActive) return;
            // Opcjonalnie, gdyby nauczyciel chciał nadpisać, tutaj dodajemy logikę. Aktualnie to zablokowane powiązaniem.
        }

        private void UpdateLimitsForSelectedGroup(string? groupName)
        {
            if (string.IsNullOrEmpty(groupName) || _currentWork == null || !_currentWork.HasGroups) return;
            
            ScoresCollection.Clear(); // Usuwamy wszystkie wiersze starej grupy
            
            // Pobieramy z bazy zadania należące TYLKO i wyłącznie do aktualnie wybranej grupy i tworzymy na ich podstawie nowe wiersze
            var groupTasks = _currentWork.Tasks.Where(t => t.GroupName == groupName).OrderBy(t => t.TaskNumber).ToList();

            foreach (var task in groupTasks)
            {
                var vm = new TaskScoreViewModel 
                { 
                    TaskId = task.Id, 
                    TaskNumber = task.TaskNumber, 
                    MaxL1 = task.MaxPointsLevel1, 
                    MaxL2 = task.MaxPointsLevel2, 
                    MaxL3 = task.MaxPointsLevel3 
                };
                
                var scoreRecord = _existingScores.FirstOrDefault(s => s.WrittenWorkTaskId == task.Id);
                vm.ScoreL1 = scoreRecord?.PointsLevel1;
                vm.ScoreL2 = scoreRecord?.PointsLevel2;
                vm.ScoreL3 = scoreRecord?.PointsLevel3;
                vm.RetakeScoreL1 = scoreRecord?.RetakePointsLevel1;
                vm.RetakeScoreL2 = scoreRecord?.RetakePointsLevel2;
                vm.RetakeScoreL3 = scoreRecord?.RetakePointsLevel3;
                
                vm.PropertyChanged += (s, e) => RecalculateTotalScore();
                ScoresCollection.Add(vm);
            }
            RecalculateTotalScore();
        }

        // ====================================================================
        // LOGIKA INTERFEJSU (Daty, panele, NB)
        // ====================================================================

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
                    RetakeGroupComboBox.SelectedItem = GroupComboBox.SelectedItem;
                else 
                    RetakeGroupTextBox.Text = string.Empty; 
                    
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
                    if (vm.MaxL3 > 0) vm.RetakeScoreL3 = vm.MaxL3;
                    else if (vm.MaxL2 > 0) vm.RetakeScoreL2 = vm.MaxL2;
                    else if (vm.MaxL1 > 0) vm.RetakeScoreL1 = vm.MaxL1;
                }
                else
                {
                    if (vm.MaxL3 > 0) vm.ScoreL3 = vm.MaxL3;
                    else if (vm.MaxL2 > 0) vm.ScoreL2 = vm.MaxL2;
                    else if (vm.MaxL1 > 0) vm.ScoreL1 = vm.MaxL1;
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

        // ====================================================================
        // DYNAMICZNY KALKULATOR PSO (Liczy w locie, w pamięci)
        // ====================================================================
        private double CalculateScoreFromViewModels(double M, bool isRetake)
        {
            if (M <= 0 || ScoresCollection.Count == 0) return 0;
            
            int n = ScoresCollection.Count; 
            double sumQ1 = 0, sumQ2 = 0, sumQ3 = 0;

            foreach (var vm in ScoresCollection)
            {
                double p1 = isRetake ? (vm.RetakeScoreL1 ?? 0) : (vm.ScoreL1 ?? 0);
                double p2 = isRetake ? (vm.RetakeScoreL2 ?? 0) : (vm.ScoreL2 ?? 0);
                double p3 = isRetake ? (vm.RetakeScoreL3 ?? 0) : (vm.ScoreL3 ?? 0);

                double P1 = vm.MaxL1 ?? 0;
                double P2 = vm.MaxL2 ?? 0;
                double P3 = vm.MaxL3 ?? 0;

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

            bool hasBaseScores = ScoresCollection.Any(vm => vm.ScoreL1.HasValue || vm.ScoreL2.HasValue || vm.ScoreL3.HasValue);
            bool hasRetakeScores = ScoresCollection.Any(vm => vm.RetakeScoreL1.HasValue || vm.RetakeScoreL2.HasValue || vm.RetakeScoreL3.HasValue);

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

        // ====================================================================
        // ZAPISYWANIE DANYCH (WALIDACJE I PUSZCZANIE DO BAZY)
        // ====================================================================
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Keyboard.ClearFocus();

            if (_currentWork != null && _currentWork.HasGroups && GroupComboBox.SelectedItem == null && AbsentCheckBox.IsChecked == false)
            {
                MessageBox.Show("Wybrana praca korzysta ze zmiennych grup. Musisz wybrać grupę, aby poprawnie zapisać punkty.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool hasAnyBaseScore = ScoresCollection.Any(vm => vm.ScoreL1 != null || vm.ScoreL2 != null || vm.ScoreL3 != null);
            bool hasAllBaseScores = ScoresCollection.All(vm => vm.ScoreL1 != null || vm.ScoreL2 != null || vm.ScoreL3 != null);

            if (AbsentCheckBox.IsChecked == false && hasAnyBaseScore && !hasAllBaseScores)
            {
                MessageBox.Show("Narzędzie bezpieczeństwa PSO: Rozpoczęto wpisywanie ocen dla terminu bazowego. Musisz uzupełnić oceny we wszystkich zadaniach, aby wynik mógł zostać poprawnie uśredniony.", 
                                "Ajajajaj! Brakujące wpisy...", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_isRetakeActive)
            {
                bool hasAnyRetakeScore = ScoresCollection.Any(vm => vm.RetakeScoreL1 != null || vm.RetakeScoreL2 != null || vm.RetakeScoreL3 != null);
                bool hasAllRetakeScores = ScoresCollection.All(vm => vm.RetakeScoreL1 != null || vm.RetakeScoreL2 != null || vm.RetakeScoreL3 != null);

                if (hasAnyRetakeScore && !hasAllRetakeScores)
                {
                    MessageBox.Show("Narzędzie bezpieczeństwa PSO: Rozpoczęto wpisywanie ocen w trybie POPRAWY. Musisz uzupełnić poprawione oceny we wszystkich zadaniach.", 
                                    "Ajajajaj! Brakujące wpisy...", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            foreach (var vm in ScoresCollection)
            {
                if (vm.ScoreL1 > vm.MaxL1) { ShowError(vm.TaskNumber, 1, vm.MaxL1, "Baza"); return; }
                if (vm.ScoreL2 > vm.MaxL2) { ShowError(vm.TaskNumber, 2, vm.MaxL2, "Baza"); return; }
                if (vm.ScoreL3 > vm.MaxL3) { ShowError(vm.TaskNumber, 3, vm.MaxL3, "Baza"); return; }
                
                if (_isRetakeActive)
                {
                    if (vm.RetakeScoreL1 > vm.MaxL1) { ShowError(vm.TaskNumber, 1, vm.MaxL1, "Poprawa"); return; }
                    if (vm.RetakeScoreL2 > vm.MaxL2) { ShowError(vm.TaskNumber, 2, vm.MaxL2, "Poprawa"); return; }
                    if (vm.RetakeScoreL3 > vm.MaxL3) { ShowError(vm.TaskNumber, 3, vm.MaxL3, "Poprawa"); return; }
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
                var record = _dbContext.StudentTaskScores.FirstOrDefault(s => s.StudentId == _studentId && s.WrittenWorkTaskId == vm.TaskId);
                if (record == null)
                {
                    record = new StudentTaskScore { StudentId = _studentId, WrittenWorkTaskId = vm.TaskId };
                    _dbContext.StudentTaskScores.Add(record);
                }
                
                record.PointsLevel1 = AbsentCheckBox.IsChecked == true ? 0 : vm.ScoreL1;
                record.PointsLevel2 = AbsentCheckBox.IsChecked == true ? 0 : vm.ScoreL2;
                record.PointsLevel3 = AbsentCheckBox.IsChecked == true ? 0 : vm.ScoreL3;
                
                record.RetakePointsLevel1 = _isRetakeActive ? vm.RetakeScoreL1 : null;
                record.RetakePointsLevel2 = _isRetakeActive ? vm.RetakeScoreL2 : null;
                record.RetakePointsLevel3 = _isRetakeActive ? vm.RetakeScoreL3 : null;
            }

            _dbContext.SaveChanges();
            
            DataSavedEvent?.Invoke(this, EventArgs.Empty);
            this.Close();
        }

        private void ShowError(int taskNum, int level, double? max, string attempt)
        {
            MessageBox.Show($"Błąd w zadaniu {taskNum} ({attempt}): wpisano więcej punktów niż przewidziano dla poziomu {level} (Max: {max}).", 
                            "Ajajajaj! Przekroczono limit...", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close(); 
        }

        // ====================================================================
        // OPTYMALIZACJA KLAWIATURY (Automatyczny focus na TextBox)
        // ====================================================================

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

    // ====================================================================
    // MODEL DANYCH - INTELIGENTNA WALIDACJA W LOCIE (Max Punkty)
    // ====================================================================
    public class TaskScoreViewModel : INotifyPropertyChanged
    {
        public int TaskId { get; set; }
        public int TaskNumber { get; set; }
        public double? MaxL1 { get; set; }
        public double? MaxL2 { get; set; }
        public double? MaxL3 { get; set; }

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
                if (value.HasValue && value.Value > (MaxL1 ?? 0)) { _retakeScoreL1 = null; } 
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
                if (value.HasValue && value.Value > (MaxL2 ?? 0)) { _retakeScoreL2 = null; } 
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
                if (value.HasValue && value.Value > (MaxL3 ?? 0)) { _retakeScoreL3 = null; } 
                else { _retakeScoreL3 = value; if(value != null) { _retakeScoreL1 = null; _retakeScoreL2 = null; OnBothChangedRetake(); } } 
                OnPropertyChanged(); 
            } 
        }

        private void OnBothChangedRetake() { OnPropertyChanged(nameof(RetakeScoreL1)); OnPropertyChanged(nameof(RetakeScoreL2)); OnPropertyChanged(nameof(RetakeScoreL3)); }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}