using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using GradebookApp.Services;

namespace GradebookApp
{
    public class ClassHoursSummaryItem
    {
        public string ClassName { get; set; } = string.Empty;
        public int WeeklyHours { get; set; }
        public string MondayDetails { get; set; } = "-";
        public string TuesdayDetails { get; set; } = "-";
        public string WednesdayDetails { get; set; } = "-";
        public string ThursdayDetails { get; set; } = "-";
        public string FridayDetails { get; set; } = "-";
        public int TotalYearHours { get; set; }
        public string TotalYearHoursDisplay => $"{TotalYearHours} godz.";
    }

    public partial class ClassHoursSummaryWindow : Window
    {
        private readonly int _schoolYearStart;
        public ObservableCollection<ClassHoursSummaryItem> SummaryItems { get; set; } = new ObservableCollection<ClassHoursSummaryItem>();

        public ClassHoursSummaryWindow(int schoolYearStart)
        {
            InitializeComponent();
            _schoolYearStart = schoolYearStart;
            HeaderTitleText.Text = $"ZESTAWIENIE GODZIN LEKCYJNYCH KLAS — ROK SZKOLNY {schoolYearStart} / {schoolYearStart + 1}";
            HoursDataGrid.ItemsSource = SummaryItems;

            CalculateHours(_schoolYearStart);
        }

        private void RemainingOnlyCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (GeneralStatsText == null) return;
            CalculateHours(_schoolYearStart);
        }

        private void CalculateHours(int schoolYearStart)
        {
            SummaryItems.Clear();

            bool isRemaining = RemainingOnlyCheckBox?.IsChecked == true;
            DateTime today = DateTime.Today;

            // 1. Dni robocze w roku szkolnym (od 1 września do 30 czerwca)
            var yearStart = new DateTime(schoolYearStart, 9, 1);
            var yearEnd = new DateTime(schoolYearStart + 1, 6, 30);
            var activeRules = SchoolCalendarService.GetRules();

            int workMon = 0, workTue = 0, workWed = 0, workThu = 0, workFri = 0;
            int workingDays = 0, totalFreeDays = 0, weekendDays = 0;

            for (var d = yearStart; d <= yearEnd; d = d.AddDays(1))
            {
                // Jeśli zaznaczono "tylko pozostałe", ignorujemy dni z przeszłości
                if (isRemaining && d.Date < today)
                {
                    continue;
                }

                if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday)
                {
                    weekendDays++;
                    totalFreeDays++;
                }
                else if (!SchoolCalendarService.IsWorkingDay(d, activeRules))
                {
                    totalFreeDays++;
                }
                else
                {
                    workingDays++;
                    switch (d.DayOfWeek)
                    {
                        case DayOfWeek.Monday: workMon++; break;
                        case DayOfWeek.Tuesday: workTue++; break;
                        case DayOfWeek.Wednesday: workWed++; break;
                        case DayOfWeek.Thursday: workThu++; break;
                        case DayOfWeek.Friday: workFri++; break;
                    }
                }
            }

            if (isRemaining)
            {
                DateTime fromDate = today < yearStart ? yearStart : (today > yearEnd ? yearEnd : today);
                GeneralStatsText.Text = $"Dni nauki pozostałe do końca roku (od {fromDate:dd.MM.yyyy} do 30.06): Robocze: {workingDays} | Wolne: {totalFreeDays} (w tym weekendy: {weekendDays})";
                WorkingDaysSummaryText.Text = $"Dni nauki wg dni tygodnia (pozostałe): Pn: {workMon} | Wt: {workTue} | Śr: {workWed} | Czw: {workThu} | Pt: {workFri}";
            }
            else
            {
                GeneralStatsText.Text = $"Dni nauki (Wrz - Cze): Robocze: {workingDays} | Wolne: {totalFreeDays} (w tym weekendy: {weekendDays})";
                WorkingDaysSummaryText.Text = $"Dni nauki wg dni tygodnia: Pn: {workMon} | Wt: {workTue} | Śr: {workWed} | Czw: {workThu} | Pt: {workFri}";
            }

