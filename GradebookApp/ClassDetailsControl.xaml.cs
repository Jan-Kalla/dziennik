using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;

namespace GradebookApp
{
    public partial class ClassDetailsControl : UserControl
    {
        private AppDbContext _dbContext;
        private List<Student> _allStudents = new List<Student>();
        private List<WrittenWork> _allWorks = new List<WrittenWork>();
        private SchoolClass _currentClass = null!;

        public event System.EventHandler<Student>? StudentDetailsRequested;

        public ObservableCollection<Student> DisplayedStudents { get; set; } = new ObservableCollection<Student>();
        public ObservableCollection<ClassWorkViewModel> DisplayedWorks { get; set; } = new ObservableCollection<ClassWorkViewModel>();

        public ClassDetailsControl()
        {
            InitializeComponent();
            StudentsDataGrid.ItemsSource = DisplayedStudents;
            WorksDataGrid.ItemsSource = DisplayedWorks; 
            _dbContext = new AppDbContext();
        }

        public void LoadClassData(SchoolClass schoolClass)
        {
            _currentClass = schoolClass;
            ClassNameText.Text = $"Klasa: {schoolClass.Name}";
            
            _dbContext.ChangeTracker.Clear();
            
            _allStudents = _dbContext.Students
                .Where(s => s.SchoolClassId == schoolClass.Id)
                .Include(s => s.WrittenWorks)
                .ToList();

            _allWorks = _dbContext.WrittenWorks
                .Where(w => w.SchoolClassId == schoolClass.Id)
                .Include(w => w.Tasks)
                .ToList();

            RecalculateAllStudentsAverages();

            SearchTextBox.Text = string.Empty;
            RefreshDisplayedStudents(_allStudents);
            RefreshDisplayedWorks();
            
            RefreshCalendar();
        }

        private void RecalculateAllStudentsAverages()
        {
            var classWorks = _allWorks;
            
            foreach (var student in _allStudents)
            {
                var studentScores = _dbContext.StudentTaskScores.Where(s => s.StudentId == student.Id).ToList();
                var studentWorkRecords = _dbContext.StudentWorkRecords.Where(r => r.StudentId == student.Id).ToList();

                double totalEarnedP = 0;
                double totalPossibleM = 0;

                foreach (var work in classWorks)
                {
                    var workScores = studentScores.Where(s => work.Tasks.Any(t => t.Id == s.WrittenWorkTaskId)).ToList();
                    var workRecord = studentWorkRecords.FirstOrDefault(r => r.WrittenWorkId == work.Id);
                    
                    bool hasBaseScores = workScores.Any(s => s.PointsLevel1.HasValue || s.PointsLevel2.HasValue || s.PointsLevel3.HasValue);
                    bool hasRetakeScores = workScores.Any(s => s.RetakePointsLevel1.HasValue || s.RetakePointsLevel2.HasValue || s.RetakePointsLevel3.HasValue);

                    if (workRecord != null && workRecord.IsAbsent) { }
                    else 
                    {
                        double baseSum = 0;
                        double retakeSum = 0;
                        
                        if (hasBaseScores || hasRetakeScores)
                        {
                            baseSum = GradeCalculator.CalculateWorkScore(work.MaxFinalPoints, work.Tasks, workScores, false);
                            retakeSum = GradeCalculator.CalculateWorkScore(work.MaxFinalPoints, work.Tasks, workScores, true);
                        }

                        bool useRetake = workRecord != null && workRecord.IsRetakeActive && hasRetakeScores && retakeSum > baseSum;

                        if (useRetake)
                        {
                            totalEarnedP += retakeSum;
                            totalPossibleM += work.MaxFinalPoints;
                        }
                        else if (hasBaseScores)
                        {
                            totalEarnedP += baseSum;
                            totalPossibleM += work.MaxFinalPoints;
                        }
                    }
                }

                double average = 0;
                if (totalPossibleM > 0)
                {
                    average = Math.Round((totalEarnedP / totalPossibleM) * 100, 0, MidpointRounding.AwayFromZero);
                }
                
                if (student.AveragePercentage != average)
                {
                    student.AveragePercentage = average;
                    _dbContext.Students.Update(student);
                }
            }

            _dbContext.SaveChanges();
        }

