using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GradebookApp.Services;

namespace GradebookApp
{
    public class NonWorkingDayRuleViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string TypeDisplay { get; set; } = string.Empty;
        public string DateDisplay { get; set; } = string.Empty;
        public NonWorkingDayType RuleType { get; set; }
    }

    public partial class NonWorkingDaysWindow : Window
    {
        private readonly GradebookDataService _dataService;
        private int _schoolYearStart; // np. 2026 dla roku 2026/2027
        private int _currentDisplayYear;
        private int _currentDisplayMonth;
        private bool _isUpdatingWeeklyCheckboxes = false;
        private bool _isUpdatingDatePickers = false;

        // Stan interaktywnego zaznaczania okresu na kalendarzu
        private DateTime? _rangeStartDate = null;
        private DateTime? _rangeEndDate = null;

        // 10 miesięcy roku szkolnego (od września do czerwca, bez wakacji lipiec-sierpień)
        private readonly int[] _schoolMonths = new int[] { 9, 10, 11, 12, 1, 2, 3, 4, 5, 6 };

        public ObservableCollection<NonWorkingDayRuleViewModel> RulesList { get; set; } = new ObservableCollection<NonWorkingDayRuleViewModel>();

        // Wybrana data do nawigacji w głównym oknie (po dwukrotnym kliknięciu w dzień)
        public DateTime? SelectedLessonDateToNavigate { get; private set; }

        public NonWorkingDaysWindow()
        {
            InitializeComponent();
            _dataService = new GradebookDataService();
            _dataService.ApplyMigrations(); // upewniamy się, że tabela w SQLite istnieje

            RulesDataGrid.ItemsSource = RulesList;

            // Ustalenie bieżącego roku szkolnego
            var today = DateTime.Today;
            _schoolYearStart = SchoolCalendarService.GetSchoolYearStartYear(today);
            _currentDisplayMonth = today.Month;

            // Jeśli dzisiaj są wakacje (lipiec lub sierpień), domyślnie startujemy od września
            if (_currentDisplayMonth == 7 || _currentDisplayMonth == 8)
            {
                _currentDisplayMonth = 9;
                _currentDisplayYear = _schoolYearStart;
            }
            else
            {
                _currentDisplayYear = _currentDisplayMonth >= 9 ? _schoolYearStart : _schoolYearStart + 1;
            }

            // Inicjalizacja miesięcy w ComboBox dla reguł corocznych
            for (int m = 1; m <= 12; m++)
            {
                RecurringMonthComboBox.Items.Add(new ComboBoxItem 
                { 
                    Content = $"{m:00} - {SchoolCalendarService.GetPolishMonthName(m)}", 
                    Tag = m 
                });
            }
            RecurringMonthComboBox.SelectedIndex = today.Month - 1;
            RecurringDayTextBox.Text = today.Day.ToString();

            _isUpdatingDatePickers = true;
            SingleDatePicker.SelectedDate = today;
            RangeStartDatePicker.SelectedDate = null;
            RangeEndDatePicker.SelectedDate = null;
            _isUpdatingDatePickers = false;

            LoadRules();
            RefreshCalendarView();
            RefreshYearStats();
        }

        private void LoadRules()
        {
            RulesList.Clear();
            var rules = _dataService.GetAllCalendarRules();

            // Aktualizacja checkboxów stałych dni tygodnia
            _isUpdatingWeeklyCheckboxes = true;
            MondayCheckBox.IsChecked = rules.Any(r => r.RuleType == NonWorkingDayType.WeeklyDay && r.DayOfWeek == DayOfWeek.Monday);
            TuesdayCheckBox.IsChecked = rules.Any(r => r.RuleType == NonWorkingDayType.WeeklyDay && r.DayOfWeek == DayOfWeek.Tuesday);
            WednesdayCheckBox.IsChecked = rules.Any(r => r.RuleType == NonWorkingDayType.WeeklyDay && r.DayOfWeek == DayOfWeek.Wednesday);
            ThursdayCheckBox.IsChecked = rules.Any(r => r.RuleType == NonWorkingDayType.WeeklyDay && r.DayOfWeek == DayOfWeek.Thursday);
            FridayCheckBox.IsChecked = rules.Any(r => r.RuleType == NonWorkingDayType.WeeklyDay && r.DayOfWeek == DayOfWeek.Friday);
            _isUpdatingWeeklyCheckboxes = false;

            foreach (var r in rules.OrderBy(r => r.RuleType).ThenBy(r => r.Name))
            {
                string typeStr = r.RuleType switch
                {
                    NonWorkingDayType.SingleDay => "Pojedynczy",
                    NonWorkingDayType.DateRange => "Okres",
                    NonWorkingDayType.RecurringDateRange => "Okres coroczny",
                    NonWorkingDayType.RecurringYearly => "Dzień co roku",
                    NonWorkingDayType.WeeklyDay => "Stały dzień tyg.",
                    _ => "Inny"
                };

                string dateStr = "-";
                if (r.RuleType == NonWorkingDayType.SingleDay && r.StartDate.HasValue)
                {
                    dateStr = r.StartDate.Value.ToString("dd.MM.yyyy");
                }
                else if (r.RuleType == NonWorkingDayType.DateRange && r.StartDate.HasValue)
                {
                    var end = r.EndDate ?? r.StartDate.Value;
                    dateStr = $"{r.StartDate.Value:dd.MM.yyyy} - {end:dd.MM.yyyy}";
                }
                else if (r.RuleType == NonWorkingDayType.RecurringDateRange && r.Day.HasValue && r.Month.HasValue && r.EndDay.HasValue && r.EndMonth.HasValue)
                {
                    dateStr = $"{r.Day.Value:00}.{r.Month.Value:00} - {r.EndDay.Value:00}.{r.EndMonth.Value:00} (co roku)";
                }
                else if (r.RuleType == NonWorkingDayType.RecurringYearly && r.Day.HasValue && r.Month.HasValue)
                {
                    dateStr = $"{r.Day.Value:00}.{r.Month.Value:00} (każdy rok)";
                }
                else if (r.RuleType == NonWorkingDayType.WeeklyDay && r.DayOfWeek.HasValue)
                {
                    dateStr = $"Każdy {SchoolCalendarService.GetPolishDayName(r.DayOfWeek.Value).ToLower()}";
                }

                RulesList.Add(new NonWorkingDayRuleViewModel
                {
                    Id = r.Id,
                    Name = r.Name,
                    TypeDisplay = typeStr,
                    DateDisplay = dateStr,
                    RuleType = r.RuleType
                });
            }
        }

        private void RefreshCalendarView()
        {
            SchoolYearTitleText.Text = $"ROK SZKOLNY {_schoolYearStart} / {_schoolYearStart + 1}";
            CurrentMonthTitleText.Text = $"{SchoolCalendarService.GetPolishMonthName(_currentDisplayMonth)} {_currentDisplayYear}";

            UpdateMonthButtonsHighlight();

            DaysUniformGrid.Children.Clear();

            var firstOfMonth = new DateTime(_currentDisplayYear, _currentDisplayMonth, 1);
            int daysInMonth = DateTime.DaysInMonth(_currentDisplayYear, _currentDisplayMonth);

            // Obliczenie przesunięcia pierwszego dnia (Poniedziałek = 0, Niedziela = 6)
            int startOffset = ((int)firstOfMonth.DayOfWeek + 6) % 7;

            var activeRules = SchoolCalendarService.GetRules();

            // 1. Dni z poprzedniego miesiąca (KLIKALNE!)
            var prevMonthDate = firstOfMonth.AddMonths(-1);
            int daysInPrevMonth = DateTime.DaysInMonth(prevMonthDate.Year, prevMonthDate.Month);
            for (int i = 0; i < startOffset; i++)
            {
                int dayNum = daysInPrevMonth - startOffset + i + 1;
                var prevDate = new DateTime(prevMonthDate.Year, prevMonthDate.Month, dayNum);
                var cell = CreateDayCell(dayNum, false, prevDate, activeRules);
                DaysUniformGrid.Children.Add(cell);
            }

            // 2. Dni bieżącego miesiąca
            for (int day = 1; day <= daysInMonth; day++)
            {
                var date = new DateTime(_currentDisplayYear, _currentDisplayMonth, day);
                var cell = CreateDayCell(day, true, date, activeRules);
                DaysUniformGrid.Children.Add(cell);
            }

            // 3. Dopełnienie do 42 komórek (6 wierszy x 7 kolumn) dniami z kolejnego miesiąca (KLIKALNE!)
            int filled = startOffset + daysInMonth;
            int remaining = 42 - filled;
            var nextMonthDate = firstOfMonth.AddMonths(1);
            for (int i = 1; i <= remaining; i++)
            {
                var nextDate = new DateTime(nextMonthDate.Year, nextMonthDate.Month, i);
                var cell = CreateDayCell(i, false, nextDate, activeRules);
                DaysUniformGrid.Children.Add(cell);
            }
        }

        private void UpdateMonthButtonsHighlight()
        {
            HighlightButton(BtnMonth9, _currentDisplayMonth == 9);
            HighlightButton(BtnMonth10, _currentDisplayMonth == 10);
            HighlightButton(BtnMonth11, _currentDisplayMonth == 11);
            HighlightButton(BtnMonth12, _currentDisplayMonth == 12);
            HighlightButton(BtnMonth1, _currentDisplayMonth == 1);
            HighlightButton(BtnMonth2, _currentDisplayMonth == 2);
            HighlightButton(BtnMonth3, _currentDisplayMonth == 3);
            HighlightButton(BtnMonth4, _currentDisplayMonth == 4);
            HighlightButton(BtnMonth5, _currentDisplayMonth == 5);
            HighlightButton(BtnMonth6, _currentDisplayMonth == 6);
        }

        private void HighlightButton(Button? btn, bool isSelected)
        {
            if (btn == null) return;
            if (isSelected)
            {
                btn.Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)); // Aktywny niebieski akcent
                btn.FontWeight = FontWeights.Bold;
            }
            else
            {
                btn.Background = new SolidColorBrush(Color.FromRgb(51, 51, 55));
                btn.FontWeight = FontWeights.Normal;
            }
        }

        private UIElement CreateDayCell(int dayNumber, bool isCurrentMonth, DateTime date, List<NonWorkingDayRule>? rules = null)
        {
            var border = new Border
            {
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1)
            };

            var grid = new Grid();
            var textBlock = new TextBlock
            {
                Text = dayNumber.ToString(),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold
            };
            grid.Children.Add(textBlock);
            border.Child = grid;

            var d = date.Date;
            bool isWorking = SchoolCalendarService.IsWorkingDay(d, rules);
            string? reason = SchoolCalendarService.GetNonWorkingDayReason(d, rules);
            bool isToday = d == DateTime.Today;

            // Sprawdzamy stan interaktywnego zaznaczenia na kalendarzu
            bool isSelectedSingle = SingleDayRadio.IsChecked == true && SingleDatePicker.SelectedDate.HasValue && SingleDatePicker.SelectedDate.Value.Date == d;
            bool isSelectedRangeStart = DateRangeRadio.IsChecked == true && _rangeStartDate.HasValue && _rangeStartDate.Value.Date == d;
            bool isSelectedRangeEnd = DateRangeRadio.IsChecked == true && _rangeEndDate.HasValue && _rangeEndDate.Value.Date == d;
            bool isInSelectedRange = DateRangeRadio.IsChecked == true && _rangeStartDate.HasValue && _rangeEndDate.HasValue &&
                                     d >= _rangeStartDate.Value.Date && d <= _rangeEndDate.Value.Date;

            // 1. ZAZNACZENIE MA NAJWYŻSZY PRIORYTET WIZUALNY
            if (isSelectedRangeStart || isSelectedRangeEnd)
            {
                // Wyraźny neonowy turkus / akcent zaznaczenia krańców okresu
                border.Background = new SolidColorBrush(Color.FromRgb(26, 75, 102)); // #1A4B66
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 229, 255)); // #00E5FF
                border.BorderThickness = new Thickness(2.5);
                textBlock.Foreground = new SolidColorBrush(Colors.White);
                textBlock.FontWeight = FontWeights.Bold;

                string role = isSelectedRangeStart && isSelectedRangeEnd ? "[POCZĄTEK I KONIEC OKRESU]" :
                              isSelectedRangeStart && !_rangeEndDate.HasValue ? "[POCZĄTEK OKRESU (OD)] — Kliknij datę zakończenia" :
                              isSelectedRangeStart ? "[POCZĄTEK OKRESU (OD)]" : "[KONIEC OKRESU (DO)]";
                border.ToolTip = $"{d:dd.MM.yyyy} — {role}";
            }
            else if (isInSelectedRange)
            {
                // Dni wewnątrz wybranego zakresu
                border.Background = new SolidColorBrush(Color.FromRgb(20, 52, 70)); // #143446
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 176, 255)); // #00B0FF
                border.BorderThickness = new Thickness(1.5);
                textBlock.Foreground = new SolidColorBrush(Color.FromRgb(224, 247, 250));
                border.ToolTip = $"{d:dd.MM.yyyy} — [W ZAZNACZONYM OKRESIE]";
            }
            else if (isSelectedSingle)
            {
                // Pojedynczy wybrany dzień
                border.Background = new SolidColorBrush(Color.FromRgb(26, 75, 102));
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 229, 255));
                border.BorderThickness = new Thickness(2.5);
                textBlock.Foreground = new SolidColorBrush(Colors.White);
                textBlock.FontWeight = FontWeights.Bold;
                border.ToolTip = $"{d:dd.MM.yyyy} — [WYBRANY DZIEŃ]";
            }
            else
            {
                // Standardowy wygląd dnia roboczego / wolnego
                if (isToday)
                {
                    border.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 183, 77)); // Pomarańczowy dzisiaj
                    border.BorderThickness = new Thickness(2);
                }
                else
                {
                    border.BorderBrush = new SolidColorBrush(isCurrentMonth ? Color.FromRgb(55, 55, 60) : Color.FromRgb(40, 40, 44));
                }

                if (!isWorking)
                {
                    // Dzień wolny / święto / weekend
                    border.Background = new SolidColorBrush(isCurrentMonth ? Color.FromRgb(90, 26, 26) : Color.FromRgb(60, 20, 20));
                    textBlock.Foreground = new SolidColorBrush(isCurrentMonth ? Color.FromRgb(255, 205, 210) : Color.FromRgb(180, 140, 140));
                    border.ToolTip = $"{d:dd.MM.yyyy}{(!isCurrentMonth ? " (Sąsiedni miesiąc)" : "")} — {reason}";
                }
                else
                {
                    // Dzień roboczy
                    border.Background = new SolidColorBrush(isCurrentMonth ? Color.FromRgb(45, 45, 48) : Color.FromRgb(32, 32, 35));
                    textBlock.Foreground = new SolidColorBrush(isCurrentMonth ? Color.FromRgb(224, 224, 224) : Color.FromRgb(120, 120, 120));
                    border.ToolTip = $"{d:dd.MM.yyyy}{(!isCurrentMonth ? " (Sąsiedni miesiąc)" : "")} (Dzień roboczy)";
                }
            }

            // Dni są ZAWSZE KLIKALNE (zarówno z bieżącego, jak i sąsiedniego miesiąca!)
            border.Cursor = System.Windows.Input.Cursors.Hand;
            border.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    e.Handled = true;
                    DayDoubleClicked(d);
                }
            };
            border.MouseLeftButtonUp += (s, e) =>
            {
                if (e.ClickCount < 2)
                {
                    DayClicked(d);
                }
            };

            if (border.ToolTip is string tip)
            {
                border.ToolTip = $"{tip}\n💡 Kliknij dwukrotnie, aby otworzyć ten dzień w zakładce Lekcja";
            }

            return border;
        }

        private void DayDoubleClicked(DateTime d)
        {
            SelectedLessonDateToNavigate = d.Date;
            Close();
        }

        private void DayClicked(DateTime d)
        {
            if (SingleDayRadio.IsChecked == true)
            {
                _isUpdatingDatePickers = true;
                SingleDatePicker.SelectedDate = d;
                _isUpdatingDatePickers = false;

                _rangeStartDate = null;
                _rangeEndDate = null;

                string? reason = SchoolCalendarService.GetNonWorkingDayReason(d);
                if (!string.IsNullOrEmpty(reason) && reason != "Sobota (weekend)" && reason != "Niedziela (weekend)")
                {
                    RuleNameTextBox.Text = reason;
                }
                else if (string.IsNullOrWhiteSpace(RuleNameTextBox.Text))
                {
                    RuleNameTextBox.Text = "Dzień wolny";
                }

                RefreshCalendarView();
            }
            else if (DateRangeRadio.IsChecked == true)
            {
                if (_rangeStartDate == null || _rangeEndDate != null)
                {
                    // KROK 1: Wybór daty początkowej (Od)
                    _rangeStartDate = d.Date;
                    _rangeEndDate = null;

                    _isUpdatingDatePickers = true;
                    RangeStartDatePicker.SelectedDate = d.Date;
                    RangeEndDatePicker.SelectedDate = null;
                    _isUpdatingDatePickers = false;

                    if (string.IsNullOrWhiteSpace(RuleNameTextBox.Text))
                    {
                        RuleNameTextBox.Text = "Okres wolny";
                    }
                }
                else // _rangeStartDate != null && _rangeEndDate == null
                {
                    if (d.Date >= _rangeStartDate.Value.Date)
                    {
                        // KROK 2: Wybór daty końcowej (Do) - data jest późniejsza lub równa
                        _rangeEndDate = d.Date;

                        _isUpdatingDatePickers = true;
                        RangeEndDatePicker.SelectedDate = d.Date;
                        _isUpdatingDatePickers = false;
                    }
                    else
                    {
                        // Użytkownik kliknął datę wcześniejszą niż data początkowa -> ustawiamy jako nowy początek!
                        _rangeStartDate = d.Date;
                        _rangeEndDate = null;

                        _isUpdatingDatePickers = true;
                        RangeStartDatePicker.SelectedDate = d.Date;
                        RangeEndDatePicker.SelectedDate = null;
                        _isUpdatingDatePickers = false;
                    }
                }

                RefreshCalendarView();
            }
            else if (RecurringRadio.IsChecked == true)
            {
                RecurringDayTextBox.Text = d.Day.ToString();
                RecurringMonthComboBox.SelectedIndex = d.Month - 1;

                _rangeStartDate = null;
                _rangeEndDate = null;

                string? reason = SchoolCalendarService.GetNonWorkingDayReason(d);
                if (!string.IsNullOrEmpty(reason) && reason != "Sobota (weekend)" && reason != "Niedziela (weekend)")
                {
                    RuleNameTextBox.Text = reason;
                }
                else if (string.IsNullOrWhiteSpace(RuleNameTextBox.Text))
                {
                    RuleNameTextBox.Text = "Święto";
                }

                RefreshCalendarView();
            }
        }

        private void SingleDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingDatePickers) return;
            RefreshCalendarView();
        }

        private void RangeDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingDatePickers) return;
            _rangeStartDate = RangeStartDatePicker.SelectedDate;
            _rangeEndDate = RangeEndDatePicker.SelectedDate;
            RefreshCalendarView();
        }

        private void RefreshYearStats()
        {
            var rules = SchoolCalendarService.GetRules();
            // Statystyki zliczają dni w okresie nauki szkolnej (od 1 września do 30 czerwca)
            var schoolYearStart = new DateTime(_schoolYearStart, 9, 1);
            var schoolYearEnd = new DateTime(_schoolYearStart + 1, 6, 30);

            int workingDays = 0;
            int totalFreeDays = 0;
            int weekendDays = 0;

            for (var d = schoolYearStart; d <= schoolYearEnd; d = d.AddDays(1))
            {
                if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday)
                {
                    weekendDays++;
                    totalFreeDays++;
                }
                else if (!SchoolCalendarService.IsWorkingDay(d, rules))
                {
                    totalFreeDays++;
                }
                else
                {
                    workingDays++;
                }
            }

            StatsSummaryText.Text = $"Dni nauki (Wrz - Cze): Robocze: {workingDays} | Wolne: {totalFreeDays} (w tym weekendy: {weekendDays})";
        }

        // --- NAWIGACJA ROKU I MIESIĄCA ---

        private void PrevSchoolYear_Click(object sender, RoutedEventArgs e)
        {
            _schoolYearStart--;
            _currentDisplayYear = _currentDisplayMonth >= 9 ? _schoolYearStart : _schoolYearStart + 1;
            RefreshCalendarView();
            RefreshYearStats();
        }

        private void NextSchoolYear_Click(object sender, RoutedEventArgs e)
        {
            _schoolYearStart++;
            _currentDisplayYear = _currentDisplayMonth >= 9 ? _schoolYearStart : _schoolYearStart + 1;
            RefreshCalendarView();
            RefreshYearStats();
        }

        private void MonthTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int month))
            {
                _currentDisplayMonth = month;
                _currentDisplayYear = month >= 9 ? _schoolYearStart : _schoolYearStart + 1;
                RefreshCalendarView();
            }
        }

        private void PrevMonth_Click(object sender, RoutedEventArgs e)
        {
            int idx = Array.IndexOf(_schoolMonths, _currentDisplayMonth);
            if (idx > 0)
            {
                _currentDisplayMonth = _schoolMonths[idx - 1];
                _currentDisplayYear = _currentDisplayMonth >= 9 ? _schoolYearStart : _schoolYearStart + 1;
            }
            else
            {
                // Przejście do czerwca poprzedniego roku szkolnego
                _schoolYearStart--;
                _currentDisplayMonth = 6;
                _currentDisplayYear = _schoolYearStart + 1;
                RefreshYearStats();
            }
            RefreshCalendarView();
        }

        private void NextMonth_Click(object sender, RoutedEventArgs e)
        {
            int idx = Array.IndexOf(_schoolMonths, _currentDisplayMonth);
            if (idx >= 0 && idx < _schoolMonths.Length - 1)
            {
                _currentDisplayMonth = _schoolMonths[idx + 1];
                _currentDisplayYear = _currentDisplayMonth >= 9 ? _schoolYearStart : _schoolYearStart + 1;
            }
            else
            {
                // Przejście do września kolejnego roku szkolnego
                _schoolYearStart++;
                _currentDisplayMonth = 9;
                _currentDisplayYear = _schoolYearStart;
                RefreshYearStats();
            }
            RefreshCalendarView();
        }

        // --- ZARZĄDZANIE REGUŁAMI ---

        private void RuleType_Changed(object sender, RoutedEventArgs e)
        {
            if (SingleDayPanel == null || DateRangePanel == null || RecurringPanel == null) return;

            SingleDayPanel.Visibility = SingleDayRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            DateRangePanel.Visibility = DateRangeRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            RecurringPanel.Visibility = RecurringRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

            if (DateRangeRadio.IsChecked == true)
            {
                // Użytkownik wybrał tryb okresu:
                // Program wpisuje tylko datę rozpoczęcia (z wybranego wcześniej dnia lub dzisiejszego) i podświetla ją na kalendarzu.
                DateTime startDate;
                if (SingleDatePicker.SelectedDate.HasValue &&
                    SingleDatePicker.SelectedDate.Value.Year == _currentDisplayYear &&
                    SingleDatePicker.SelectedDate.Value.Month == _currentDisplayMonth)
                {
                    startDate = SingleDatePicker.SelectedDate.Value.Date;
                }
                else if (DateTime.Today.Year == _currentDisplayYear && DateTime.Today.Month == _currentDisplayMonth)
                {
                    startDate = DateTime.Today;
                }
                else
                {
                    startDate = new DateTime(_currentDisplayYear, _currentDisplayMonth, 1);
                }

                _rangeStartDate = startDate;
                _rangeEndDate = null;

                _isUpdatingDatePickers = true;
                RangeStartDatePicker.SelectedDate = _rangeStartDate;
                RangeEndDatePicker.SelectedDate = null;
                _isUpdatingDatePickers = false;

                if (string.IsNullOrWhiteSpace(RuleNameTextBox.Text))
                {
                    RuleNameTextBox.Text = "Okres wolny";
                }
            }
            else if (SingleDayRadio.IsChecked == true)
            {
                _rangeStartDate = null;
                _rangeEndDate = null;
                _isUpdatingDatePickers = true;
                RangeStartDatePicker.SelectedDate = null;
                RangeEndDatePicker.SelectedDate = null;
                _isUpdatingDatePickers = false;
            }
            else if (RecurringRadio.IsChecked == true)
            {
                _rangeStartDate = null;
                _rangeEndDate = null;
                _isUpdatingDatePickers = true;
                RangeStartDatePicker.SelectedDate = null;
                RangeEndDatePicker.SelectedDate = null;
                _isUpdatingDatePickers = false;
            }

            RefreshCalendarView();
        }

        private void ClearRangeSelection_Click(object sender, RoutedEventArgs e)
        {
            _rangeStartDate = null;
            _rangeEndDate = null;
            _isUpdatingDatePickers = true;
            RangeStartDatePicker.SelectedDate = null;
            RangeEndDatePicker.SelectedDate = null;
            _isUpdatingDatePickers = false;
            RefreshCalendarView();
        }

        private void WeeklyDay_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingWeeklyCheckboxes) return;

            _dataService.SetWeeklyNonWorkingDay(DayOfWeek.Monday, MondayCheckBox.IsChecked == true);
            _dataService.SetWeeklyNonWorkingDay(DayOfWeek.Tuesday, TuesdayCheckBox.IsChecked == true);
            _dataService.SetWeeklyNonWorkingDay(DayOfWeek.Wednesday, WednesdayCheckBox.IsChecked == true);
            _dataService.SetWeeklyNonWorkingDay(DayOfWeek.Thursday, ThursdayCheckBox.IsChecked == true);
            _dataService.SetWeeklyNonWorkingDay(DayOfWeek.Friday, FridayCheckBox.IsChecked == true);

            LoadRules();
            RefreshCalendarView();
            RefreshYearStats();
        }

        private void AddRule_Click(object sender, RoutedEventArgs e)
        {
            string name = RuleNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Podaj nazwę lub powód dnia wolnego (np. Ferie zimowe, Dzień Nauczyciela).", "Brak nazwy", MessageBoxButton.OK, MessageBoxImage.Warning);
                RuleNameTextBox.Focus();
                return;
            }

            var rule = new NonWorkingDayRule { Name = name };

            if (SingleDayRadio.IsChecked == true)
            {
                if (!SingleDatePicker.SelectedDate.HasValue)
                {
                    MessageBox.Show("Wybierz datę z kalendarza.", "Brak daty", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                rule.RuleType = NonWorkingDayType.SingleDay;
                rule.StartDate = SingleDatePicker.SelectedDate.Value.Date;
            }
            else if (DateRangeRadio.IsChecked == true)
            {
                if (!RangeStartDatePicker.SelectedDate.HasValue || !RangeEndDatePicker.SelectedDate.HasValue)
                {
                    MessageBox.Show("Wybierz obie daty (Od oraz Do) klikając na kalendarzu lub w polach dat.", "Brak zakresu dat", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (RangeStartDatePicker.SelectedDate.Value > RangeEndDatePicker.SelectedDate.Value)
                {
                    MessageBox.Show("Data początkowa (Od) nie może być późniejsza niż data końcowa (Do).", "Nieprawidłowy zakres", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var start = RangeStartDatePicker.SelectedDate.Value.Date;
                var end = RangeEndDatePicker.SelectedDate.Value.Date;

                if (RangeRecurringCheckBox.IsChecked == true)
                {
                    // Okres powtarzający się co roku (np. Boże Narodzenie 23.12 - 01.01)
                    rule.RuleType = NonWorkingDayType.RecurringDateRange;
                    rule.Month = start.Month;
                    rule.Day = start.Day;
                    rule.EndMonth = end.Month;
                    rule.EndDay = end.Day;
                    rule.StartDate = start;
                    rule.EndDate = end;
                }
                else
                {
                    // Jednorazowy zakres w konkretnym roku
                    rule.RuleType = NonWorkingDayType.DateRange;
                    rule.StartDate = start;
                    rule.EndDate = end;
                }
            }
            else if (RecurringRadio.IsChecked == true)
            {
                if (!int.TryParse(RecurringDayTextBox.Text.Trim(), out int day) || day < 1 || day > 31)
                {
                    MessageBox.Show("Wpisz poprawny dzień miesiąca (1 - 31).", "Błąd dnia", MessageBoxButton.OK, MessageBoxImage.Warning);
                    RecurringDayTextBox.Focus();
                    return;
                }
                int month = RecurringMonthComboBox.SelectedIndex + 1;
                rule.RuleType = NonWorkingDayType.RecurringYearly;
                rule.Day = day;
                rule.Month = month;
            }

            _dataService.AddCalendarRule(rule);
            RuleNameTextBox.Clear();

            // Zresetowanie zaznaczenia okresu
            _rangeStartDate = null;
            _rangeEndDate = null;
            _isUpdatingDatePickers = true;
            RangeStartDatePicker.SelectedDate = null;
            RangeEndDatePicker.SelectedDate = null;
            _isUpdatingDatePickers = false;

            LoadRules();
            RefreshCalendarView();
            RefreshYearStats();

            MessageBox.Show($"Pomyślnie dodano regułę '{name}' do kalendarza.", "Dodano", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void DeleteRule_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is NonWorkingDayRuleViewModel vm)
            {
                if (MessageBox.Show($"Czy na pewno chcesz usunąć regułę '{vm.Name}'?", "Potwierdzenie usunięcia", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    _dataService.DeleteCalendarRule(vm.Id);
                    LoadRules();
                    RefreshCalendarView();
                    RefreshYearStats();
                }
            }
        }

        private void SeedHolidays_Click(object sender, RoutedEventArgs e)
        {
            int added = _dataService.SeedStandardPolishHolidays();
            LoadRules();
            RefreshCalendarView();
            RefreshYearStats();

            if (added > 0)
            {
                MessageBox.Show($"Pomyślnie dodano {added} polskich świąt państwowych do kalendarza.", "Święta państwowe", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Wszystkie standardowe święta państwowe znajdują się już w Twoim kalendarzu.", "Informacja", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _dataService.Dispose();
            base.OnClosed(e);
        }
    }
}
