using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
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
        
        // Zdarzenie (Event). Strzelamy nim do okna-rodzica, żeby wiedziało, że zapisaliśmy 
        // oceny i trzeba odświeżyć dużą tabelę.
        public event EventHandler? DataSavedEvent;

        // BARDZO WAŻNE: Tu trzymamy punkty z tabelki. To NIE JEST baza danych. 
        // To jest pamięć RAM programu. Trzymamy to tu, dopóki użytkownik nie kliknie "Zapisz".
        public ObservableCollection<TaskScoreViewModel> ScoresCollection { get; set; } = new ObservableCollection<TaskScoreViewModel>();

        public StudentWorkDetailsWindow(int studentId, int workId)
        {
            InitializeComponent();
            _dbContext = new AppDbContext();
            _studentId = studentId;
            _workId = workId;

            LoadData(); 
        }

        private void LoadData()
        {
            // Zasysamy potrzebne graty z bazy danych
            var student = _dbContext.Students.Include(s => s.SchoolClass).FirstOrDefault(s => s.Id == _studentId);
            _currentWork = _dbContext.WrittenWorks.Include(w => w.Tasks).FirstOrDefault(w => w.Id == _workId);
            var existingScores = _dbContext.StudentTaskScores.Where(s => s.StudentId == _studentId).ToList();
            var workRecord = _dbContext.StudentWorkRecords.FirstOrDefault(r => r.StudentId == _studentId && r.WrittenWorkId == _workId);

            if (student == null || _currentWork == null) return;

            // Jak uczeń ma już przypisane swoje daty albo NB, to wrzucamy to do okienek
            if (workRecord != null)
            {
                GroupTextBox.Text = workRecord.Group;
                CustomDateWrittenPicker.SelectedDate = workRecord.CustomDateWritten;
                CustomDateEnteredPicker.SelectedDate = workRecord.CustomDateEntered;
                AbsentCheckBox.IsChecked = workRecord.IsAbsent;
                
                RetakeDeadlinePicker.SelectedDate = workRecord.RetakeDeadline;
                RetakeDateWrittenPicker.SelectedDate = workRecord.RetakeDateWritten;
                RetakeDateEnteredPicker.SelectedDate = workRecord.RetakeDateEntered;
                _isRetakeActive = workRecord.IsRetakeActive;
            }

            UpdateDeadlinePrompt(); // Przelicza termin poprawy (zawsze 14 dni od wpisania bazy)
            ApplyRetakeVisibility(); // Chowa albo pokazuje panel poprawy

            WorkTypeAndTitleText.Text = $"{_currentWork.WorkType.ToUpper()}: {_currentWork.Title}";
            ClassGroupText.Text = $"Klasa: {student.SchoolClass.Name} | Uczeń: {student.FullName}";
            
            string defaultWritten = _currentWork.DateWritten?.ToString("dd.MM.yyyy") ?? "Brak";
            string defaultEntered = _currentWork.DateEntered?.ToString("dd.MM.yyyy") ?? "Brak";
            DatesText.Text = $"Terminy całej klasy - Napisano: {defaultWritten} | Wpisano: {defaultEntered}";

            // Mapujemy surowe dane z bazy na nasze obiekty ViewModel, z którymi umie gadać tabela WPF.
            foreach (var task in _currentWork.Tasks.OrderBy(t => t.TaskNumber))
            {
                var scoreRecord = existingScores.FirstOrDefault(s => s.WrittenWorkTaskId == task.Id);
                
                var vm = new TaskScoreViewModel
                {
                    TaskId = task.Id,
                    TaskNumber = task.TaskNumber,
                    MaxL1 = task.MaxPointsLevel1,
                    MaxL2 = task.MaxPointsLevel2,
                    MaxL3 = task.MaxPointsLevel3,
                    ScoreL1 = scoreRecord?.PointsLevel1,
                    ScoreL2 = scoreRecord?.PointsLevel2,
                    ScoreL3 = scoreRecord?.PointsLevel3,
                    RetakeScoreL1 = scoreRecord?.RetakePointsLevel1,
                    RetakeScoreL2 = scoreRecord?.RetakePointsLevel2,
                    RetakeScoreL3 = scoreRecord?.RetakePointsLevel3
                };
                
                // podpinamy się pod każdy wiersz. Jak nauczyciel wklepie liczbę z klawiatury,
                // to od razu odpali się RecalculateTotalScore().
                vm.PropertyChanged += (s, e) => RecalculateTotalScore();
                ScoresCollection.Add(vm);
            }

            ScoresDataGrid.ItemsSource = ScoresCollection;
            RecalculateTotalScore(); // Odpalamy żeby załadował się wynik na starcie
        }

        // ==============================================================================
        // PIERDOŁY Z INTERFEJSU (Ukrywanie paneli, checkboxy itp.)
        // ==============================================================================

        private void UpdateDeadlinePrompt()
        {
            DateTime? baseDate = CustomDateEnteredPicker.SelectedDate ?? _currentWork?.DateEntered;
            if (baseDate.HasValue)
            {
                DateTime deadline = baseDate.Value.AddDays(14);
                RetakeDeadlinePromptText.Text = $"Termin na poprawę: do {deadline:dd.MM.yyyy}";
                if (RetakeDeadlinePicker.SelectedDate == null)
                    RetakeDeadlinePicker.SelectedDate = deadline; 
            }
            else
            {
                RetakeDeadlinePromptText.Text = "Termin na poprawę: (Brak daty wpisania)";
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
            var result = MessageBox.Show("Czy na pewno chcesz usunąć poprawę? Spowoduje to skasowanie ewentualnych punktów i dat wpisanych w tym panelu podczas zapisu.", 
                                         "Potwierdzenie", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                _isRetakeActive = false;
                
                // Jak facet kasuje poprawę, to czyścimy z pamięci wszystko co wpisał w te kolumny
                foreach (var vm in ScoresCollection)
                {
                    vm.RetakeScoreL1 = null; vm.RetakeScoreL2 = null; vm.RetakeScoreL3 = null;
                }
                RetakeDateWrittenPicker.SelectedDate = null;
                RetakeDateEnteredPicker.SelectedDate = null;

                ApplyRetakeVisibility();
                RecalculateTotalScore();
            }
        }

        private void ApplyRetakeVisibility()
        {
            // Logika ukrywania okienek i kolumn w tabelce
            if (_isRetakeActive)
            {
                RetakePromptPanel.Visibility = Visibility.Collapsed;
                RetakeEditPanel.Visibility = Visibility.Visible;
                RetakeCol1.Visibility = Visibility.Visible;
                RetakeCol2.Visibility = Visibility.Visible;
                RetakeCol3.Visibility = Visibility.Visible;
            }
            else
            {
                RetakePromptPanel.Visibility = Visibility.Visible;
                RetakeEditPanel.Visibility = Visibility.Collapsed;
                RetakeCol1.Visibility = Visibility.Collapsed;
                RetakeCol2.Visibility = Visibility.Collapsed;
                RetakeCol3.Visibility = Visibility.Collapsed;
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

        // ==============================================================================
        // DYNAMICZNY KALKULATOR PSO (LIVE)
        // Czemu mamy tu sklonowany kod matematyki z AppDbContext? Bo liczymy punkty 
        // W LOCIE (z pamięci UI, zanim zapiszemy do bazy). Facet wpisuje punkty 
        // i od razu chce widzieć jak rośnie mu P.
        // ==============================================================================
        private double CalculateScoreFromViewModels(double M, bool isRetake)
        {
            if (M <= 0 || ScoresCollection.Count == 0) return 0;
            
            int n = ScoresCollection.Count; // Bierzemy liczbę zadań z tabeli
            
            double sumQ1 = 0, sumQ2 = 0, sumQ3 = 0;

            // Lecimy po ViewModelach (naszych danych z pamięci)
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

            // Ten sam wzór co w głównym kalkulatorze. Nic się nie zmienia.
            return Math.Round(M * (0.54 * (sumQ1 / n) + 0.9 * (sumQ2 / n) + (sumQ3 / n)), 2);
        }

        private void RecalculateTotalScore()
        {
            if (AbsentCheckBox.IsChecked == true)
            {
                RetakePromptPanel.IsEnabled = false; 
                TotalResultText.Text = "NB";
                TotalResultText.Foreground = System.Windows.Media.Brushes.IndianRed; // Przyjemniejsza czerwień na ciemne tło
                return;
            }

            RetakePromptPanel.IsEnabled = true;
            double M = _currentWork?.MaxFinalPoints ?? 0;
            
            double totalBase = CalculateScoreFromViewModels(M, false);
            double totalRetake = CalculateScoreFromViewModels(M, true);

            if (_isRetakeActive)
            {
                if (totalRetake > totalBase)
                {
                    TotalResultText.Text = $"{totalRetake} / {M} pkt (popr.)";
                    TotalResultText.Foreground = System.Windows.Media.Brushes.LightGreen; // Widoczna zieleń zamiast ForestGreen
                }
                else
                {
                    TotalResultText.Text = $"{totalBase} / {M} pkt";
                    TotalResultText.Foreground = System.Windows.Media.Brushes.LightSkyBlue; // Jasny niebieski zamiast DarkBlue
                }
            }
            else
            {
                TotalResultText.Text = totalBase > 0 ? $"{totalBase} / {M} pkt" : "Brak ocen";
                TotalResultText.Foreground = System.Windows.Media.Brushes.LightSkyBlue;
            }
        }

        // ==============================================================================
        // ZAPISYWANIE DO BAZY (TWARDY ZAPIS NA DYSKU)
        // ==============================================================================
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // Trick z WPF: jak wpisujesz coś w DataGrid z włączonym ułamkiem (np. 4,5), to focus 
            // cały czas trzyma tę komórkę. To wywołanie niżej w chamski sposób puszcza focus, 
            // zmuszając komórkę do przetworzenia wartości przed zapisem do bazy.
            Keyboard.ClearFocus();

            // Szybki check, czy nauczyciel nie oszalał i nie wpisał 10 pkt w zadaniu na max 5 pkt.
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

            // Krok 1: Zrzut metadanych. Szukamy czy taki wpis już jest. Jak nie, tworzymy nowy.
            var workRecord = _dbContext.StudentWorkRecords.FirstOrDefault(r => r.StudentId == _studentId && r.WrittenWorkId == _workId);
            if (workRecord == null)
            {
                workRecord = new StudentWorkRecord { StudentId = _studentId, WrittenWorkId = _workId };
                _dbContext.StudentWorkRecords.Add(workRecord);
            }
            
            // Przepisanie wartości z okienek do bazy
            workRecord.Group = GroupTextBox.Text.Trim();
            workRecord.CustomDateWritten = CustomDateWrittenPicker.SelectedDate;
            workRecord.CustomDateEntered = CustomDateEnteredPicker.SelectedDate;
            workRecord.IsAbsent = AbsentCheckBox.IsChecked == true;
            
            workRecord.IsRetakeActive = _isRetakeActive;
            workRecord.RetakeDeadline = RetakeDeadlinePicker.SelectedDate;
            workRecord.RetakeDateWritten = _isRetakeActive ? RetakeDateWrittenPicker.SelectedDate : null;
            workRecord.RetakeDateEntered = _isRetakeActive ? RetakeDateEnteredPicker.SelectedDate : null;

            // Krok 2: Zrzut ocen. Lecimy pętlą po całej pamięci operacyjnej (ScoresCollection)
            var existingScores = _dbContext.StudentTaskScores.Where(s => s.StudentId == _studentId).ToList();
            foreach (var vm in ScoresCollection)
            {
                var record = existingScores.FirstOrDefault(s => s.WrittenWorkTaskId == vm.TaskId);
                if (record == null)
                {
                    record = new StudentTaskScore { StudentId = _studentId, WrittenWorkTaskId = vm.TaskId };
                    _dbContext.StudentTaskScores.Add(record);
                }
                
                record.PointsLevel1 = vm.ScoreL1;
                record.PointsLevel2 = vm.ScoreL2;
                record.PointsLevel3 = vm.ScoreL3;
                
                record.RetakePointsLevel1 = _isRetakeActive ? vm.RetakeScoreL1 : null;
                record.RetakePointsLevel2 = _isRetakeActive ? vm.RetakeScoreL2 : null;
                record.RetakePointsLevel3 = _isRetakeActive ? vm.RetakeScoreL3 : null;
            }

            // Ogień. Plik SQLite na dysku się nadpisuje.
            _dbContext.SaveChanges();
            
            // Strzelamy sygnałem na zewnątrz. "Hej, okno rodzica, przeładuj dane z bazy!". I zamykamy siebie.
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
            this.Close(); // X na prawym górnym rogu robi to samo
        }
    }

    // ==============================================================================
    // STRUKTURA DANYCH DLA INTERFEJSU (Tzw. ViewModel)
    // To jest tylko nakładka na tabelę. Nigdy nie zapisuje się jej w bazie danych.
    // Służy tylko temu, żeby WPF umiał poprawnie wygenerować i obsługiwać kolumny.
    // ==============================================================================
    public class TaskScoreViewModel : INotifyPropertyChanged
    {
        public int TaskId { get; set; }
        public int TaskNumber { get; set; }
        public double? MaxL1 { get; set; }
        public double? MaxL2 { get; set; }
        public double? MaxL3 { get; set; }

        // MEGA WAŻNY FRAGMENT (Spełnienie zasady wyłączności poziomów z dokumentacji PSO)
        // Jeśli nauczyciel wpisze wartość np. w komórkę Poziomu 1 (value != null), setter automatycznie
        // wyzeruje zmienne dla Poziomów 2 i 3. Nie da się mieć ocen z kilku poziomów jednocześnie.
        
        private double? _scoreL1;
        public double? ScoreL1 { get => _scoreL1; set { _scoreL1 = value; if(value != null) { _scoreL2 = null; _scoreL3 = null; OnBothChangedT1(); } OnPropertyChanged(); } }
        
        private double? _scoreL2;
        public double? ScoreL2 { get => _scoreL2; set { _scoreL2 = value; if(value != null) { _scoreL1 = null; _scoreL3 = null; OnBothChangedT1(); } OnPropertyChanged(); } }
        
        private double? _scoreL3;
        public double? ScoreL3 { get => _scoreL3; set { _scoreL3 = value; if(value != null) { _scoreL1 = null; _scoreL2 = null; OnBothChangedT1(); } OnPropertyChanged(); } }

        // Mówimy UI, żeby przeładowało komórki, bo właśnie wyzerowaliśmy inne poziomy.
        private void OnBothChangedT1() { OnPropertyChanged(nameof(ScoreL1)); OnPropertyChanged(nameof(ScoreL2)); OnPropertyChanged(nameof(ScoreL3)); }

        // Ten sam mechanizm blokowania wielu kolumn jednocześnie, ale skopiowany dla sekcji poprawy.
        private double? _retakeScoreL1;
        public double? RetakeScoreL1 { get => _retakeScoreL1; set { _retakeScoreL1 = value; if(value != null) { _retakeScoreL2 = null; _retakeScoreL3 = null; OnBothChangedRetake(); } OnPropertyChanged(); } }
        
        private double? _retakeScoreL2;
        public double? RetakeScoreL2 { get => _retakeScoreL2; set { _retakeScoreL2 = value; if(value != null) { _retakeScoreL1 = null; _retakeScoreL3 = null; OnBothChangedRetake(); } OnPropertyChanged(); } }
        
        private double? _retakeScoreL3;
        public double? RetakeScoreL3 { get => _retakeScoreL3; set { _retakeScoreL3 = value; if(value != null) { _retakeScoreL1 = null; _retakeScoreL2 = null; OnBothChangedRetake(); } OnPropertyChanged(); } }

        private void OnBothChangedRetake() { OnPropertyChanged(nameof(RetakeScoreL1)); OnPropertyChanged(nameof(RetakeScoreL2)); OnPropertyChanged(nameof(RetakeScoreL3)); }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}