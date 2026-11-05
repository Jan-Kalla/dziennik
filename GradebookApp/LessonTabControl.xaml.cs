using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace GradebookApp
{
    public partial class LessonTabControl : UserControl
    {
        // Centralna lista całego planu, jedno źródło prawdy
        private ObservableCollection<ScheduleRow> _scheduleRows = new ObservableCollection<ScheduleRow>();
        
        // Zmienna lista wraperów dla aktywnego dnia
        private ObservableCollection<DailyRowWrapper> _dailyRows = new ObservableCollection<DailyRowWrapper>();
        
        private DateTime _currentDate = DateTime.Today;

        public LessonTabControl()
        {
            InitializeComponent();
            SetupData();
        }

        private void SetupData()
        {
            // Tymczasowe dane startowe (zgodnie ze szkicami), później zasilane bazą danych
            _scheduleRows.Add(new ScheduleRow { TimeSlot = "8:00 - 8:45", MonClass = "2 C", WedClass = "3 CGN", ThuClass = "5 FL" });
            _scheduleRows.Add(new ScheduleRow { TimeSlot = "8:55 - 9:40", MonClass = "dyżur", MonMat = "I piętro szkoły" });
            _scheduleRows.Add(new ScheduleRow { TimeSlot = "9:50 - 10:35", MonClass = "1 A", TueClass = "7", WedClass = "1 EN", ThuClass = "1 C" });
            
            WeeklyScheduleGrid.ItemsSource = _scheduleRows;
            DailyScheduleGrid.ItemsSource = _dailyRows;
            
            SkipWeekends();
            RefreshDailyView();
        }

        private void SkipWeekends()
        {
            if (_currentDate.DayOfWeek == DayOfWeek.Saturday) _currentDate = _currentDate.AddDays(2);
            else if (_currentDate.DayOfWeek == DayOfWeek.Sunday) _currentDate = _currentDate.AddDays(1);
        }

        private void RefreshDailyView()
        {
            var culture = new System.Globalization.CultureInfo("pl-PL");
            string dayName = culture.DateTimeFormat.GetDayName(_currentDate.DayOfWeek);
            dayName = char.ToUpper(dayName[0]) + dayName.Substring(1);
            
            string prefix = "";
            if (_currentDate.Date == DateTime.Today) prefix = "Dzisiaj: ";
            else if (_currentDate.Date == DateTime.Today.AddDays(1)) prefix = "Jutro: ";
            else if (_currentDate.Date == DateTime.Today.AddDays(-1)) prefix = "Wczoraj: ";
            
            LessonDateText.Text = $"{prefix}{dayName}, {_currentDate.ToString("dd.MM.yyyy")}";

            _dailyRows.Clear();
            foreach (var row in _scheduleRows)
            {
                // Tworzymy dynamiczny wrapper przekierowujący kolumny na wybrany dzień
                _dailyRows.Add(new DailyRowWrapper(row, _currentDate.DayOfWeek));
            }
        }

        // Nawigacja dni (z omijaniem soboty i niedzieli)
        private void PrevDay_Click(object sender, RoutedEventArgs e)
        {
            _currentDate = _currentDate.AddDays(-1);
            if (_currentDate.DayOfWeek == DayOfWeek.Sunday) _currentDate = _currentDate.AddDays(-2);
            RefreshDailyView();
        }

        private void NextDay_Click(object sender, RoutedEventArgs e)
        {
            _currentDate = _currentDate.AddDays(1);
            if (_currentDate.DayOfWeek == DayOfWeek.Saturday) _currentDate = _currentDate.AddDays(2);
            RefreshDailyView();
        }

        // Dodawanie wierszy ukrytym "+" (działa i z dziennego, i z tygodniowego)
        private void AddRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                int index = -1;
                if (btn.DataContext is DailyRowWrapper dailyRow) index = _scheduleRows.IndexOf(dailyRow.Row);
                else if (btn.DataContext is ScheduleRow weeklyRow) index = _scheduleRows.IndexOf(weeklyRow);

                if (index >= 0)
                {
                    _scheduleRows.Insert(index + 1, new ScheduleRow { TimeSlot = "" });
                    RefreshDailyView(); // Odświeżamy też dzienny widok
                }
                else if (_scheduleRows.Count == 0)
                {
                    _scheduleRows.Add(new ScheduleRow { TimeSlot = "8:00 - 8:45" });
                    RefreshDailyView();
                }
            }
        }

        // Przełączanie widoków
        private void DailyViewBtn_Click(object sender, RoutedEventArgs e)
        {
            DailyViewBtn.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3E3E42"));
            DailyViewBtn.Foreground = System.Windows.Media.Brushes.White;
            DailyViewBtn.FontWeight = FontWeights.Bold;

            WeeklyViewBtn.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2D2D30"));
            WeeklyViewBtn.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#AAAAAA"));
            WeeklyViewBtn.FontWeight = FontWeights.Normal;

            DailyScheduleGrid.Visibility = Visibility.Visible;
            DateNavigationPanel.Visibility = Visibility.Visible;
            
            WeeklyScheduleGrid.Visibility = Visibility.Collapsed;
            WeeklyTitleText.Visibility = Visibility.Collapsed;
        }

        private void WeeklyViewBtn_Click(object sender, RoutedEventArgs e)
        {
            WeeklyViewBtn.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3E3E42"));
            WeeklyViewBtn.Foreground = System.Windows.Media.Brushes.White;
            WeeklyViewBtn.FontWeight = FontWeights.Bold;

            DailyViewBtn.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2D2D30"));
            DailyViewBtn.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#AAAAAA"));
            DailyViewBtn.FontWeight = FontWeights.Normal;

            WeeklyScheduleGrid.Visibility = Visibility.Visible;
            WeeklyTitleText.Visibility = Visibility.Visible;

            DailyScheduleGrid.Visibility = Visibility.Collapsed;
            DateNavigationPanel.Visibility = Visibility.Collapsed;
        }

        private void GoToLessonInfo_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Tu w przyszłości otworzy się szkicowane okno 'INFORMACJE' dla bieżącego tematu i klasy.", "Brakujące okno", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // ==========================================================
    // MODELE DANYCH (Synchronizujące się w czasie rzeczywistym)
    // ==========================================================

    public class ScheduleRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null!) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private string _timeSlot = "";
        public string TimeSlot { get => _timeSlot; set { _timeSlot = value; OnPropertyChanged(); } }

        private string _monClass = "";
        public string MonClass { get => _monClass; set { _monClass = value; OnPropertyChanged(); } }
        private string _monMat = "";
        public string MonMat { get => _monMat; set { _monMat = value; OnPropertyChanged(); } }

        private string _tueClass = "";
        public string TueClass { get => _tueClass; set { _tueClass = value; OnPropertyChanged(); } }
        private string _tueMat = "";
        public string TueMat { get => _tueMat; set { _tueMat = value; OnPropertyChanged(); } }

        private string _wedClass = "";
        public string WedClass { get => _wedClass; set { _wedClass = value; OnPropertyChanged(); } }
        private string _wedMat = "";
        public string WedMat { get => _wedMat; set { _wedMat = value; OnPropertyChanged(); } }

        private string _thuClass = "";
        public string ThuClass { get => _thuClass; set { _thuClass = value; OnPropertyChanged(); } }
        private string _thuMat = "";
        public string ThuMat { get => _thuMat; set { _thuMat = value; OnPropertyChanged(); } }

        private string _friClass = "";
        public string FriClass { get => _friClass; set { _friClass = value; OnPropertyChanged(); } }
        private string _friMat = "";
        public string FriMat { get => _friMat; set { _friMat = value; OnPropertyChanged(); } }
    }

    // Ten elegancki Wrapper sprawia, że wpisując 'Materiał' w widoku dziennym, trafia on prosto do pamięci widoku tygodniowego na odpowiedni dzień.
    public class DailyRowWrapper : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null!) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public ScheduleRow Row { get; }
        public DayOfWeek Day { get; }

        public DailyRowWrapper(ScheduleRow row, DayOfWeek day)
        {
            Row = row; Day = day;
            Row.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(Row.TimeSlot)) OnPropertyChanged(nameof(TimeSlot)); };
        }

        public string TimeSlot { get => Row.TimeSlot; set { Row.TimeSlot = value; OnPropertyChanged(); } }

        public string ClassName
        {
            get => Day switch { DayOfWeek.Monday => Row.MonClass, DayOfWeek.Tuesday => Row.TueClass, DayOfWeek.Wednesday => Row.WedClass, DayOfWeek.Thursday => Row.ThuClass, DayOfWeek.Friday => Row.FriClass, _ => "" };
            set { switch (Day) { case DayOfWeek.Monday: Row.MonClass = value; break; case DayOfWeek.Tuesday: Row.TueClass = value; break; case DayOfWeek.Wednesday: Row.WedClass = value; break; case DayOfWeek.Thursday: Row.ThuClass = value; break; case DayOfWeek.Friday: Row.FriClass = value; break; } OnPropertyChanged(); }
        }

        public string Material
        {
            get => Day switch { DayOfWeek.Monday => Row.MonMat, DayOfWeek.Tuesday => Row.TueMat, DayOfWeek.Wednesday => Row.WedMat, DayOfWeek.Thursday => Row.ThuMat, DayOfWeek.Friday => Row.FriMat, _ => "" };
            set { switch (Day) { case DayOfWeek.Monday: Row.MonMat = value; break; case DayOfWeek.Tuesday: Row.TueMat = value; break; case DayOfWeek.Wednesday: Row.WedMat = value; break; case DayOfWeek.Thursday: Row.ThuMat = value; break; case DayOfWeek.Friday: Row.FriMat = value; break; } OnPropertyChanged(); }
        }
    }
}