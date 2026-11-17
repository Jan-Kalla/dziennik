using System;
using System.Collections.Generic;
using System.Linq;

namespace GradebookApp.Services
{
    public static class SchoolCalendarService
    {
        private static List<NonWorkingDayRule>? _cachedRules;
        private static readonly object _lock = new object();

        public static void InvalidateCache()
        {
            lock (_lock)
            {
                _cachedRules = null;
            }
        }

        public static List<NonWorkingDayRule> GetRules()
        {
            lock (_lock)
            {
                if (_cachedRules == null)
                {
                    try
                    {
                        using var db = new AppDbContext();
                        _cachedRules = db.NonWorkingDayRules.ToList();
                    }
                    catch
                    {
                        _cachedRules = new List<NonWorkingDayRule>();
                    }
                }
                return _cachedRules;
            }
        }

        /// <summary>
        /// Sprawdza, czy dany dzień jest dniem roboczym (nie jest weekendem ani dniem oznaczonym jako wolny).
        /// </summary>
        public static bool IsWorkingDay(DateTime date, List<NonWorkingDayRule>? rules = null)
        {
            // Weekendy są zawsze dniami nieroboczymi
            if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
            {
                return false;
            }

            var activeRules = rules ?? GetRules();
            var dateOnly = date.Date;

            foreach (var rule in activeRules)
            {
                switch (rule.RuleType)
                {
                    case NonWorkingDayType.WeeklyDay:
                        if (rule.DayOfWeek.HasValue && rule.DayOfWeek.Value == date.DayOfWeek)
                            return false;
                        break;

                    case NonWorkingDayType.SingleDay:
                        if (rule.StartDate.HasValue && rule.StartDate.Value.Date == dateOnly)
                            return false;
                        break;

                    case NonWorkingDayType.DateRange:
                        if (rule.StartDate.HasValue)
                        {
                            var start = rule.StartDate.Value.Date;
                            var end = (rule.EndDate ?? rule.StartDate).Value.Date;
                            if (dateOnly >= start && dateOnly <= end)
                                return false;
                        }
                        break;

                    case NonWorkingDayType.RecurringYearly:
                        if (rule.Month.HasValue && rule.Day.HasValue &&
                            rule.Month.Value == date.Month && rule.Day.Value == date.Day)
                            return false;
                        break;

                    case NonWorkingDayType.RecurringDateRange:
                        if (rule.Month.HasValue && rule.Day.HasValue && rule.EndMonth.HasValue && rule.EndDay.HasValue)
                        {
                            if (IsDateInRecurringRange(date, rule.Month.Value, rule.Day.Value, rule.EndMonth.Value, rule.EndDay.Value))
                                return false;
                        }
                        break;
                }
            }

            return true;
        }

        /// <summary>
        /// Zwraca opis/przyczynę dlaczego dany dzień jest nieroboczy, lub null jeśli jest roboczy.
        /// </summary>
        public static string? GetNonWorkingDayReason(DateTime date, List<NonWorkingDayRule>? rules = null)
        {
            if (date.DayOfWeek == DayOfWeek.Saturday) return "Sobota (weekend)";
            if (date.DayOfWeek == DayOfWeek.Sunday) return "Niedziela (weekend)";

            var activeRules = rules ?? GetRules();
            var dateOnly = date.Date;

            foreach (var rule in activeRules)
            {
                switch (rule.RuleType)
                {
                    case NonWorkingDayType.WeeklyDay:
                        if (rule.DayOfWeek.HasValue && rule.DayOfWeek.Value == date.DayOfWeek)
                            return string.IsNullOrWhiteSpace(rule.Name) ? $"Dzień wolny: {GetPolishDayName(rule.DayOfWeek.Value)}" : rule.Name;
                        break;

                    case NonWorkingDayType.SingleDay:
                        if (rule.StartDate.HasValue && rule.StartDate.Value.Date == dateOnly)
                            return string.IsNullOrWhiteSpace(rule.Name) ? "Dzień wolny" : rule.Name;
                        break;

                    case NonWorkingDayType.DateRange:
                        if (rule.StartDate.HasValue)
                        {
                            var start = rule.StartDate.Value.Date;
                            var end = (rule.EndDate ?? rule.StartDate).Value.Date;
                            if (dateOnly >= start && dateOnly <= end)
                                return string.IsNullOrWhiteSpace(rule.Name) ? "Okres wolny od zajęć" : rule.Name;
                        }
                        break;

                    case NonWorkingDayType.RecurringYearly:
                        if (rule.Month.HasValue && rule.Day.HasValue &&
                            rule.Month.Value == date.Month && rule.Day.Value == date.Day)
                            return string.IsNullOrWhiteSpace(rule.Name) ? "Święto coroczne" : rule.Name;
                        break;

                    case NonWorkingDayType.RecurringDateRange:
                        if (rule.Month.HasValue && rule.Day.HasValue && rule.EndMonth.HasValue && rule.EndDay.HasValue)
                        {
                            if (IsDateInRecurringRange(date, rule.Month.Value, rule.Day.Value, rule.EndMonth.Value, rule.EndDay.Value))
                                return string.IsNullOrWhiteSpace(rule.Name) ? "Okres coroczny wolny od zajęć" : rule.Name;
                        }
                        break;
                }
            }

            return null;
        }

