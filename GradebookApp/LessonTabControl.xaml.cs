using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GradebookApp.Services;

namespace GradebookApp
{
    public partial class LessonTabControl : UserControl
    {
        private ObservableCollection<ScheduleRow> _scheduleRows = new ObservableCollection<ScheduleRow>();
        private ObservableCollection<DailyRowWrapper> _dailyRows = new ObservableCollection<DailyRowWrapper>();
        
        public ObservableCollection<string> ClassNames { get; set; } = new ObservableCollection<string>();

        private DateTime _currentDate = DateTime.Today;
        private readonly GradebookDataService _dataService;
        private bool _isInitializingData = true;

        public LessonTabControl()
        {
            InitializeComponent();
            this.DataContext = this; 
            _dataService = new GradebookDataService();
            
            LoadClassNames();
            SetupData();
        }

        private void LoadClassNames()
        {
            var classes = _dataService.GetAllClasses();
            foreach (var c in classes.OrderBy(c => c.Name))
            {
                ClassNames.Add(c.Name);
            }
        }

        // NOWOŚĆ: Obliczanie poniedziałku dla obecnie wyświetlanej daty
        private DateTime GetMondayOfCurrentWeek()
        {
            int diff = (7 + (_currentDate.DayOfWeek - DayOfWeek.Monday)) % 7;
            return _currentDate.AddDays(-1 * diff).Date;
        }

        private void SetupData()
        {
            _scheduleRows.Clear();
            var templates = _dataService.GetLessonTemplates();
            var timeSlots = templates.Select(i => i.TimeSlot).Distinct().ToList();
            if (!timeSlots.Any()) timeSlots.Add("08:00 - 08:45");

            DateTime currentMonday = GetMondayOfCurrentWeek();
            var currentWeekMaterials = _dataService.GetScheduleForWeek(currentMonday);

            foreach (var slot in timeSlots)
            {
                var row = new ScheduleRow { TimeSlot = slot };
                
                var mon = templates.FirstOrDefault(i => i.TimeSlot == slot && i.DayOfWeek == DayOfWeek.Monday);
                if (mon != null) { var mItem = currentWeekMaterials.FirstOrDefault(m => m.TimeSlot == slot && m.Date == currentMonday); row.MonClass = mon.ClassName; row.MonMat = mItem?.Material ?? ""; row.MonNotes = mItem?.Notes ?? ""; }
                
                var tue = templates.FirstOrDefault(i => i.TimeSlot == slot && i.DayOfWeek == DayOfWeek.Tuesday);
                if (tue != null) { var mItem = currentWeekMaterials.FirstOrDefault(m => m.TimeSlot == slot && m.Date == currentMonday.AddDays(1)); row.TueClass = tue.ClassName; row.TueMat = mItem?.Material ?? ""; row.TueNotes = mItem?.Notes ?? ""; }

                var wed = templates.FirstOrDefault(i => i.TimeSlot == slot && i.DayOfWeek == DayOfWeek.Wednesday);
                if (wed != null) { var mItem = currentWeekMaterials.FirstOrDefault(m => m.TimeSlot == slot && m.Date == currentMonday.AddDays(2)); row.WedClass = wed.ClassName; row.WedMat = mItem?.Material ?? ""; row.WedNotes = mItem?.Notes ?? ""; }

                var thu = templates.FirstOrDefault(i => i.TimeSlot == slot && i.DayOfWeek == DayOfWeek.Thursday);
                if (thu != null) { var mItem = currentWeekMaterials.FirstOrDefault(m => m.TimeSlot == slot && m.Date == currentMonday.AddDays(3)); row.ThuClass = thu.ClassName; row.ThuMat = mItem?.Material ?? ""; row.ThuNotes = mItem?.Notes ?? ""; }

                var fri = templates.FirstOrDefault(i => i.TimeSlot == slot && i.DayOfWeek == DayOfWeek.Friday);
                if (fri != null) { var mItem = currentWeekMaterials.FirstOrDefault(m => m.TimeSlot == slot && m.Date == currentMonday.AddDays(4)); row.FriClass = fri.ClassName; row.FriMat = mItem?.Material ?? ""; row.FriNotes = mItem?.Notes ?? ""; }

                row.PropertyChanged += Row_PropertyChanged;
                _scheduleRows.Add(row);
            }

            WeeklyScheduleGrid.ItemsSource = _scheduleRows;
            DailyScheduleGrid.ItemsSource = _dailyRows;
            
            UpdateLessonNumbers();
            UpdateAllWatermarks(); 
            _isInitializingData = false;
            SkipWeekends();
            RefreshDailyView();
        }