        private void RefreshDisplayedWorks()
        {
            DisplayedWorks.Clear();
            int totalStudents = _allStudents.Count;

            var allClassRecords = _dbContext.StudentWorkRecords
                                            .Where(r => r.Student.SchoolClassId == _currentClass.Id)
                                            .ToList();

            var globalWorks = _allWorks.Where(w => !w.IsIndividual).ToList();

            foreach (var work in globalWorks)
            {
                var taskIds = work.Tasks.Select(t => t.Id).ToList();
                
                var absentStudentIds = allClassRecords
                                                 .Where(r => r.WrittenWorkId == work.Id && r.IsAbsent)
                                                 .Select(r => r.StudentId)
                                                 .ToList();

                int gradedCount = _dbContext.StudentTaskScores
                                            .Where(s => taskIds.Contains(s.WrittenWorkTaskId) 
                                                     && !absentStudentIds.Contains(s.StudentId)
                                                     && (s.PointsLevel1 != null || s.PointsLevel2 != null || s.PointsLevel3 != null || 
                                                         s.RetakePointsLevel1 != null || s.RetakePointsLevel2 != null || s.RetakePointsLevel3 != null))
                                            .Select(s => s.StudentId)
                                            .Distinct()
                                            .Count();

                DisplayedWorks.Add(new ClassWorkViewModel
                {
                    WorkId = work.Id,
                    WorkType = work.WorkType,
                    Title = work.Title,
                    MaxFinalPoints = work.MaxFinalPoints,
                    DateWrittenDisplay = work.DateWritten?.ToString("dd.MM.yyyy") ?? "Brak",
                    DateEnteredDisplay = work.DateEntered?.ToString("dd.MM.yyyy") ?? "Brak",
                    AttendanceRatio = $"{gradedCount} / {totalStudents}",
                    OriginalWork = work
                });
            }
        }

        private void RefreshCalendar()
        {
            if (_currentClass == null) return;
            
            var events = new List<CalendarEventViewModel>();
            var today = DateTime.Today;

            var retakes = _dbContext.PlannedRetakes
                .Include(r => r.WrittenWork)
                .Include(r => r.Attendees).ThenInclude(a => a.Student)
                .Where(r => r.WrittenWork.SchoolClassId == _currentClass.Id && r.Date >= today)
                .ToList();

            foreach (var r in retakes)
            {
                events.Add(new CalendarEventViewModel
                {
                    EventId = r.Id,
                    IsRetake = true,
                    SortDate = r.Date,
                    DateDisplay = r.Date.ToString("dd.MM.yyyy"),
                    TimeDisplay = string.IsNullOrEmpty(r.Time) ? "-" : r.Time,
                    EventType = "Poprawa",
                    WorkType = r.WrittenWork.WorkType, 
                    Title = r.WrittenWork.Title,
                    StudentsDisplay = string.Join(", ", r.Attendees.Select(a => a.Student.FullName))
                });
            }

            if (ShowAllWorksCheckBox.IsChecked == true)
            {
                var upcomingWorks = _allWorks.Where(w => w.DateWritten.HasValue && w.DateWritten.Value >= today && !w.IsIndividual).ToList();
                foreach (var w in upcomingWorks)
                {
                    events.Add(new CalendarEventViewModel
                    {
                        EventId = w.Id,
                        IsRetake = false,
                        SortDate = w.DateWritten!.Value,
                        DateDisplay = w.DateWritten.Value.ToString("dd.MM.yyyy"),
                        TimeDisplay = "-", 
                        EventType = "Pierwszy termin",
                        WorkType = w.WorkType, 
                        Title = w.Title,
                        StudentsDisplay = "Cała klasa"
                    });
                }
            }

            CalendarDataGrid.ItemsSource = events.OrderBy(e => e.SortDate).ToList();
        }

        private void RefreshCalendar_Click(object sender, RoutedEventArgs e)
        {
            RefreshCalendar();
        }

        private void PlanRetake_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new PlanRetakeDialog(_currentClass.Id)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
            {
                RefreshCalendar();
            }
        }