        /// <summary>
        /// Sprawdza, czy data wpada w zakres powtarzający się co roku (np. 23.12 - 01.01).
        /// </summary>
        public static bool IsDateInRecurringRange(DateTime date, int startMonth, int startDay, int endMonth, int endDay)
        {
            int m = date.Month;
            int d = date.Day;

            if (startMonth == endMonth)
            {
                return m == startMonth && d >= startDay && d <= endDay;
            }
            else if (startMonth < endMonth)
            {
                if (m == startMonth) return d >= startDay;
                if (m > startMonth && m < endMonth) return true;
                if (m == endMonth) return d <= endDay;
                return false;
            }
            else // startMonth > endMonth (przejście przez przełom roku, np. od 23 grudnia do 1 stycznia)
            {
                if (m == startMonth) return d >= startDay;
                if (m > startMonth) return true;
                if (m < endMonth) return true;
                if (m == endMonth) return d <= endDay;
                return false;
            }
        }

        /// <summary>
        /// Dodaje (lub odejmuje) określoną liczbę DNI ROBOCZYCH do podanej daty startowej.
        /// Pomija weekendy oraz wszystkie zdefiniowane dni wolne/nierobocze.
        /// </summary>
        public static DateTime AddWorkingDays(DateTime startDate, int workingDays, List<NonWorkingDayRule>? rules = null)
        {
            if (workingDays == 0) return startDate;

            var activeRules = rules ?? GetRules();
            DateTime current = startDate.Date;
            int step = workingDays > 0 ? 1 : -1;
            int remaining = Math.Abs(workingDays);

            // Zabezpieczenie przed nieskończoną pętlą w skrajnym przypadku (np. gdyby ktoś zablokował wszystkie dni)
            int safetyLimit = 730; // maks 2 lata w przód/tył
            int iterations = 0;

            while (remaining > 0 && iterations++ < safetyLimit)
            {
                current = current.AddDays(step);
                if (IsWorkingDay(current, activeRules))
                {
                    remaining--;
                }
            }

            return current;
        }

        /// <summary>
        /// Zwraca rok początkowy roku szkolnego dla podanej daty (np. dla 15.11.2026 zwraca 2026, dla 15.02.2027 zwraca 2026).
        /// </summary>
        public static int GetSchoolYearStartYear(DateTime date)
        {
            return date.Month >= 9 ? date.Year : date.Year - 1;
        }

        public static string GetPolishDayName(DayOfWeek dow)
        {
            return dow switch
            {
                DayOfWeek.Monday => "Poniedziałek",
                DayOfWeek.Tuesday => "Wtorek",
                DayOfWeek.Wednesday => "Środa",
                DayOfWeek.Thursday => "Czwartek",
                DayOfWeek.Friday => "Piątek",
                DayOfWeek.Saturday => "Sobota",
                DayOfWeek.Sunday => "Niedziela",
                _ => dow.ToString()
            };
        }

        public static string GetPolishMonthName(int month)
        {
            return month switch
            {
                1 => "Styczeń",
                2 => "Luty",
                3 => "Marzec",
                4 => "Kwiecień",
                5 => "Maj",
                6 => "Czerwiec",
                7 => "Lipiec",
                8 => "Sierpień",
                9 => "Wrzesień",
                10 => "Październik",
                11 => "Listopad",
                12 => "Grudzień",
                _ => $"Miesiąc {month}"
            };
        }

        /// <summary>
        /// Zwraca listę standardowych polskich świąt państwowych/kościelnych stałych w kalendarzu.
        /// </summary>
        public static List<NonWorkingDayRule> GetStandardPolishHolidays()
        {
            return new List<NonWorkingDayRule>
            {
                new NonWorkingDayRule { RuleType = NonWorkingDayType.RecurringYearly, Month = 1, Day = 1, Name = "Nowy Rok" },
                new NonWorkingDayRule { RuleType = NonWorkingDayType.RecurringYearly, Month = 1, Day = 6, Name = "Święto Trzech Króli" },
                new NonWorkingDayRule { RuleType = NonWorkingDayType.RecurringYearly, Month = 5, Day = 1, Name = "Święto Pracy" },
                new NonWorkingDayRule { RuleType = NonWorkingDayType.RecurringYearly, Month = 5, Day = 3, Name = "Święto Konstytucji 3 Maja" },
                new NonWorkingDayRule { RuleType = NonWorkingDayType.RecurringYearly, Month = 8, Day = 15, Name = "Wniebowzięcie NMP" },
                new NonWorkingDayRule { RuleType = NonWorkingDayType.RecurringYearly, Month = 11, Day = 1, Name = "Wszystkich Świętych" },
                new NonWorkingDayRule { RuleType = NonWorkingDayType.RecurringYearly, Month = 11, Day = 11, Name = "Święto Niepodległości" },
                new NonWorkingDayRule { RuleType = NonWorkingDayType.RecurringYearly, Month = 12, Day = 25, Name = "Boże Narodzenie (I dzień)" },
                new NonWorkingDayRule { RuleType = NonWorkingDayType.RecurringYearly, Month = 12, Day = 26, Name = "Boże Narodzenie (II dzień)" }
            };
        }
    }
}
