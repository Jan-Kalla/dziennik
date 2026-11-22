using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
            DefaultRoomTextBox.Text = Services.AppConfig.Settings.DefaultRoom ?? "";
        }

        private void LoadClassNames()
        {
            ClassNames.Clear();
            ClassNames.Add("Poprawy");
            ClassNames.Add("Konsultacje / Poprawy");

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

        private void SetupData(bool allowSkipWeekends = false)
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
            ApplyWeeklyFilter();
            _isInitializingData = false;
            if (allowSkipWeekends) SkipWeekends();
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

            // Automatyczna synchronizacja godzin na poprawy do chmury po zapisie planu
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var sync = new Services.CloudSyncService();
                    if (sync.IsConfigured) await sync.SyncTeacherScheduleToCloudAsync();
                }
                catch { }
            });
        }

        public void RefreshDailyView()
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

            var regularDailyRows = new List<DailyRowWrapper>();
            foreach (var row in _scheduleRows)
            {
                var wrapper = new DailyRowWrapper(row, _currentDate.DayOfWeek);
                // Uwzględniamy obecność Uwag (Notes) w warunku wyświetlania wiersza
                if (!string.IsNullOrWhiteSpace(wrapper.ClassName) || !string.IsNullOrWhiteSpace(wrapper.Material) || !string.IsNullOrWhiteSpace(wrapper.Notes) || row.ForceShow)
                {
                    regularDailyRows.Add(wrapper);
                }
            }

            var retakes = _dataService.GetPlannedRetakesForDate(_currentDate.Date);

            // Grupujemy poprawki na tę samą godzinę w jeden wiersz
            var retakesByTime = retakes
                .GroupBy(r => SmartTimeParser.ExtractStartTime(r.Time))
                .OrderBy(g => TimeSpan.TryParse(g.Key, out var ts) ? ts : TimeSpan.MaxValue)
                .ToList();

            var retakeWrappers = new List<DailyRowWrapper>();
            foreach (var group in retakesByTime)
            {
                string retakeStart = group.Key;
                var matchingRow = _scheduleRows.FirstOrDefault(r => SmartTimeParser.ExtractStartTime(r.TimeSlot) == retakeStart);
                string lessonNum = matchingRow?.LessonNumber ?? "";
                if (string.IsNullOrEmpty(lessonNum))
                {
                    matchingRow = _scheduleRows.FirstOrDefault(r => !string.IsNullOrEmpty(r.TimeSlot) && r.TimeSlot.Contains(retakeStart));
                    lessonNum = matchingRow?.LessonNumber ?? "";
                }

                retakeWrappers.Add(new DailyRowWrapper(group.ToList(), lessonNum, retakeStart));
            }

            // Zachowujemy kolejność z planu tygodniowego (_scheduleRows), aby dyżury nie spadały na dół!
            var finalDailyRows = new List<DailyRowWrapper>(regularDailyRows);

            // Dołączamy zaplanowane poprawki w odpowiednich miejscach
            foreach (var retakeWrapper in retakeWrappers)
            {
                string retakeStart = SmartTimeParser.ExtractStartTime(retakeWrapper.TimeSlot);
                TimeSpan.TryParse(retakeStart, out var retakeTime);

                // 1. Sprawdzamy, czy wiersz poprawki odpowiada konkretnej lekcji z planu tygodniowego
                var matchingScheduleRow = _scheduleRows.FirstOrDefault(r => 
                    !string.IsNullOrWhiteSpace(r.TimeSlot) && 
                    (SmartTimeParser.ExtractStartTime(r.TimeSlot) == retakeStart || r.TimeSlot.Contains(retakeWrapper.TimeSlot)));

                if (matchingScheduleRow != null)
                {
                    // Szukamy tego wiersza w widoku dziennym
                    int targetIdx = finalDailyRows.FindLastIndex(w => w.Row == matchingScheduleRow);
                    if (targetIdx >= 0)
                    {
                        finalDailyRows.Insert(targetIdx + 1, retakeWrapper);
                        continue;
                    }
                    else
                    {
                        // Lekcja nie jest widoczna dzisiaj, wstawiamy wg pozycji w szablonie tygodniowym
                        int schedIdx = _scheduleRows.IndexOf(matchingScheduleRow);
                        int insertIdx = finalDailyRows.FindIndex(w => w.Row != null && _scheduleRows.IndexOf(w.Row) > schedIdx);
                        if (insertIdx >= 0)
                        {
                            finalDailyRows.Insert(insertIdx, retakeWrapper);
                            continue;
                        }
                    }
                }

                // 2. Jeśli nie dopasowano do wiersza planu, wstawiamy chronologicznie wg czasu
                if (retakeTime > TimeSpan.Zero)
                {
                    int timeInsertIdx = finalDailyRows.FindIndex(w =>
                    {
                        string s = SmartTimeParser.ExtractStartTime(w.TimeSlot);
                        return TimeSpan.TryParse(s, out var slotTs) && slotTs > retakeTime;
                    });

                    if (timeInsertIdx >= 0)
                    {
                        finalDailyRows.Insert(timeInsertIdx, retakeWrapper);
                        continue;
                    }
                }

                // 3. W ostateczności na sam koniec
                finalDailyRows.Add(retakeWrapper);
            }

            // Ustalamy naprzemienne tła wierszy dla widoku dziennego
            var brushA = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252526"));
            var brushB = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2D2D30"));
            brushA.Freeze();
            brushB.Freeze();

            for (int i = 0; i < finalDailyRows.Count; i++)
            {
                finalDailyRows[i].RowBackgroundBrush = (i % 2 == 1) ? brushB : brushA;
                _dailyRows.Add(finalDailyRows[i]);
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

            var latestHistories = new Dictionary<string, string>();
            
            // Pobieramy całą historię i upewniamy się, że jest posortowana od najnowszego wpisu do najstarszego
            var allHistoricalMaterials = _dataService.GetAllScheduleMaterials().OrderByDescending(h => h.Date).ToList();

            foreach (var className in allUsedClasses)
            {
                string latestMaterial = "";
                
                // Szukamy absolutnie najświeższego (pierwszego w posortowanej liście) wpisu dla danej klasy w bieżącym tygodniu
                foreach (var row in _scheduleRows.Reverse()) // Patrzymy od końca tygodnia
                {
                    if (string.IsNullOrEmpty(latestMaterial))
                    {
                        if (row.FriClass == className && !string.IsNullOrWhiteSpace(row.FriMat)) latestMaterial = ExtractMat(row.FriMat);
                        else if (row.ThuClass == className && !string.IsNullOrWhiteSpace(row.ThuMat)) latestMaterial = ExtractMat(row.ThuMat);
                        else if (row.WedClass == className && !string.IsNullOrWhiteSpace(row.WedMat)) latestMaterial = ExtractMat(row.WedMat);
                        else if (row.TueClass == className && !string.IsNullOrWhiteSpace(row.TueMat)) latestMaterial = ExtractMat(row.TueMat);
                        else if (row.MonClass == className && !string.IsNullOrWhiteSpace(row.MonMat)) latestMaterial = ExtractMat(row.MonMat);
                    }
                }

                // Jeśli w bieżącym tygodniu nic nie wpisano, szukamy najnowszego wpisu w całej historii bazy
                if (string.IsNullOrEmpty(latestMaterial))
                {
                    var classTemplates = _dataService.GetLessonTemplates().Where(t => t.ClassName == className).ToList();
                    foreach (var histMat in allHistoricalMaterials)
                    {
                        // Pomijamy wpisy z bieżącego lub przyszłych tygodni, żeby przypadkiem "znak wodny" z przyszłości nie pojawił się wczoraj
                        if (histMat.Date >= GetMondayOfCurrentWeek()) continue;

                        var matchingTemplate = classTemplates.FirstOrDefault(t => t.DayOfWeek == histMat.Date.DayOfWeek && t.TimeSlot == histMat.TimeSlot);
                        if (matchingTemplate != null)
                        {
                            latestMaterial = ExtractMat(histMat.Material);
                            break; // Znaleźliśmy najświeższy przeszły materiał, przerywamy pętlę
                        }
                    }
                }

                latestHistories[className] = latestMaterial;
            }

            // Dystrybucja ostatniego tematu jako znaku wodnego
            foreach (var row in _scheduleRows)
            {
                row.MonWatermark = latestHistories.TryGetValue(row.MonClass ?? "", out var mon) ? mon : "";
                row.TueWatermark = latestHistories.TryGetValue(row.TueClass ?? "", out var tue) ? tue : "";
                row.WedWatermark = latestHistories.TryGetValue(row.WedClass ?? "", out var wed) ? wed : "";
                row.ThuWatermark = latestHistories.TryGetValue(row.ThuClass ?? "", out var thu) ? thu : "";
                row.FriWatermark = latestHistories.TryGetValue(row.FriClass ?? "", out var fri) ? fri : "";
            }
        }

        private string ExtractMat(string fullMat)
        {
            return fullMat?.Trim() ?? "";
        }

        private void HideDutiesCheckBox_Click(object sender, RoutedEventArgs e)
        {
            ApplyWeeklyFilter();
        }

        private void ApplyWeeklyFilter()
        {
            if (HideDutiesCheckBox == null) return;
            var view = CollectionViewSource.GetDefaultView(_scheduleRows);
            if (view == null) return;
            if (HideDutiesCheckBox.IsChecked == true)
            {
                view.Filter = item =>
                {
                    if (item is ScheduleRow row)
                    {
                        return !string.IsNullOrWhiteSpace(row.TimeSlot);
                    }
                    return true;
                };
            }
            else
            {
                view.Filter = null;
            }
            view.Refresh();
        }

        private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ScheduleRow.TimeSlot))
            {
                UpdateLessonNumbers();
                if (HideDutiesCheckBox?.IsChecked == true)
                {
                    CollectionViewSource.GetDefaultView(_scheduleRows)?.Refresh();
                }
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
        
        public void SetDailyViewMode()
        {
            DailyViewBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3E3E42"));
            DailyViewBtn.Foreground = Brushes.White;
            DailyViewBtn.FontWeight = FontWeights.Bold;

            WeeklyViewBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2D2D30"));
            WeeklyViewBtn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#AAAAAA"));
            WeeklyViewBtn.FontWeight = FontWeights.Normal;

            DailyScheduleGrid.Visibility = Visibility.Visible;
            DateNavigationPanel.Visibility = Visibility.Visible;
            if (DailyBottomPanel != null) DailyBottomPanel.Visibility = Visibility.Visible;
            WeeklyScheduleGrid.Visibility = Visibility.Collapsed;
            if (WeeklyHeaderPanel != null) WeeklyHeaderPanel.Visibility = Visibility.Collapsed;
        }

        public void NavigateToDate(DateTime targetDate)
        {
            _currentDate = targetDate.Date;
            SetDailyViewMode();
            _isInitializingData = true;
            SetupData(allowSkipWeekends: false);
        }

        // Aktualizacja widoku przy wyjściu z trybu tygodniowego z przeładowaniem
        private void DailyViewBtn_Click(object sender, RoutedEventArgs e)
        {
            SetDailyViewMode();
            
            // Wymuszamy pełne odświeżenie danych i filtrów
            _isInitializingData = true;
            SetupData();
        }

        private void AddRowAtTop_Click(object sender, RoutedEventArgs e)
        {
            if (Mouse.RightButton == MouseButtonState.Pressed) return;
            InsertNewRow(0);
        }

        private void AddRowBelow_Click(object sender, RoutedEventArgs e)
        {
            if (Mouse.RightButton == MouseButtonState.Pressed) return;
            if (sender is Button btn)
            {
                int index = GetRowIndex(btn);
                if (index >= 0) InsertNewRow(index + 1);
            }
        }

        private int GetRowIndex(Button btn)
        {
            if (btn.DataContext is DailyRowWrapper daily)
            {
                if (daily.Row != null) return _scheduleRows.IndexOf(daily.Row);
                return -1;
            }
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

        // Zdarzenie blokujące uruchamianie opcji w ContextMenu prawym przyciskiem myszy (tylko LPM powinien klikać menu)
        private void MenuItem_BlockRightClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private void DefaultRoomTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var newRoom = DefaultRoomTextBox.Text.Trim();
            if (Services.AppConfig.Settings.DefaultRoom != newRoom)
            {
                Services.AppConfig.Settings.DefaultRoom = newRoom;
                Services.AppConfig.SaveSettings(Services.AppConfig.Settings);
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var sync = new Services.CloudSyncService();
                        if (sync.IsConfigured) await sync.SyncTeacherScheduleToCloudAsync();
                    }
                    catch { }
                });
            }
        }

        // Bezkompromisowa obsługa PPM na komórce planu lekcji w widoku tygodniowym
        private void Cell_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            ComboBox? cb = sender as ComboBox;
            if (cb == null && sender is DependencyObject dep)
            {
                cb = GetVisualChild<ComboBox>(dep);
            }

            if (cb?.ContextMenu != null)
            {
                UpdateRetakeMenuItem(cb);
                cb.ContextMenu.PlacementTarget = cb;
                cb.ContextMenu.IsOpen = true;
                e.Handled = true; // Zawsze i bezwzględnie otwieramy menu popraw, blokując menu schowka
            }
        }

        private void CellContextMenu_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (sender is ComboBox cb)
            {
                UpdateRetakeMenuItem(cb);
            }
        }

        private void UpdateRetakeMenuItem(ComboBox cb)
        {
            if (cb.ContextMenu != null && cb.DataContext is ScheduleRow row)
            {
                var retakeItem = cb.ContextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Tag != null && !m.Tag.ToString()!.EndsWith("Clear"));
                if (retakeItem != null)
                {
                    string dayTag = retakeItem.Tag.ToString()!;
                    string currentVal = dayTag switch
                    {
                        "Mon" => row.MonClass,
                        "Tue" => row.TueClass,
                        "Wed" => row.WedClass,
                        "Thu" => row.ThuClass,
                        "Fri" => row.FriClass,
                        _ => ""
                    };

                    bool isRetake = !string.IsNullOrWhiteSpace(currentVal) && currentVal.Contains("popraw", StringComparison.OrdinalIgnoreCase);
                    if (isRetake)
                    {
                        retakeItem.Header = "✕ Usuń oznaczenie terminu popraw";
                        retakeItem.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 138, 128)); // Jasnoczerwony
                    }
                    else
                    {
                        retakeItem.Header = "🎯 Oznacz tę godzinę jako termin na poprawy";
                        retakeItem.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(77, 182, 172)); // Turkus
                    }
                }
            }
        }

        private void ToggleRetakeSlot_Click(object sender, RoutedEventArgs e)
        {
            if (Mouse.RightButton == MouseButtonState.Pressed) return;

            if (sender is MenuItem menuItem && menuItem.Tag != null && menuItem.DataContext is ScheduleRow row)
            {
                string tag = menuItem.Tag.ToString()!;
                bool isClear = tag.EndsWith("Clear");
                string day = isClear ? tag.Replace("Clear", "") : tag;

                Action<string> setVal = day switch
                {
                    "Mon" => val => row.MonClass = val,
                    "Tue" => val => row.TueClass = val,
                    "Wed" => val => row.WedClass = val,
                    "Thu" => val => row.ThuClass = val,
                    "Fri" => val => row.FriClass = val,
                    _ => _ => { }
                };

                Func<string> getVal = day switch
                {
                    "Mon" => () => row.MonClass,
                    "Tue" => () => row.TueClass,
                    "Wed" => () => row.WedClass,
                    "Thu" => () => row.ThuClass,
                    "Fri" => () => row.FriClass,
                    _ => () => ""
                };

                if (isClear)
                {
                    setVal("");
                }
                else
                {
                    string current = getVal();
                    if (!string.IsNullOrWhiteSpace(current) && current.Contains("popraw", StringComparison.OrdinalIgnoreCase))
                    {
                        setVal("");
                    }
                    else
                    {
                        setVal("Poprawy");
                    }
                }

                SaveToDatabase();
                RefreshDailyView();
            }
        }

        private void DeleteRow_Click(object sender, RoutedEventArgs e)
        {
            // Bezpieczeństwo: usunięcie wiersza może nastąpić WYŁĄCZNIE lewym przyciskiem myszy (LPM)
            if (Mouse.RightButton == MouseButtonState.Pressed) return;

            if (sender is MenuItem menu)
            {
                if (menu.DataContext is DailyRowWrapper daily)
                {
                    if (daily.IsRetake)
                    {
                        var sub = daily.RetakeItems.FirstOrDefault();
                        if (sub != null)
                        {
                            var confirm = MessageBox.Show(
                                $"Czy na pewno chcesz usunąć zaplanowaną poprawę '{sub.Material}'?",
                                "Potwierdzenie usunięcia", MessageBoxButton.YesNo, MessageBoxImage.Question);
                            if (confirm == MessageBoxResult.Yes)
                            {
                                _dataService.DeletePlannedRetake(sub.RetakeId);
                                RefreshDailyView();
                            }
                        }
                        return;
                    }

                    if (daily.Row != null) _scheduleRows.Remove(daily.Row);
                }
                else if (menu.DataContext is ScheduleRow weekly)
                {
                    _scheduleRows.Remove(weekly);
                }

                UpdateLessonNumbers();
                RefreshDailyView();
                SaveToDatabase();
            }
        }

        private void ViewRetakeDetails_Click(object sender, RoutedEventArgs e)
        {
            if (Mouse.RightButton == MouseButtonState.Pressed) return;
            if (sender is MenuItem menu && menu.DataContext is DailyRowWrapper daily && daily.IsRetake)
            {
                var sub = daily.RetakeItems.FirstOrDefault();
                if (sub != null) OpenRetakeDetails(sub);
            }
        }

        private void EditRetake_Click(object sender, RoutedEventArgs e)
        {
            if (Mouse.RightButton == MouseButtonState.Pressed) return;
            if (sender is MenuItem menu && menu.DataContext is DailyRowWrapper daily && daily.IsRetake)
            {
                var sub = daily.RetakeItems.FirstOrDefault();
                if (sub != null && new PlanRetakeDialog(null, sub.RetakeId) { Owner = Window.GetWindow(this) }.ShowDialog() == true)
                {
                    RefreshDailyView();
                }
            }
        }

        private void RetakeItemArrow_Click(object sender, RoutedEventArgs e)
        {
            if (Mouse.RightButton == MouseButtonState.Pressed) return;
            if (sender is Button btn && btn.DataContext is RetakeSubItem sub)
            {
                OpenRetakeDetails(sub);
            }
        }

        private void ViewRetakeItemDetails_Click(object sender, RoutedEventArgs e)
        {
            if (Mouse.RightButton == MouseButtonState.Pressed) return;
            if (sender is MenuItem menu && menu.DataContext is RetakeSubItem sub)
            {
                OpenRetakeDetails(sub);
            }
        }

        private void EditRetakeItem_Click(object sender, RoutedEventArgs e)
        {
            if (Mouse.RightButton == MouseButtonState.Pressed) return;
            if (sender is MenuItem menu && menu.DataContext is RetakeSubItem sub)
            {
                if (new PlanRetakeDialog(null, sub.RetakeId) { Owner = Window.GetWindow(this) }.ShowDialog() == true)
                {
                    RefreshDailyView();
                }
            }
        }

        private void DeleteRetakeItem_Click(object sender, RoutedEventArgs e)
        {
            if (Mouse.RightButton == MouseButtonState.Pressed) return;
            if (sender is MenuItem menu && menu.DataContext is RetakeSubItem sub)
            {
                var confirm = MessageBox.Show(
                    $"Czy na pewno chcesz usunąć zaplanowaną poprawę '{sub.Material}'?",
                    "Potwierdzenie usunięcia", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm == MessageBoxResult.Yes)
                {
                    _dataService.DeletePlannedRetake(sub.RetakeId);
                    RefreshDailyView();
                }
            }
        }

        private void OpenRetakeDetails(RetakeSubItem sub)
        {
            new RetakeDetailsDialog(sub.RetakeId, sub.WorkId, sub.Material, sub.AttendeeIds)
            {
                Owner = Window.GetWindow(this)
            }.ShowDialog();
            RefreshDailyView();
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
            if (WeeklyHeaderPanel != null) WeeklyHeaderPanel.Visibility = Visibility.Visible;
            DailyScheduleGrid.Visibility = Visibility.Collapsed;
            DateNavigationPanel.Visibility = Visibility.Collapsed;
            if (DailyBottomPanel != null) DailyBottomPanel.Visibility = Visibility.Collapsed;
            ApplyWeeklyFilter();
        }

        private void PlanRetake_Click(object sender, RoutedEventArgs e)
        {
            if (new PlanRetakeDialog() { Owner = Window.GetWindow(this) }.ShowDialog() == true)
            {
                RefreshDailyView();
                (Application.Current.MainWindow as MainWindow)?.RefreshGlobalCalendar();
            }
        }

        private void GoToLessonInfo_Click(object sender, RoutedEventArgs e)
        {
            if (Mouse.RightButton == MouseButtonState.Pressed) return;
            if (sender is Button btn && btn.DataContext is DailyRowWrapper row)
            {
                if (row.IsRetake && row.RetakeId.HasValue)
                {
                    new RetakeDetailsDialog(row.RetakeId.Value, row.WorkId ?? 0, row.Material, row.AttendeeIds ?? new List<int>())
                    {
                        Owner = Window.GetWindow(this)
                    }.ShowDialog();
                    RefreshDailyView();
                    return;
                }

                var window = new LessonInfoWindow(row, LessonDateText.Text, _currentDate) 
                { 
                    Owner = Window.GetWindow(this) 
                };
                window.ShowDialog();
                RefreshDailyView();
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

            // NOWOŚĆ: Zatwierdzanie Enterem
            if (e.Key == Key.Enter)
            {
                var textBox = e.OriginalSource as TextBox;
                if (textBox != null)
                {
                    e.Handled = true;
                    // Wymuszamy aktualizację tekstu powiązanego z bazą danych (LostFocus)
                    textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                    
                    // Po zatwierdzeniu przenosimy ostrość z powrotem na główną siatkę, by "zamknąć" edycję
                    grid.Focus();
                    return;
                }
            }

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
        public bool IsRetake => false;

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

        public static string ExtractStartTime(string timeSlot)
        {
            if (string.IsNullOrWhiteSpace(timeSlot)) return "";
            string firstPart = timeSlot.Contains('-') ? timeSlot.Split('-')[0].Trim() : timeSlot.Trim();
            if (TryParseSingle(firstPart, out string parsed)) return parsed;
            var match = System.Text.RegularExpressions.Regex.Match(firstPart, @"\b\d{1,2}:\d{2}\b");
            if (match.Success)
            {
                if (TryParseSingle(match.Value, out string mParsed)) return mParsed;
                return match.Value;
            }
            return firstPart;
        }

        public static List<string> GetLessonStartTimes(AppDbContext dbContext)
        {
            var times = new List<string>();
            var templates = dbContext.LessonTemplateItems.ToList();
            var distinctSlots = templates
                .Select(t => t.TimeSlot)
                .Where(ts => !string.IsNullOrWhiteSpace(ts))
                .Distinct()
                .ToList();

            foreach (var slot in distinctSlots)
            {
                string startTime = ExtractStartTime(slot);
                if (!string.IsNullOrWhiteSpace(startTime) && !times.Contains(startTime))
                {
                    times.Add(startTime);
                }
            }

            if (!times.Any())
            {
                times.AddRange(new[] { "08:00", "08:50", "09:40", "10:35", "11:30", "12:25", "13:20", "14:15", "15:10" });
            }
            else
            {
                times = times.OrderBy(t => TimeSpan.TryParse(t, out var ts) ? ts : TimeSpan.Zero).ToList();
            }

            return times;
        }

        public static bool TryParseSingle(string val, out string parsed)
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

        public ScheduleRow? Row { get; }
        public DayOfWeek Day { get; }

        public bool IsRetake { get; } = false;
        public int? RetakeId { get; set; }
        public int? WorkId { get; set; }
        public List<int>? AttendeeIds { get; set; }

        public ObservableCollection<RetakeSubItem> RetakeItems { get; set; } = new ObservableCollection<RetakeSubItem>();

        public bool ShowMultipleClasses => IsRetake && RetakeItems.Select(r => r.ClassName).Distinct().Count() > 1;

        private Brush _rowBackgroundBrush = Brushes.Transparent;
        public Brush RowBackgroundBrush 
        { 
            get => _rowBackgroundBrush; 
            set { _rowBackgroundBrush = value; OnPropertyChanged(); } 
        }

        private string _retakeLessonNumber = "";
        private string _retakeTimeSlot = "";
        private string _retakeClassName = "";
        private string _retakeMaterial = "";
        private string _retakeNotes = "";
        private string _retakeTooltip = "";

        public string RetakeTooltip => _retakeTooltip;

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

        public DailyRowWrapper(List<PlannedRetake> retakes, string lessonNumber, string timeSlot)
        {
            IsRetake = true;
            Row = null;
            Day = retakes.FirstOrDefault()?.Date.DayOfWeek ?? DayOfWeek.Monday;
            _retakeLessonNumber = lessonNumber;
            _retakeTimeSlot = timeSlot;

            for (int i = 0; i < retakes.Count; i++)
            {
                var retake = retakes[i];
                var subItem = new RetakeSubItem
                {
                    RetakeId = retake.Id,
                    WorkId = retake.WrittenWorkId,
                    AttendeeIds = retake.Attendees?.Select(a => a.StudentId).ToList() ?? new List<int>(),
                    ClassName = retake.WrittenWork?.SchoolClass?.Name ?? "",
                    Material = $"Poprawa: {retake.WrittenWork?.Title ?? ""}",
                    ShowBottomSeparator = (i < retakes.Count - 1)
                };

                var studentNames = retake.Attendees?
                    .Select(a => a.Student?.FullName)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Select(n => n!)
                    .ToList() ?? new List<string>();

                subItem.Notes = studentNames.Any() 
                    ? $"Uczniowie ({studentNames.Count}): {string.Join(", ", studentNames)}"
                    : "";

                string tt = $"Poprawa: {retake.WrittenWork?.Title ?? ""}\nKlasa: {subItem.ClassName}\nGodzina: {timeSlot}";
                if (studentNames.Any())
                {
                    tt += $"\n\nUczniowie ({studentNames.Count}):\n" + string.Join("\n", studentNames.Select(n => $"• {n}"));
                }
                subItem.Tooltip = tt;

                RetakeItems.Add(subItem);
            }

            var first = RetakeItems.FirstOrDefault();
            if (first != null)
            {
                RetakeId = first.RetakeId;
                WorkId = first.WorkId;
                AttendeeIds = first.AttendeeIds;
                _retakeClassName = first.ClassName;
                _retakeMaterial = first.Material;
                _retakeNotes = first.Notes;
                _retakeTooltip = first.Tooltip;
            }
        }

        public DailyRowWrapper(PlannedRetake retake, string lessonNumber)
            : this(new List<PlannedRetake> { retake }, lessonNumber, retake.Time)
        {
        }

        public string LessonNumber => IsRetake ? _retakeLessonNumber : (Row?.LessonNumber ?? "");

        public string TimeSlot 
        { 
            get => IsRetake ? _retakeTimeSlot : (Row?.TimeSlot ?? ""); 
            set 
            { 
                if (IsRetake) { _retakeTimeSlot = value; OnPropertyChanged(); }
                else if (Row != null) { Row.TimeSlot = value; OnPropertyChanged(); }
            } 
        }

        // Dynamiczne wyciąganie odpowiedniej historii
        public string MaterialWatermark
        {
            get => IsRetake ? "" : (Row == null ? "" : (Day switch { DayOfWeek.Monday => Row.MonWatermark, DayOfWeek.Tuesday => Row.TueWatermark, DayOfWeek.Wednesday => Row.WedWatermark, DayOfWeek.Thursday => Row.ThuWatermark, DayOfWeek.Friday => Row.FriWatermark, _ => "" }));
        }

        public string ClassName
        {
            get => IsRetake ? _retakeClassName : (Row == null ? "" : (Day switch { DayOfWeek.Monday => Row.MonClass, DayOfWeek.Tuesday => Row.TueClass, DayOfWeek.Wednesday => Row.WedClass, DayOfWeek.Thursday => Row.ThuClass, DayOfWeek.Friday => Row.FriClass, _ => "" }));
            set 
            { 
                if (IsRetake) { _retakeClassName = value; OnPropertyChanged(); }
                else if (Row != null) { switch (Day) { case DayOfWeek.Monday: Row.MonClass = value; break; case DayOfWeek.Tuesday: Row.TueClass = value; break; case DayOfWeek.Wednesday: Row.WedClass = value; break; case DayOfWeek.Thursday: Row.ThuClass = value; break; case DayOfWeek.Friday: Row.FriClass = value; break; } OnPropertyChanged(); }
            }
        }

        public string Material
        {
            get => IsRetake ? _retakeMaterial : (Row == null ? "" : (Day switch { DayOfWeek.Monday => Row.MonMat, DayOfWeek.Tuesday => Row.TueMat, DayOfWeek.Wednesday => Row.WedMat, DayOfWeek.Thursday => Row.ThuMat, DayOfWeek.Friday => Row.FriMat, _ => "" }));
            set 
            { 
                if (IsRetake) { _retakeMaterial = value; OnPropertyChanged(); }
                else if (Row != null) { switch (Day) { case DayOfWeek.Monday: Row.MonMat = value; break; case DayOfWeek.Tuesday: Row.TueMat = value; break; case DayOfWeek.Wednesday: Row.WedMat = value; break; case DayOfWeek.Thursday: Row.ThuMat = value; break; case DayOfWeek.Friday: Row.FriMat = value; break; } OnPropertyChanged(); }
            }
        }
        public string Notes
        {
            get => IsRetake ? _retakeNotes : (Row == null ? "" : (Day switch { DayOfWeek.Monday => Row.MonNotes, DayOfWeek.Tuesday => Row.TueNotes, DayOfWeek.Wednesday => Row.WedNotes, DayOfWeek.Thursday => Row.ThuNotes, DayOfWeek.Friday => Row.FriNotes, _ => "" }));
            set 
            { 
                if (IsRetake) { _retakeNotes = value; OnPropertyChanged(); }
                else if (Row != null) { switch (Day) { case DayOfWeek.Monday: Row.MonNotes = value; break; case DayOfWeek.Tuesday: Row.TueNotes = value; break; case DayOfWeek.Wednesday: Row.WedNotes = value; break; case DayOfWeek.Thursday: Row.ThuNotes = value; break; case DayOfWeek.Friday: Row.FriNotes = value; break; } OnPropertyChanged(); }
            }
        }
    }

    public class RetakeSubItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null!) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public int RetakeId { get; set; }
        public int WorkId { get; set; }
        public List<int> AttendeeIds { get; set; } = new List<int>();
        public string ClassName { get; set; } = "";
        public string Material { get; set; } = "";
        public string Notes { get; set; } = "";
        public string Tooltip { get; set; } = "";
        
        private bool _showBottomSeparator = false;
        public bool ShowBottomSeparator
        {
            get => _showBottomSeparator;
            set { _showBottomSeparator = value; OnPropertyChanged(); OnPropertyChanged(nameof(SeparatorVisibility)); }
        }

        public Visibility SeparatorVisibility => _showBottomSeparator ? Visibility.Visible : Visibility.Collapsed;
    }
}