        private void SaveToDatabase()
        {
            if (_isInitializingData) return;

            var templates = new List<LessonTemplateItem>();
            var materials = new List<LessonScheduleItem>();
            int emptyCounter = 0;
            DateTime currentMonday = GetMondayOfCurrentWeek();

            foreach (var row in _scheduleRows)
            {
                string ts = row.TimeSlot ?? "";
                if (string.IsNullOrEmpty(ts))
                {
                    ts = new string(' ', emptyCounter);
                    emptyCounter++;
                }

                templates.Add(new LessonTemplateItem { TimeSlot = ts, DayOfWeek = DayOfWeek.Monday, ClassName = row.MonClass ?? "" });
                templates.Add(new LessonTemplateItem { TimeSlot = ts, DayOfWeek = DayOfWeek.Tuesday, ClassName = row.TueClass ?? "" });
                templates.Add(new LessonTemplateItem { TimeSlot = ts, DayOfWeek = DayOfWeek.Wednesday, ClassName = row.WedClass ?? "" });
                templates.Add(new LessonTemplateItem { TimeSlot = ts, DayOfWeek = DayOfWeek.Thursday, ClassName = row.ThuClass ?? "" });
                templates.Add(new LessonTemplateItem { TimeSlot = ts, DayOfWeek = DayOfWeek.Friday, ClassName = row.FriClass ?? "" });

                if (!string.IsNullOrWhiteSpace(row.MonMat) || !string.IsNullOrWhiteSpace(row.MonNotes)) materials.Add(new LessonScheduleItem { TimeSlot = ts, Date = currentMonday, Material = row.MonMat, Notes = row.MonNotes });
                if (!string.IsNullOrWhiteSpace(row.TueMat) || !string.IsNullOrWhiteSpace(row.TueNotes)) materials.Add(new LessonScheduleItem { TimeSlot = ts, Date = currentMonday.AddDays(1), Material = row.TueMat, Notes = row.TueNotes });
                if (!string.IsNullOrWhiteSpace(row.WedMat) || !string.IsNullOrWhiteSpace(row.WedNotes)) materials.Add(new LessonScheduleItem { TimeSlot = ts, Date = currentMonday.AddDays(2), Material = row.WedMat, Notes = row.WedNotes });
                if (!string.IsNullOrWhiteSpace(row.ThuMat) || !string.IsNullOrWhiteSpace(row.ThuNotes)) materials.Add(new LessonScheduleItem { TimeSlot = ts, Date = currentMonday.AddDays(3), Material = row.ThuMat, Notes = row.ThuNotes });
                if (!string.IsNullOrWhiteSpace(row.FriMat) || !string.IsNullOrWhiteSpace(row.FriNotes)) materials.Add(new LessonScheduleItem { TimeSlot = ts, Date = currentMonday.AddDays(4), Material = row.FriMat, Notes = row.FriNotes });
            }
            
            _dataService.SaveLessonTemplates(templates);
            _dataService.SaveScheduleMaterials(materials);
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
                var wrapper = new DailyRowWrapper(row, _currentDate.DayOfWeek);
                // Uwzględniamy obecność Uwag (Notes) w warunku wyświetlania wiersza
                if (!string.IsNullOrWhiteSpace(wrapper.ClassName) || !string.IsNullOrWhiteSpace(wrapper.Material) || !string.IsNullOrWhiteSpace(wrapper.Notes) || row.ForceShow)
                {
                    _dailyRows.Add(wrapper);
                }
            }
        }

