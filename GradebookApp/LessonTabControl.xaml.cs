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
        
        private DateTime _currentDate = DateTime.Today;
        private readonly GradebookDataService _dataService;
        private bool _isInitializingData = true;

        public LessonTabControl()
        {
            InitializeComponent();
            _dataService = new GradebookDataService();
            SetupData();
        }

        private void SetupData()
        {
            var allItems = _dataService.GetFullSchedule();
            var timeSlots = allItems.Select(i => i.TimeSlot).Distinct().ToList();

            if (!timeSlots.Any()) timeSlots.Add("8:00 - 8:45");

            foreach (var slot in timeSlots)
            {
                var row = new ScheduleRow { TimeSlot = slot };
                
                var mon = allItems.FirstOrDefault(i => i.TimeSlot == slot && i.DayOfWeek == DayOfWeek.Monday);
                if (mon != null) { row.MonClass = mon.ClassName; row.MonMat = mon.Material; }
                
                var tue = allItems.FirstOrDefault(i => i.TimeSlot == slot && i.DayOfWeek == DayOfWeek.Tuesday);
                if (tue != null) { row.TueClass = tue.ClassName; row.TueMat = tue.Material; }

                var wed = allItems.FirstOrDefault(i => i.TimeSlot == slot && i.DayOfWeek == DayOfWeek.Wednesday);
                if (wed != null) { row.WedClass = wed.ClassName; row.WedMat = wed.Material; }

                var thu = allItems.FirstOrDefault(i => i.TimeSlot == slot && i.DayOfWeek == DayOfWeek.Thursday);
                if (thu != null) { row.ThuClass = thu.ClassName; row.ThuMat = thu.Material; }

                var fri = allItems.FirstOrDefault(i => i.TimeSlot == slot && i.DayOfWeek == DayOfWeek.Friday);
                if (fri != null) { row.FriClass = fri.ClassName; row.FriMat = fri.Material; }

                row.PropertyChanged += Row_PropertyChanged;
                _scheduleRows.Add(row);
            }

            WeeklyScheduleGrid.ItemsSource = _scheduleRows;
            DailyScheduleGrid.ItemsSource = _dailyRows;
            
            _isInitializingData = false;
            SkipWeekends();
            RefreshDailyView();
        }

        private void SaveToDatabase()
        {
            if (_isInitializingData) return;

            var items = new List<LessonScheduleItem>();
            int emptyCounter = 0;

            foreach (var row in _scheduleRows)
            {
                string ts = row.TimeSlot ?? "";
                if (string.IsNullOrEmpty(ts))
                {
                    ts = new string(' ', emptyCounter);
                    emptyCounter++;
                }

                bool hasData = false;

                if (!string.IsNullOrWhiteSpace(row.MonClass) || !string.IsNullOrWhiteSpace(row.MonMat)) { items.Add(new LessonScheduleItem { TimeSlot = ts, DayOfWeek = DayOfWeek.Monday, ClassName = row.MonClass ?? "", Material = row.MonMat ?? "" }); hasData = true; }
                if (!string.IsNullOrWhiteSpace(row.TueClass) || !string.IsNullOrWhiteSpace(row.TueMat)) { items.Add(new LessonScheduleItem { TimeSlot = ts, DayOfWeek = DayOfWeek.Tuesday, ClassName = row.TueClass ?? "", Material = row.TueMat ?? "" }); hasData = true; }
                if (!string.IsNullOrWhiteSpace(row.WedClass) || !string.IsNullOrWhiteSpace(row.WedMat)) { items.Add(new LessonScheduleItem { TimeSlot = ts, DayOfWeek = DayOfWeek.Wednesday, ClassName = row.WedClass ?? "", Material = row.WedMat ?? "" }); hasData = true; }
                if (!string.IsNullOrWhiteSpace(row.ThuClass) || !string.IsNullOrWhiteSpace(row.ThuMat)) { items.Add(new LessonScheduleItem { TimeSlot = ts, DayOfWeek = DayOfWeek.Thursday, ClassName = row.ThuClass ?? "", Material = row.ThuMat ?? "" }); hasData = true; }
                if (!string.IsNullOrWhiteSpace(row.FriClass) || !string.IsNullOrWhiteSpace(row.FriMat)) { items.Add(new LessonScheduleItem { TimeSlot = ts, DayOfWeek = DayOfWeek.Friday, ClassName = row.FriClass ?? "", Material = row.FriMat ?? "" }); hasData = true; }
                
                if (!hasData)
                {
                    items.Add(new LessonScheduleItem { TimeSlot = ts, DayOfWeek = DayOfWeek.Monday, ClassName = "", Material = "" });
                }
            }
            _dataService.SaveFullSchedule(items);
        }

        private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            SaveToDatabase();
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
                _dailyRows.Add(new DailyRowWrapper(row, _currentDate.DayOfWeek));
            }
        }

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
            var newRow = new ScheduleRow { TimeSlot = "" };
            newRow.PropertyChanged += Row_PropertyChanged;
            _scheduleRows.Insert(index, newRow);
            RefreshDailyView();
            SaveToDatabase();
        }

        private void DeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menu)
            {
                if (menu.DataContext is DailyRowWrapper daily) _scheduleRows.Remove(daily.Row);
                else if (menu.DataContext is ScheduleRow weekly) _scheduleRows.Remove(weekly);

                RefreshDailyView();
                SaveToDatabase();
            }
        }

        private void DailyViewBtn_Click(object sender, RoutedEventArgs e)
        {
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
            // Odczytujemy kontekst wiersza, z którego kliknięto przycisk "→"
            if (sender is Button btn && btn.DataContext is DailyRowWrapper row)
            {
                // Przekazujemy kontekst wiersza i aktualną datę wyświetlaną nad tabelą
                var window = new LessonInfoWindow(row, LessonDateText.Text) 
                { 
                    Owner = Window.GetWindow(this) 
                };
                window.ShowDialog();
            }
        }

        // ==============================
        // LOGIKA NAWIGACJI KLAWIATURĄ
        // ==============================

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
                        if (colIndex <= 0) // Zabezpieczenie lewej kolumny
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

                    if (rowIndex < 0) rowIndex = grid.Items.Count - 1; // Pętla góra-dół
                    if (rowIndex >= grid.Items.Count) rowIndex = 0;

                    if (TryFocusCell(grid, rowIndex, colIndex))
                        break;
                        
                    // Zabezpieczenie przed pętlą nieskończoną
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
                    return; // Umożliwia chodzenie po literach w TextBox

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

        // Fizycznie wymusza zaznaczenie pola, wykluczając rzucenie błędu
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
                        if (tb != null) // Pomija komórki bez TextBoxa (np. przycisk strzałki)
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

        // Bezpieczne wędrowanie po drzewie UI (odporne na NullReferenceException)
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

    // ===================================
    // MODELE DANYCH Z BAZY
    // ===================================

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