using System;
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
        // ====================================================================
        // ZMIENNE GŁÓWNE OKNA
        // ====================================================================
        
        // Główne połączenie z naszą bazą SQLite. To okno ma swoją własną, niezależną instancję.
        private AppDbContext _dbContext;
        
        private int _studentId;
        private int _workId;
        
        // Zmienna trzymająca informacje o samej pracy (tytuł, max punktów itp.).
        private WrittenWork? _currentWork;
        
        // Flaga mówiąca nam, czy panel poprawy jest w tym momencie rozwinięty i aktywny.
        private bool _isRetakeActive = false;
        
        // MEGA WAŻNA FLAGA: Mówi nam, czy okno skończyło już pobierać dane z bazy przy uruchamianiu.
        // Dzięki temu nasze funkcje nie nadpisują głupot w kalendarzach, zanim okno do końca się załaduje.
        private bool _isLoaded = false;
        
        // Sygnał (zdarzenie), którym "strzelamy" na zewnątrz do okna głównego, gdy skończymy zapisywać oceny.
        // Mówi on tabeli wyników: "Hej, zmieniłem bazę, odśwież sobie widok!".
        public event EventHandler? DataSavedEvent;
        
        // Specjalna lista WPF. Cokolwiek do niej dodasz lub z niej usuniesz, od razu zaktualizuje się na ekranie.
        // Trzymamy tu wiersze z punktami za zadania (nasz ViewModel), ZANIM wyślemy je do twardej bazy danych.
        public ObservableCollection<TaskScoreViewModel> ScoresCollection { get; set; } = new ObservableCollection<TaskScoreViewModel>();

        public StudentWorkDetailsWindow(int studentId, int workId)
        {
            InitializeComponent();
            _dbContext = new AppDbContext();
            _studentId = studentId;
            _workId = workId;

            // Odpalamy zaciąganie danych
            LoadData(); 
            
            // Okno załadowane, odpalamy flagę. Od teraz kalendarze mogą bezpiecznie reagować na kliknięcia.
            _isLoaded = true;
        }

        // ====================================================================
        // POBIERANIE DANYCH Z BAZY I ŁADOWANIE DO INTERFEJSU
        // ====================================================================
        private void LoadData()
        {
            // Zaciągamy ucznia i pracę. Używamy Include, żeby EF Core od razu dołączył klasę i zadania.
            var student = _dbContext.Students.Include(s => s.SchoolClass).FirstOrDefault(s => s.Id == _studentId);
            _currentWork = _dbContext.WrittenWorks.Include(w => w.Tasks).FirstOrDefault(w => w.Id == _workId);
            
            // Pobieramy wpisane do tej pory punkty oraz ewentualne metadane (np. daty napisania przez ucznia)
            var existingScores = _dbContext.StudentTaskScores.Where(s => s.StudentId == _studentId).ToList();
            var workRecord = _dbContext.StudentWorkRecords.FirstOrDefault(r => r.StudentId == _studentId && r.WrittenWorkId == _workId);

            // Jak czegoś brakuje (np. uczeń nie istnieje), to uciekamy.
            if (student == null || _currentWork == null) return;

            // Jeśli uczeń ma już jakieś wpisy dla tej pracy, to je ładujemy do kontrolek
            if (workRecord != null)
            {
                GroupTextBox.Text = workRecord.Group;
                CustomDateWrittenPicker.SelectedDate = workRecord.CustomDateWritten;
                CustomDateEnteredPicker.SelectedDate = workRecord.CustomDateEntered;
                AbsentCheckBox.IsChecked = workRecord.IsAbsent;
                
                // Ładujemy dane z panelu poprawy (w tym nową grupę poprawy)
                RetakeGroupTextBox.Text = workRecord.RetakeGroup; 
                RetakeDeadlinePicker.SelectedDate = workRecord.RetakeDeadline;
                RetakeDateWrittenPicker.SelectedDate = workRecord.RetakeDateWritten;
                RetakeDateEnteredPicker.SelectedDate = workRecord.RetakeDateEntered;
                _isRetakeActive = workRecord.IsRetakeActive;
            }

            // Przeliczamy i ustawiamy tekst dla terminu poprawy
            UpdateDeadlinePrompt(); 
            // Ukrywamy/pokazujemy panel poprawy zależnie od tego, co było w bazie
            ApplyRetakeVisibility(); 

            // Ustawiamy ładne teksty w nagłówku
            WorkTypeAndTitleText.Text = $"{_currentWork.WorkType.ToUpper()}: {_currentWork.Title}";
            ClassGroupText.Text = $"Klasa: {student.SchoolClass.Name} | Uczeń: {student.FullName}";
            
            string defaultWritten = _currentWork.DateWritten?.ToString("dd.MM.yyyy") ?? "Brak";
            string defaultEntered = _currentWork.DateEntered?.ToString("dd.MM.yyyy") ?? "Brak";
            DatesText.Text = $"Terminy całej klasy - Napisano: {defaultWritten} | Wpisano: {defaultEntered}";

            // Tłumaczymy to, co dała baza (surowe rekordy), na nasz ułatwiony obiekt TaskScoreViewModel, 
            // z którym potrafi dogadać się tabela WPF (DataGrid).
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
                
                // Gdy ktokolwiek wpisze cokolwiek w komórkę (PropertyChanged), wywołaj przeliczenie całkowitego wyniku (live calculator).
                vm.PropertyChanged += (s, e) => RecalculateTotalScore();
                ScoresCollection.Add(vm);
            }

            ScoresDataGrid.ItemsSource = ScoresCollection;
            // Odpalamy to raz na sucho, żeby na starcie pokazało punkty w prawym górnym rogu
            RecalculateTotalScore(); 
        }

        // ====================================================================
        // LOGIKA INTERFEJSU (Daty, panele, NB)
        // ====================================================================

        // Metoda, która dba o to, by termin na poprawę miał zawsze równe 14 dni od daty wpisania oceny.
        private void UpdateDeadlinePrompt()
        {
            DateTime? baseDate = CustomDateEnteredPicker.SelectedDate ?? _currentWork?.DateEntered;
            if (baseDate.HasValue)
            {
                DateTime deadline = baseDate.Value.AddDays(14);
                
                // Zdejmujemy starą blokadę. Kiedy okno jest w pełni załadowane i nauczyciel klika w kalendarz,
                // data poprawy zawsze posłusznie zaktualizuje się do +14 dni od wybranej daty (nawet jeśli cofamy się w czasie).
                if (_isLoaded)
                {
                    RetakeDeadlinePicker.SelectedDate = deadline; 
                }
                
                DateTime? displayDate = RetakeDeadlinePicker.SelectedDate ?? deadline;
                RetakeDeadlinePromptText.Text = $"Termin na poprawę: do {displayDate.Value:dd.MM.yyyy}";
            }
            else
            {
                RetakeDeadlinePromptText.Text = "Termin na poprawę: (Brak daty wpisania)";
                if (_isLoaded)
                {
                    RetakeDeadlinePicker.SelectedDate = null;
                }
            }
        }

        // Kiedy nauczyciel zmieni datę wpisania oceny ucznia, od razu reagujemy i przeliczamy 14 dni
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
                
                // Usuwamy całą wpisaną poprawę z pamięci podręcznej (zniknie po kliknięciu Zapisz).
                foreach (var vm in ScoresCollection)
                {
                    vm.RetakeScoreL1 = null; vm.RetakeScoreL2 = null; vm.RetakeScoreL3 = null;
                }
                RetakeGroupTextBox.Text = string.Empty; 
                RetakeDateWrittenPicker.SelectedDate = null;
                RetakeDateEnteredPicker.SelectedDate = null;

                ApplyRetakeVisibility();
                RecalculateTotalScore();
            }
        }

        // Chowa albo pokazuje kolumny i panel zależnie od tego, czy poprawa jest odpalona
        private void ApplyRetakeVisibility()
        {
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

        // Wyłączamy DataGrid (żeby nie dało się wklepywać ocen) i przeliczamy punkty (pokazuje się wielkie NB)
        private void AbsentCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            ScoresDataGrid.IsEnabled = AbsentCheckBox.IsChecked != true; 
            RecalculateTotalScore();
        }

        // Rozwijanie / Zwijanie dużej tabeli z zadaniami
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

        // ====================================================================
        // DYNAMICZNY KALKULATOR PSO (Liczy w locie, w pamięci)
        // ====================================================================
        
        // Klon algorytmu z GradeCalculator. Bierzemy te dane z ViewModelu, a nie z bazy danych, 
        // żeby nauczyciel widział zmieniający się wynik natychmiast podczas wpisywania.
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

            // MidpointRounding.AwayFromZero = zasada oświaty. Jak wychodzi X.5, to idzie w górę.
            return Math.Round(M * (0.54 * (sumQ1 / n) + 0.9 * (sumQ2 / n) + (sumQ3 / n)), 0, MidpointRounding.AwayFromZero);
        }

        // Ta metoda tylko patrzy na to, co policzył kod wyżej i wyrzuca to na ten wielki napis w prawym górnym rogu
        private void RecalculateTotalScore()
        {
            if (AbsentCheckBox.IsChecked == true)
            {
                RetakePromptPanel.IsEnabled = false; 
                TotalResultText.Text = "NB";
                TotalResultText.Foreground = System.Windows.Media.Brushes.IndianRed; 
                return;
            }

            RetakePromptPanel.IsEnabled = true;
            double M = _currentWork?.MaxFinalPoints ?? 0;
            
            double totalBase = CalculateScoreFromViewModels(M, false);
            double totalRetake = CalculateScoreFromViewModels(M, true);

            // Zamiast patrzeć na to, czy suma jest > 0, sprawdzamy, czy nauczyciel fizycznie wpisał 
            // jakąkolwiek wartość (nawet jeśli jest to nieszczęsne 0) do którejkolwiek z komórek.
            bool hasBaseScores = ScoresCollection.Any(vm => vm.ScoreL1.HasValue || vm.ScoreL2.HasValue || vm.ScoreL3.HasValue);
            bool hasRetakeScores = ScoresCollection.Any(vm => vm.RetakeScoreL1.HasValue || vm.RetakeScoreL2.HasValue || vm.RetakeScoreL3.HasValue);

            if (_isRetakeActive)
            {
                if (hasRetakeScores && totalRetake > totalBase)
                {
                    TotalResultText.Text = $"{totalRetake} / {M} pkt (popr.)";
                    TotalResultText.Foreground = System.Windows.Media.Brushes.LightGreen;
                }
                else if (hasBaseScores)
                {
                    TotalResultText.Text = $"{totalBase} / {M} pkt";
                    TotalResultText.Foreground = System.Windows.Media.Brushes.LightSkyBlue;
                }
                else
                {
                    TotalResultText.Text = "Brak ocen";
                    TotalResultText.Foreground = System.Windows.Media.Brushes.LightSkyBlue;
                }
            }
            else
            {
                if (hasBaseScores)
                {
                    TotalResultText.Text = $"{totalBase} / {M} pkt";
                    TotalResultText.Foreground = System.Windows.Media.Brushes.LightSkyBlue;
                }
                else
                {
                    TotalResultText.Text = "Brak ocen";
                    TotalResultText.Foreground = System.Windows.Media.Brushes.LightSkyBlue;
                }
            }
        }

        // ====================================================================
        // ZAPISYWANIE DANYCH (WALIDACJE I PUSZCZANIE DO BAZY)
        // ====================================================================
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // Pozwala WPF ogarnąć, że komórka, w której uczeń przed sekundą coś wpisał, ma puścić focus i wrzucić wartość do ViewModelu.
            Keyboard.ClearFocus();

            // --------------------------------------------------------------------
            // 1. Zabezpieczenie przed wpisywaniem wyrywkowym (Opcja 0, 100% albo NB)
            // --------------------------------------------------------------------
            
            // Sprawdzamy czy nauczyciel zaczął w ogóle coś wpisywać w terminie bazowym (choćby jeden poziom)
            bool hasAnyBaseScore = ScoresCollection.Any(vm => vm.ScoreL1 != null || vm.ScoreL2 != null || vm.ScoreL3 != null);
            
            // Sprawdzamy czy absolutnie w każdym wierszu podana jest jakaś ocena
            bool hasAllBaseScores = ScoresCollection.All(vm => vm.ScoreL1 != null || vm.ScoreL2 != null || vm.ScoreL3 != null);

            // Jak facet zaczął wpisywać, ale nie skończył, to wywalamy błąd.
            // (Chyba że zaznaczył NB, wtedy wybaczamy, bo i tak nadpiszemy oceny zerami).
            if (AbsentCheckBox.IsChecked == false && hasAnyBaseScore && !hasAllBaseScores)
            {
                MessageBox.Show("Narzędzie bezpieczeństwa PSO: Rozpoczęto wpisywanie ocen dla terminu bazowego. Musisz uzupełnić oceny we wszystkich zadaniach, aby wynik mógł zostać poprawnie uśredniony.", 
                                "Ajajajaj! Brakujące wpisy...", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // --------------------------------------------------------------------
            // 2. To samo rygorystyczne sprawdzanie dla panelu poprawy
            // --------------------------------------------------------------------
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

            // --------------------------------------------------------------------
            // 3. Sprawdzanie przed oszukiwaniem matematyki (np. 15 pkt w zadaniu na max 5 pkt)
            // --------------------------------------------------------------------
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
            
            // ====================================================================
            // ZAPIS DANYCH - PRZEPISYWANIE Z PAMIĘCI PODRĘCZNEJ DO BAZY
            // ====================================================================

            // Krok 1: Metadane (daty, NB, Grupy)
            var workRecord = _dbContext.StudentWorkRecords.FirstOrDefault(r => r.StudentId == _studentId && r.WrittenWorkId == _workId);
            if (workRecord == null)
            {
                workRecord = new StudentWorkRecord { StudentId = _studentId, WrittenWorkId = _workId };
                _dbContext.StudentWorkRecords.Add(workRecord);
            }
            
            workRecord.Group = GroupTextBox.Text.Trim();
            workRecord.CustomDateWritten = CustomDateWrittenPicker.SelectedDate;
            workRecord.CustomDateEntered = CustomDateEnteredPicker.SelectedDate;
            workRecord.IsAbsent = AbsentCheckBox.IsChecked == true;
            
            // Jeżeli kliknięto opcję poprawy, zapisujemy też jej grupę, jeżeli nie - czyścimy ewentualne pozostałości.
            workRecord.RetakeGroup = _isRetakeActive ? RetakeGroupTextBox.Text.Trim() : string.Empty;
            
            workRecord.IsRetakeActive = _isRetakeActive;
            workRecord.RetakeDeadline = RetakeDeadlinePicker.SelectedDate;
            workRecord.RetakeDateWritten = _isRetakeActive ? RetakeDateWrittenPicker.SelectedDate : null;
            workRecord.RetakeDateEntered = _isRetakeActive ? RetakeDateEnteredPicker.SelectedDate : null;

            // Krok 2: Oceny punktowe
            var existingScores = _dbContext.StudentTaskScores.Where(s => s.StudentId == _studentId).ToList();
            foreach (var vm in ScoresCollection)
            {
                var record = existingScores.FirstOrDefault(s => s.WrittenWorkTaskId == vm.TaskId);
                if (record == null)
                {
                    record = new StudentTaskScore { StudentId = _studentId, WrittenWorkTaskId = vm.TaskId };
                    _dbContext.StudentTaskScores.Add(record);
                }
                
                // UWAGA: Jak facet zaznaczy "NB", to niezależnie od tego co tam zostawił wpisane, wszystko zerujemy. Uczeń nie pisał, więc punktów nie ma.
                record.PointsLevel1 = AbsentCheckBox.IsChecked == true ? 0 : vm.ScoreL1;
                record.PointsLevel2 = AbsentCheckBox.IsChecked == true ? 0 : vm.ScoreL2;
                record.PointsLevel3 = AbsentCheckBox.IsChecked == true ? 0 : vm.ScoreL3;
                
                record.RetakePointsLevel1 = _isRetakeActive ? vm.RetakeScoreL1 : null;
                record.RetakePointsLevel2 = _isRetakeActive ? vm.RetakeScoreL2 : null;
                record.RetakePointsLevel3 = _isRetakeActive ? vm.RetakeScoreL3 : null;
            }

            // Ogień! Wszelkie zmiany lądują fizycznie na dysku w pliku .db.
            _dbContext.SaveChanges();
            
            // Powiadamiamy ekran pod spodem, żeby zaktualizował wiersz, i zamykamy siebie.
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

        // Ta metoda łapie sygnał zanim TextBox zdąży użyć strzałki do wędrowania wewnątrz swojej cyferki.
        private void ScoreTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right)
            {
                // Tłumaczymy wciśniętą strzałkę na komendę poruszania się dla tabeli
                FocusNavigationDirection direction = FocusNavigationDirection.Next;
                switch (e.Key)
                {
                    case Key.Up: direction = FocusNavigationDirection.Up; break;
                    case Key.Down: direction = FocusNavigationDirection.Down; break;
                    case Key.Left: direction = FocusNavigationDirection.Left; break;
                    case Key.Right: direction = FocusNavigationDirection.Right; break;
                }

                if (sender is TextBox textBox)
                {
                    // Siłowo wypychamy aktywność w stronę wciśniętej strzałki (do innej komórki)
                    textBox.MoveFocus(new TraversalRequest(direction));
                    
                    // Mówimy systemowi Windows: "Uznałem ten klawisz za załatwiony, nie rób z nim nic więcej"
                    e.Handled = true; 
                }
            }
        }
        private void DataGridCell_GotFocus(object sender, RoutedEventArgs e)
        {
            // Odrzucamy wywołania systemowe i sprawdzamy, czy skupienie faktycznie padło na główną komórkę.
            if (e.OriginalSource is DataGridCell cell)
            {
                // Znajdujemy fizyczne pole tekstowe zagnieżdżone głęboko w XAMLu i mówimy mu "obudź się!"
                var textBox = FindVisualChild<TextBox>(cell);
                if (textBox != null)
                {
                    textBox.Focus();
                    textBox.SelectAll(); // Automatycznie podświetla starą liczbę, dzięki czemu od razu wpisujesz nową bez użycia Backspace.
                }
            }
        }

        // Sprytne, małe narzędzie, które przeszukuje strukturę danej kontrolki w poszukiwaniu obiektu danego typu (tutaj: TextBoxa).
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
    // MODEL DANYCH - SŁUŻĄCY TYLKO DO WYŚWIETLANIA I EDYCJI W TABELCE
    // ====================================================================
    public class TaskScoreViewModel : INotifyPropertyChanged
    {
        public int TaskId { get; set; }
        public int TaskNumber { get; set; }
        public double? MaxL1 { get; set; }
        public double? MaxL2 { get; set; }
        public double? MaxL3 { get; set; }

        // BARDZO SPYRTNY MECHANIZM PSO: Blokowanie poziomów.
        // Jeśli nauczyciel uderzy cyfrę w Poziomie I, system automatycznie zeruje (ściślej mówiąc przypisuje null) 
        // Poziom II i III dla tego zadania. Nie da się zaliczyć dwóch poziomów w tym samym zadaniu.
        
        private double? _scoreL1;
        public double? ScoreL1 { get => _scoreL1; set { _scoreL1 = value; if(value != null) { _scoreL2 = null; _scoreL3 = null; OnBothChangedT1(); } OnPropertyChanged(); } }
        
        private double? _scoreL2;
        public double? ScoreL2 { get => _scoreL2; set { _scoreL2 = value; if(value != null) { _scoreL1 = null; _scoreL3 = null; OnBothChangedT1(); } OnPropertyChanged(); } }
        
        private double? _scoreL3;
        public double? ScoreL3 { get => _scoreL3; set { _scoreL3 = value; if(value != null) { _scoreL1 = null; _scoreL2 = null; OnBothChangedT1(); } OnPropertyChanged(); } }

        // Mówimy tabelce DataGrid: "Hej, zmieniłem wartości w tych komórkach w tle, odśwież widok!".
        private void OnBothChangedT1() { OnPropertyChanged(nameof(ScoreL1)); OnPropertyChanged(nameof(ScoreL2)); OnPropertyChanged(nameof(ScoreL3)); }

        // Kopia tego samego mechanizmu, ale dla wierszy z POPRAWY.
        private double? _retakeScoreL1;
        public double? RetakeScoreL1 { get => _retakeScoreL1; set { _retakeScoreL1 = value; if(value != null) { _retakeScoreL2 = null; _retakeScoreL3 = null; OnBothChangedRetake(); } OnPropertyChanged(); } }
        
        private double? _retakeScoreL2;
        public double? RetakeScoreL2 { get => _retakeScoreL2; set { _retakeScoreL2 = value; if(value != null) { _retakeScoreL1 = null; _retakeScoreL3 = null; OnBothChangedRetake(); } OnPropertyChanged(); } }
        
        private double? _retakeScoreL3;
        public double? RetakeScoreL3 { get => _retakeScoreL3; set { _retakeScoreL3 = value; if(value != null) { _retakeScoreL1 = null; _retakeScoreL2 = null; OnBothChangedRetake(); } OnPropertyChanged(); } }

        private void OnBothChangedRetake() { OnPropertyChanged(nameof(RetakeScoreL1)); OnPropertyChanged(nameof(RetakeScoreL2)); OnPropertyChanged(nameof(RetakeScoreL3)); }

        // Niezbędny interfejs WPF, żeby bindowanie danych w ogóle miało prawo działać.
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}