            string suffix = isRemaining ? "do końca" : "w roku";
            if (ColMon != null) ColMon.Header = $"Pn (tydz. / {suffix})";
            if (ColTue != null) ColTue.Header = $"Wt (tydz. / {suffix})";
            if (ColWed != null) ColWed.Header = $"Śr (tydz. / {suffix})";
            if (ColThu != null) ColThu.Header = $"Czw (tydz. / {suffix})";
            if (ColFri != null) ColFri.Header = $"Pt (tydz. / {suffix})";
            if (ColTotal != null) ColTotal.Header = isRemaining ? "Pozostało w roku" : "Łącznie w roku";

            // 2. Pobieramy szablony planu lekcji
            using var dataService = new GradebookDataService();
            var allDbClasses = dataService.GetAllClasses().Select(c => c.Name).ToList();
            var templates = dataService.GetLessonTemplates()
                .Where(t => !string.IsNullOrWhiteSpace(t.ClassName) && !string.IsNullOrWhiteSpace(t.TimeSlot))
                .Where(t => !t.ClassName.Contains("Popraw", StringComparison.OrdinalIgnoreCase) &&
                            !t.ClassName.Contains("Konsultac", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var templateClasses = templates.Select(t => t.ClassName.Trim()).Distinct().ToList();

            var combinedClasses = allDbClasses.Concat(templateClasses)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c)
                .ToList();

            int grandTotalHours = 0;

            foreach (var className in combinedClasses)
            {
                int monCount = templates.Count(t => t.DayOfWeek == DayOfWeek.Monday && string.Equals(t.ClassName.Trim(), className, StringComparison.OrdinalIgnoreCase));
                int tueCount = templates.Count(t => t.DayOfWeek == DayOfWeek.Tuesday && string.Equals(t.ClassName.Trim(), className, StringComparison.OrdinalIgnoreCase));
                int wedCount = templates.Count(t => t.DayOfWeek == DayOfWeek.Wednesday && string.Equals(t.ClassName.Trim(), className, StringComparison.OrdinalIgnoreCase));
                int thuCount = templates.Count(t => t.DayOfWeek == DayOfWeek.Thursday && string.Equals(t.ClassName.Trim(), className, StringComparison.OrdinalIgnoreCase));
                int friCount = templates.Count(t => t.DayOfWeek == DayOfWeek.Friday && string.Equals(t.ClassName.Trim(), className, StringComparison.OrdinalIgnoreCase));

                int weeklyTotal = monCount + tueCount + wedCount + thuCount + friCount;
                int yearTotal = (monCount * workMon) + (tueCount * workTue) + (wedCount * workWed) + (thuCount * workThu) + (friCount * workFri);

                grandTotalHours += yearTotal;

                SummaryItems.Add(new ClassHoursSummaryItem
                {
                    ClassName = className,
                    WeeklyHours = weeklyTotal,
                    MondayDetails = monCount > 0 ? $"{monCount} godz. ({monCount * workMon} {suffix})" : "—",
                    TuesdayDetails = tueCount > 0 ? $"{tueCount} godz. ({tueCount * workTue} {suffix})" : "—",
                    WednesdayDetails = wedCount > 0 ? $"{wedCount} godz. ({wedCount * workWed} {suffix})" : "—",
                    ThursdayDetails = thuCount > 0 ? $"{thuCount} godz. ({thuCount * workThu} {suffix})" : "—",
                    FridayDetails = friCount > 0 ? $"{friCount} godz. ({friCount * workFri} {suffix})" : "—",
                    TotalYearHours = yearTotal
                });
            }

            GrandTotalText.Text = isRemaining
                ? $"Łączna liczba godzin pozostałych do końca roku dla wszystkich klas: {grandTotalHours} godz."
                : $"Łączna liczba godzin lekcyjnych wszystkich klas w roku: {grandTotalHours} godz.";
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