        // ZWRÓCONA METODA - naprawia błędy CS0103
        private void UpdateLessonNumbers()
        {
            if (!_scheduleRows.Any()) return;

            int lessonCounter = 1;
            var firstLesson = _scheduleRows.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.TimeSlot));
            
            // Jeśli pierwsza lekcja zaczyna się od "7" rano - startujemy od zera
            if (firstLesson != null && (firstLesson.TimeSlot.StartsWith("07") || firstLesson.TimeSlot.StartsWith("7:")))
            {
                lessonCounter = 0;
            }

            foreach (var row in _scheduleRows)
            {
                if (string.IsNullOrWhiteSpace(row.TimeSlot))
                {
                    row.LessonNumber = ""; // Dyżur
                }
                else
                {
                    row.LessonNumber = lessonCounter.ToString();
                    lessonCounter++;
                }
            }
        }

        private void UpdateAllWatermarks()
        {
            var allUsedClasses = _scheduleRows.SelectMany(r => new[] { r.MonClass, r.TueClass, r.WedClass, r.ThuClass, r.FriClass })
                                              .Where(c => !string.IsNullOrWhiteSpace(c))
                                              .Distinct()
                                              .ToList();

            var histories = new Dictionary<string, string>();
            
            // Pobieramy całą historię ze wszystkich tygodni!
            var allHistoricalMaterials = _dataService.GetAllScheduleMaterials();

            foreach (var className in allUsedClasses)
            {
                var mats = new List<string>();
                
                // Krok 1: Wyłuskujemy materiał bieżąco wpisywany z tego tygodnia (jak dotychczas)
                foreach (var row in _scheduleRows)
                {
                    if (row.MonClass == className && !string.IsNullOrWhiteSpace(row.MonMat)) mats.Add(ExtractMat(row.MonMat));
                    if (row.TueClass == className && !string.IsNullOrWhiteSpace(row.TueMat)) mats.Add(ExtractMat(row.TueMat));
                    if (row.WedClass == className && !string.IsNullOrWhiteSpace(row.WedMat)) mats.Add(ExtractMat(row.WedMat));
                    if (row.ThuClass == className && !string.IsNullOrWhiteSpace(row.ThuMat)) mats.Add(ExtractMat(row.ThuMat));
                    if (row.FriClass == className && !string.IsNullOrWhiteSpace(row.FriMat)) mats.Add(ExtractMat(row.FriMat));
                }

                // Krok 2: Pobieramy powiązania lekcji z bazą i wyłuskujemy historyczny materiał
                // (Przeszukujemy szablony by dowiedzieć się, kiedy klasa ma zajęcia i łączymy to z datami wpisów z historii)
                var classTemplates = _dataService.GetLessonTemplates().Where(t => t.ClassName == className).ToList();
                foreach(var histMat in allHistoricalMaterials)
                {
                    // Szukamy, czy w dniu tygodnia historycznego wpisu dana klasa miała przypisaną lekcję na tej godzinie
                    var matchingTemplate = classTemplates.FirstOrDefault(t => t.DayOfWeek == histMat.Date.DayOfWeek && t.TimeSlot == histMat.TimeSlot);
                    if (matchingTemplate != null)
                    {
                        mats.Add(ExtractMat(histMat.Material));
                    }
                }

                histories[className] = string.Join(", ", mats.Distinct().Where(m => !string.IsNullOrWhiteSpace(m)));
            }

            // Dystrybucja historii do konkretnych wierszy (Zoptymalizowane i wolne od ostrzeżeń CS8604)
            foreach (var row in _scheduleRows)
            {
                row.MonWatermark = histories.TryGetValue(row.MonClass ?? "", out var mon) ? mon : "";
                row.TueWatermark = histories.TryGetValue(row.TueClass ?? "", out var tue) ? tue : "";
                row.WedWatermark = histories.TryGetValue(row.WedClass ?? "", out var wed) ? wed : "";
                row.ThuWatermark = histories.TryGetValue(row.ThuClass ?? "", out var thu) ? thu : "";
                row.FriWatermark = histories.TryGetValue(row.FriClass ?? "", out var fri) ? fri : "";
            }
        }

        private string ExtractMat(string fullMat)
        {
            if (fullMat.Contains(": ")) 
                return fullMat.Substring(fullMat.IndexOf(": ") + 2).Trim();
            return fullMat.Trim();
        }

        private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ScheduleRow.TimeSlot))
            {
                UpdateLessonNumbers();
            }
            
            if (e.PropertyName != nameof(ScheduleRow.LessonNumber) && 
                e.PropertyName != null && !e.PropertyName.EndsWith("Watermark"))
            {
                UpdateAllWatermarks();
            }
            
            SaveToDatabase();
        }

        private void SkipWeekends()
        {
            if (_currentDate.DayOfWeek == DayOfWeek.Saturday) _currentDate = _currentDate.AddDays(2);
            else if (_currentDate.DayOfWeek == DayOfWeek.Sunday) _currentDate = _currentDate.AddDays(1);
        }

        private void PrevDay_Click(object sender, RoutedEventArgs e)
        {
            var oldMonday = GetMondayOfCurrentWeek();
            _currentDate = _currentDate.AddDays(-1);
            if (_currentDate.DayOfWeek == DayOfWeek.Sunday) _currentDate = _currentDate.AddDays(-2);
            
            if (oldMonday != GetMondayOfCurrentWeek()) 
            {
                _isInitializingData = true;
                SetupData();
            }
            else RefreshDailyView();
        }

        private void NextDay_Click(object sender, RoutedEventArgs e)
        {
            var oldMonday = GetMondayOfCurrentWeek();
            _currentDate = _currentDate.AddDays(1);
            if (_currentDate.DayOfWeek == DayOfWeek.Saturday) _currentDate = _currentDate.AddDays(2);
            
            if (oldMonday != GetMondayOfCurrentWeek()) 
            {
                _isInitializingData = true;
                SetupData();
            }
            else RefreshDailyView();
        }
        
        // Aktualizacja widoku przy wyjściu z trybu tygodniowego z przeładowaniem
        private void DailyViewBtn_Click(object sender, RoutedEventArgs e)
        {
            // ... (kod kolorowania przycisków i ukrywania siatki zostaje bez zmian) ...
            DailyViewBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3E3E42"));
            DailyViewBtn.Foreground = Brushes.White;
            DailyViewBtn.FontWeight = FontWeights.Bold;

            WeeklyViewBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2D2D30"));
            WeeklyViewBtn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#AAAAAA"));
            WeeklyViewBtn.FontWeight = FontWeights.Normal;

            DailyScheduleGrid.Visibility = Visibility.Visible;
            DateNavigationPanel.Visibility = Visibility.Visible;
            WeeklyScheduleGrid.Visibility = Visibility.Collapsed;
            WeeklyTitleText.Visibility = Visibility.Collapsed;
            
            // Wymuszamy pełne odświeżenie danych i filtrów
            _isInitializingData = true;
            SetupData();
        }

        private void AddRowAtTop_Click(object sender, RoutedEventArgs e)
        {
            InsertNewRow(0);
        }

        private void AddRowBelow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                int index = GetRowIndex(btn);
                if (index >= 0) InsertNewRow(index + 1);
            }
        }

        private int GetRowIndex(Button btn)
        {
            if (btn.DataContext is DailyRowWrapper daily) return _scheduleRows.IndexOf(daily.Row);
            if (btn.DataContext is ScheduleRow weekly) return _scheduleRows.IndexOf(weekly);
            return -1;
        }

        private void InsertNewRow(int index)
        {
            var newRow = new ScheduleRow { TimeSlot = "", ForceShow = true };
            newRow.PropertyChanged += Row_PropertyChanged;
            _scheduleRows.Insert(index, newRow);
            
            UpdateLessonNumbers();
            RefreshDailyView();
            SaveToDatabase();
        }

        private void DeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menu)
            {
                if (menu.DataContext is DailyRowWrapper daily) _scheduleRows.Remove(daily.Row);
                else if (menu.DataContext is ScheduleRow weekly) _scheduleRows.Remove(weekly);

                UpdateLessonNumbers();
                RefreshDailyView();
                SaveToDatabase();
            }
        }

        private void WeeklyViewBtn_Click(object sender, RoutedEventArgs e)
        {
            WeeklyViewBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3E3E42"));
            WeeklyViewBtn.Foreground = Brushes.White;
            WeeklyViewBtn.FontWeight = FontWeights.Bold;

            DailyViewBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2D2D30"));
            DailyViewBtn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#AAAAAA"));
            DailyViewBtn.FontWeight = FontWeights.Normal;

            WeeklyScheduleGrid.Visibility = Visibility.Visible;
            WeeklyTitleText.Visibility = Visibility.Visible;
            DailyScheduleGrid.Visibility = Visibility.Collapsed;
            DateNavigationPanel.Visibility = Visibility.Collapsed;
        }

        private void GoToLessonInfo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is DailyRowWrapper row)
            {
                var window = new LessonInfoWindow(row, LessonDateText.Text, _currentDate) 
                { 
                    Owner = Window.GetWindow(this) 
                };
                window.ShowDialog();
            }
        }

        private void ClassComboBox_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Enter || e.Key == Key.Escape || e.Key == Key.Tab) 
                return;

            if (sender is ComboBox cmb)
            {
                string search = cmb.Text.ToLower();
                var filtered = ClassNames.Where(c => c.ToLower().Contains(search)).ToList();
                cmb.ItemsSource = filtered;
                
                if (filtered.Any() && !string.IsNullOrWhiteSpace(search))
                    cmb.IsDropDownOpen = true;
                else
                    cmb.IsDropDownOpen = false;
            }
        }

        private void TimeSlotTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                string input = textBox.Text;

                if (SmartTimeParser.TryParse(input, out string parsedTime))
                {
                    textBox.Text = parsedTime;
                    textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                }
                else
                {
                    textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();

                    MessageBox.Show(
                        $"Wprowadzona wartość '{input}' nie jest poprawną godziną.\n\n" +
                        $"Użyj formatu np. 9:30, 0930, 1450 lub przedziału 8:00-8:45.\n" +
                        $"Godziny muszą mieścić się w zakresie 00-23, a minuty 00-59.", 
                        "Nieprawidłowy format czasu", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void DataGridCell_GotFocus(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is DataGridCell cell)
            {
                var tb = GetVisualChild<TextBox>(cell);
                tb?.Focus();
            }
        }

        private void TasksDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var grid = sender as DataGrid;
            if (grid == null) return;

            if (e.Key == Key.Tab)
            {
                var textBox = e.OriginalSource as TextBox;
                if (textBox == null) return;

                var currentCell = GetParent<DataGridCell>(textBox);
                if (currentCell == null) return;

                var currentRow = GetParent<DataGridRow>(currentCell);
                if (currentRow == null) return;

                e.Handled = true;
                bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                
                int rowIndex = grid.ItemContainerGenerator.IndexFromContainer(currentRow);
                int colIndex = currentCell.Column.DisplayIndex;
                int originalRow = rowIndex, originalCol = colIndex;

                while (true)
                {
                    if (isShift)
                    {
                        colIndex--;
                        if (colIndex <= 0)
                        {
                            colIndex = grid.Columns.Count - 1;
                            rowIndex--;
                        }
                    }
                    else
                    {
                        colIndex++;
                        if (colIndex >= grid.Columns.Count)
                        {
                            colIndex = 1;
                            rowIndex++;
                        }
                    }

                    if (rowIndex < 0) rowIndex = grid.Items.Count - 1; 
                    if (rowIndex >= grid.Items.Count) rowIndex = 0;

                    if (TryFocusCell(grid, rowIndex, colIndex))
                        break;
                        
                    if (rowIndex == originalRow && colIndex == originalCol)
                        break;
                }
            }
            else if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right)
            {
                var textBox = e.OriginalSource as TextBox;
                if (textBox == null) return;

                if ((e.Key == Key.Left && textBox.CaretIndex > 0) || 
                    (e.Key == Key.Right && textBox.CaretIndex < textBox.Text.Length))
                    return; 

                var currentCell = GetParent<DataGridCell>(textBox);
                if (currentCell == null) return;

                var currentRow = GetParent<DataGridRow>(currentCell);
                if (currentRow == null) return;

                e.Handled = true;
                
                int rowIndex = grid.ItemContainerGenerator.IndexFromContainer(currentRow);
                int colIndex = currentCell.Column.DisplayIndex;

                if (e.Key == Key.Up) rowIndex--;
                else if (e.Key == Key.Down) rowIndex++;
                
                if (rowIndex < 0 || rowIndex >= grid.Items.Count) return;

                while (true)
                {
                    if (e.Key == Key.Left) colIndex--;
                    else if (e.Key == Key.Right) colIndex++;

                    if (colIndex <= 0 || colIndex >= grid.Columns.Count) break;

                    if (TryFocusCell(grid, rowIndex, colIndex))
                        break;
                }
            }
        }

        private bool TryFocusCell(DataGrid grid, int rowIndex, int colIndex)
        {
            if (rowIndex < 0 || rowIndex >= grid.Items.Count) return false;
            if (colIndex <= 0 || colIndex >= grid.Columns.Count) return false;

            var row = grid.ItemContainerGenerator.ContainerFromIndex(rowIndex) as DataGridRow;
            if (row != null)
            {
                var presenter = GetVisualChild<System.Windows.Controls.Primitives.DataGridCellsPresenter>(row);
                if (presenter != null)
                {
                    var cell = presenter.ItemContainerGenerator.ContainerFromIndex(colIndex) as DataGridCell;
                    if (cell != null)
                    {
                        var tb = GetVisualChild<TextBox>(cell);
                        if (tb != null) 
                        {
                            grid.ScrollIntoView(grid.Items[rowIndex], grid.Columns[colIndex]);
                            tb.Focus();
                            tb.SelectAll();
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private static T? GetParent<T>(DependencyObject? child) where T : DependencyObject
        {
            if (child == null) return null;
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            if (parentObject is T parent) return parent;
            return GetParent<T>(parentObject);
        }

        private static T? GetVisualChild<T>(DependencyObject? parent) where T : DependencyObject
        {
            if (parent == null) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                T? childOfChild = GetVisualChild<T>(child);
                if (childOfChild != null) return childOfChild;
            }
            return null;
        }
    }

    public class ScheduleRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null!) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public bool ForceShow { get; set; } = false;

        private string _lessonNumber = "";
        public string LessonNumber { get => _lessonNumber; set { if (_lessonNumber != value) { _lessonNumber = value; OnPropertyChanged(); } } }

        private string _timeSlot = "";
        public string TimeSlot 
        { 
            get => _timeSlot; 
            set { if (_timeSlot != value) { _timeSlot = value; OnPropertyChanged(); } } 
        }

        private string _monClass = "";
        public string MonClass { get => _monClass; set { _monClass = value; OnPropertyChanged(); } }
        private string _monMat = "";
        public string MonMat { get => _monMat; set { _monMat = value; OnPropertyChanged(); } }
        private string _monWatermark = "";
        public string MonWatermark { get => _monWatermark; set { if (_monWatermark != value) { _monWatermark = value; OnPropertyChanged(); } } }

        private string _tueClass = "";
        public string TueClass { get => _tueClass; set { _tueClass = value; OnPropertyChanged(); } }
        private string _tueMat = "";
        public string TueMat { get => _tueMat; set { _tueMat = value; OnPropertyChanged(); } }
        private string _tueWatermark = "";
        public string TueWatermark { get => _tueWatermark; set { if (_tueWatermark != value) { _tueWatermark = value; OnPropertyChanged(); } } }

        private string _wedClass = "";
        public string WedClass { get => _wedClass; set { _wedClass = value; OnPropertyChanged(); } }
        private string _wedMat = "";
        public string WedMat { get => _wedMat; set { _wedMat = value; OnPropertyChanged(); } }
        private string _wedWatermark = "";
        public string WedWatermark { get => _wedWatermark; set { if (_wedWatermark != value) { _wedWatermark = value; OnPropertyChanged(); } } }

        private string _thuClass = "";
        public string ThuClass { get => _thuClass; set { _thuClass = value; OnPropertyChanged(); } }
        private string _thuMat = "";
        public string ThuMat { get => _thuMat; set { _thuMat = value; OnPropertyChanged(); } }
        private string _thuWatermark = "";
        public string ThuWatermark { get => _thuWatermark; set { if (_thuWatermark != value) { _thuWatermark = value; OnPropertyChanged(); } } }

        private string _friClass = "";
        public string FriClass { get => _friClass; set { _friClass = value; OnPropertyChanged(); } }
        private string _friMat = "";
        public string FriMat { get => _friMat; set { _friMat = value; OnPropertyChanged(); } }
        private string _friWatermark = "";
        public string FriWatermark { get => _friWatermark; set { if (_friWatermark != value) { _friWatermark = value; OnPropertyChanged(); } } }
        private string _monNotes = "";
        public string MonNotes { get => _monNotes; set { _monNotes = value; OnPropertyChanged(); } }
        private string _tueNotes = "";
        public string TueNotes { get => _tueNotes; set { _tueNotes = value; OnPropertyChanged(); } }
        private string _wedNotes = "";
        public string WedNotes { get => _wedNotes; set { _wedNotes = value; OnPropertyChanged(); } }
        private string _thuNotes = "";
        public string ThuNotes { get => _thuNotes; set { _thuNotes = value; OnPropertyChanged(); } }
        private string _friNotes = "";
        public string FriNotes { get => _friNotes; set { _friNotes = value; OnPropertyChanged(); } }
    }

    public static class SmartTimeParser
    {
        public static bool TryParse(string input, out string result)
        {
            result = input;
            if (string.IsNullOrWhiteSpace(input)) { result = ""; return true; }
            input = input.Trim().Replace(" ", "").Replace(";", ":").Replace(",", ":").Replace(".", ":").ToLower();
            if (input.Contains("-"))
            {
                var parts = input.Split(new[] { '-' }, 2);
                if (TryParseSingle(parts[0], out string t1) && TryParseSingle(parts[1], out string t2)) { result = $"{t1} - {t2}"; return true; }
                return false; 
            }
            if (TryParseSingle(input, out string singleTime)) { result = singleTime; return true; }
            return false;
        }

        private static bool TryParseSingle(string val, out string parsed)
        {
            parsed = val;
            if (string.IsNullOrWhiteSpace(val)) return false;
            if (!System.Text.RegularExpressions.Regex.IsMatch(val, @"^[\d:]+$")) return false;
            int hours = 0, minutes = 0;
            if (val.Contains(":"))
            {
                var parts = val.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 2) return false;
                if (!int.TryParse(parts[0], out hours)) return false;
                if (parts.Length == 2)
                {
                    string minStr = parts[1];
                    if (minStr.Length == 1) minStr += "0"; 
                    if (minStr.Length > 2 || !int.TryParse(minStr, out minutes)) return false;
                }
            }
            else
            {
                if (val.Length > 4 || val.Length == 0) return false;
                if (val.Length <= 2) { hours = int.Parse(val); }
                else if (val.Length == 3) { hours = int.Parse(val.Substring(0, 1)); minutes = int.Parse(val.Substring(1, 2)); }
                else if (val.Length == 4) { hours = int.Parse(val.Substring(0, 2)); minutes = int.Parse(val.Substring(2, 2)); }
            }
            if (hours >= 0 && hours <= 23 && minutes >= 0 && minutes <= 59) { parsed = $"{hours:D2}:{minutes:D2}"; return true; }
            return false;
        }
    }

    public class DailyRowWrapper : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null!) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public ScheduleRow Row { get; }
        public DayOfWeek Day { get; }

        public DailyRowWrapper(ScheduleRow row, DayOfWeek day)
        {
            Row = row; Day = day;
            Row.PropertyChanged += (s, e) => { 
                if (e.PropertyName == nameof(Row.TimeSlot)) OnPropertyChanged(nameof(TimeSlot)); 
                if (e.PropertyName == nameof(Row.LessonNumber)) OnPropertyChanged(nameof(LessonNumber));
                
                // Nasłuchujemy zmian w historii ("Watermark") dla konkretnego dnia
                if (e.PropertyName != null && e.PropertyName.EndsWith("Watermark")) 
                    OnPropertyChanged(nameof(MaterialWatermark));
            };
        }

        public string LessonNumber => Row.LessonNumber;

        public string TimeSlot { get => Row.TimeSlot; set { Row.TimeSlot = value; OnPropertyChanged(); } }

        // Dynamiczne wyciąganie odpowiedniej historii
        public string MaterialWatermark
        {
            get => Day switch { DayOfWeek.Monday => Row.MonWatermark, DayOfWeek.Tuesday => Row.TueWatermark, DayOfWeek.Wednesday => Row.WedWatermark, DayOfWeek.Thursday => Row.ThuWatermark, DayOfWeek.Friday => Row.FriWatermark, _ => "" };
        }

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
        public string Notes
        {
            get => Day switch { DayOfWeek.Monday => Row.MonNotes, DayOfWeek.Tuesday => Row.TueNotes, DayOfWeek.Wednesday => Row.WedNotes, DayOfWeek.Thursday => Row.ThuNotes, DayOfWeek.Friday => Row.FriNotes, _ => "" };
            set { switch (Day) { case DayOfWeek.Monday: Row.MonNotes = value; break; case DayOfWeek.Tuesday: Row.TueNotes = value; break; case DayOfWeek.Wednesday: Row.WedNotes = value; break; case DayOfWeek.Thursday: Row.ThuNotes = value; break; case DayOfWeek.Friday: Row.FriNotes = value; break; } OnPropertyChanged(); }
        }
    }
}