        private void EditRetake_Click(object sender, RoutedEventArgs e)
        {
            if (CalendarDataGrid.SelectedItem is CalendarEventViewModel vm)
            {
                if (!vm.IsRetake)
                {
                    MessageBox.Show("Możesz edytować tylko zaplanowane poprawy. Główny termin modyfikuje się w szczegółach pracy pisemnej.", 
                                    "Zablokowane", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dialog = new PlanRetakeDialog(_currentClass.Id, vm.EventId)
                {
                    Owner = Window.GetWindow(this)
                };

                if (dialog.ShowDialog() == true)
                {
                    RefreshCalendar();
                }
            }
        }

        private void DeleteRetake_Click(object sender, RoutedEventArgs e)
        {
            if (CalendarDataGrid.SelectedItem is CalendarEventViewModel vm)
            {
                if (!vm.IsRetake)
                {
                    MessageBox.Show("Z tego poziomu można usunąć tylko zaplanowane poprawy.", 
                                    "Zablokowane", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var result = MessageBox.Show($"Czy na pewno chcesz anulować i usunąć zaplanowaną poprawę dla pracy '{vm.Title}'?", 
                                             "Potwierdzenie usunięcia", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    var retake = _dbContext.PlannedRetakes.Find(vm.EventId);
                    if (retake != null)
                    {
                        _dbContext.PlannedRetakes.Remove(retake);
                        _dbContext.SaveChanges();
                        RefreshCalendar();
                    }
                }
            }
        }

        private void SwapColumns_Click(object sender, RoutedEventArgs e)
        {
            if (FirstNameCol != null && LastNameCol != null)
            {
                int tempIndex = FirstNameCol.DisplayIndex;
                FirstNameCol.DisplayIndex = LastNameCol.DisplayIndex;
                LastNameCol.DisplayIndex = tempIndex;
            }
        }

        private void QuickAdd_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true; 
                QuickAddStudent_Click(null!, null!); 
            }
        }

        private void QuickAddStudent_Click(object sender, RoutedEventArgs e)
        {
            string fName = QuickAddFirstNameTextBox.Text.Trim();
            string lName = QuickAddLastNameTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(fName) && string.IsNullOrWhiteSpace(lName))
            {
                return; 
            }

            try
            {
                int nextNumber = _allStudents.Any() ? _allStudents.Max(s => s.JournalNumber) + 1 : 1;

                var newStudent = new Student 
                { 
                    FirstName = fName, 
                    LastName = lName, 
                    SchoolClassId = _currentClass.Id,
                    JournalNumber = nextNumber
                };

                _dbContext.Students.Add(newStudent);
                _dbContext.SaveChanges();

                _allStudents.Add(newStudent);
                DisplayedStudents.Add(newStudent);
                
                RefreshDisplayedWorks();

                StudentsDataGrid.UpdateLayout();
                StudentsDataGrid.ScrollIntoView(newStudent);

                QuickAddFirstNameTextBox.Text = string.Empty;
                QuickAddLastNameTextBox.Text = string.Empty;
                
                if (FirstNameCol.DisplayIndex < LastNameCol.DisplayIndex)
                    QuickAddFirstNameTextBox.Focus();
                else
                    QuickAddLastNameTextBox.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Błąd zapisu bazy: {ex.Message}", "Ajajajaj! Błąd...", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StudentsDataGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                var student = e.Row.Item as Student;
                if (student == null) return;

                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (string.IsNullOrWhiteSpace(student.FirstName) && string.IsNullOrWhiteSpace(student.LastName))
                    {
                        MessageBox.Show("Uczeń nie może mieć pustego imienia i nazwiska.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning);
                        _dbContext.Entry(student).Reload(); 
                        StudentsDataGrid.Items.Refresh();
                        return;
                    }

                    if (student.JournalNumber > 0)
                    {
                        bool isDuplicate = _allStudents.Any(s => s != student && s.JournalNumber == student.JournalNumber);
                        if (isDuplicate)
                        {
                            MessageBox.Show($"Numerek {student.JournalNumber} jest już przypisany do innego ucznia. Zmiany zostały cofnięte.", 
                                            "Konflikt numerów", MessageBoxButton.OK, MessageBoxImage.Warning);
                            _dbContext.Entry(student).Reload(); 
                            StudentsDataGrid.Items.Refresh();
                            return; 
                        }
                    }

                    try
                    {
                        _dbContext.Students.Update(student);
                        _dbContext.SaveChanges();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Błąd aktualizacji w bazie: {ex.Message}", "Ajajajaj! Błąd...", MessageBoxButton.OK, MessageBoxImage.Error);
                    }

                }, DispatcherPriority.Background);
            }
        }

        private void AddWork_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AddWorkDialog 
            { 
                Owner = Window.GetWindow(this) 
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.WorkTitle))
            {
                var newWork = new WrittenWork 
                { 
                    Title = dialog.WorkTitle, 
                    WorkType = dialog.WorkType,
                    MaxFinalPoints = dialog.MaxFinalPoints, 
                    DateWritten = dialog.DateWritten,
                    DateEntered = dialog.DateEntered,
                    HasGroups = dialog.HasGroups, 
                    SchoolClassId = _currentClass.Id,
                    Tasks = dialog.Tasks 
                };

                _dbContext.WrittenWorks.Add(newWork);
                _dbContext.SaveChanges();

                _allWorks.Add(newWork);
                RefreshDisplayedWorks();
                RefreshCalendar();

                MessageBox.Show($"{dialog.WorkType} '{newWork.Title}' została pomyślnie dodana do bazy.", 
                                "Sukces", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = SearchTextBox.Text.Trim().ToLower();

            if (string.IsNullOrEmpty(query))
            {
                RefreshDisplayedStudents(_allStudents);
            }
            else
            {
                var filteredList = _allStudents.Where(s => 
                    s.FullName.ToLower().Contains(query) || 
                    s.WrittenWorks.Any(w => w.Title.ToLower().Contains(query))
                ).ToList();

                RefreshDisplayedStudents(filteredList);
            }
        }

        private void RefreshDisplayedStudents(List<Student> studentsToShow)
        {
            DisplayedStudents.Clear();
            foreach (var student in studentsToShow)
            {
                DisplayedStudents.Add(student);
            }
        }

        private void StudentDetailsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is Student student && student.Id != 0)
            {
                StudentDetailsRequested?.Invoke(this, student);
            }
        }

        private void OpenStudentNewWindow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is Student student && student.Id != 0)
            {
                var detailsWindow = new StudentDetailsWindow(student)
                {
                    Owner = Window.GetWindow(this)
                };
                
                detailsWindow.StudentUpdated += (s, args) => 
                {
                    _dbContext.Entry(student).Reload();
                };
                
                detailsWindow.ShowDialog();
            }
        }

        private void DeleteStudent_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is Student student && student.Id != 0)
            {
                var result = MessageBox.Show($"Czy na pewno chcesz usunąć ucznia {student.FullName} z bazy?", 
                                             "Potwierdzenie usunięcia", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    _dbContext.Students.Remove(student);
                    _dbContext.SaveChanges();
                    _allStudents.Remove(student);
                    RefreshDisplayedStudents(_allStudents);
                    RefreshDisplayedWorks();
                }
            }
        }

        private void WorkDetailsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is ClassWorkViewModel vm)
            {
                var dialog = new WorkDetailsWindow(vm.WorkId)
                {
                    Owner = Window.GetWindow(this)
                };

                if (dialog.ShowDialog() == true)
                {
                    _dbContext.Entry(vm.OriginalWork).State = EntityState.Detached;
                    if (vm.OriginalWork.Tasks != null)
                    {
                        foreach (var task in vm.OriginalWork.Tasks.ToList())
                        {
                            _dbContext.Entry(task).State = EntityState.Detached;
                        }
                    }

                    _allWorks = _dbContext.WrittenWorks
                        .Where(w => w.SchoolClassId == _currentClass.Id && !w.IsIndividual)
                        .Include(w => w.Tasks)
                        .ToList();
                        
                    RefreshDisplayedWorks();
                    RefreshCalendar();
                }
            }
        }

        private void WorkResultsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is ClassWorkViewModel vm)
            {
                var resultsWindow = new WorkResultsWindow(vm.WorkId, _currentClass.Id)
                {
                    Owner = Window.GetWindow(this)
                };

                resultsWindow.ShowDialog();
            }
        }

        private void DeleteWork_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is ClassWorkViewModel vm)
            {
                var result = MessageBox.Show($"Czy na pewno chcesz usunąć ocenę '{vm.Title}' i wszystkie powiązane z nią dane punktowe?", 
                                             "Potwierdzenie usunięcia", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    _dbContext.WrittenWorks.Remove(vm.OriginalWork);
                    _dbContext.SaveChanges();
                    
                    _allWorks.Remove(vm.OriginalWork);
                    RefreshDisplayedWorks();
                    RefreshCalendar();
                }
            }
        }

        private void RefreshTable_Click(object sender, RoutedEventArgs e)
        {
            if (_currentClass != null)
            {
                LoadClassData(_currentClass);
            }
        }
    }

    public class ClassWorkViewModel
    {
        public int WorkId { get; set; }
        public string WorkType { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public double MaxFinalPoints { get; set; }
        public string DateWrittenDisplay { get; set; } = string.Empty;
        public string DateEnteredDisplay { get; set; } = string.Empty;
        public string AttendanceRatio { get; set; } = string.Empty; 
        public WrittenWork OriginalWork { get; set; } = null!;
    }

    public class CalendarEventViewModel
    {
        public int EventId { get; set; }
        public bool IsRetake { get; set; }
        public DateTime SortDate { get; set; }
        public string DateDisplay { get; set; } = string.Empty;
        public string TimeDisplay { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public string WorkType { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string StudentsDisplay { get; set; } = string.Empty;
    }
}