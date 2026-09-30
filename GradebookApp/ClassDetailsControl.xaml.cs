using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
            
            // Czyszczenie cache EF Core by mieć 100% pewność na świeże dane
            _dbContext.ChangeTracker.Clear();
            
            _allStudents = _dbContext.Students
                .Where(s => s.SchoolClassId == schoolClass.Id)
                .Include(s => s.WrittenWorks)
                .ToList();

            _allWorks = _dbContext.WrittenWorks
                .Where(w => w.SchoolClassId == schoolClass.Id)
                .Include(w => w.Tasks)
                .ToList();

            // ====================================================================
            // AUTOMATYCZNE PRZELICZANIE ŚREDNICH DLA WSZYSTKICH UCZNIÓW KLASY
            // ====================================================================
            RecalculateAllStudentsAverages();

            SearchTextBox.Text = string.Empty;
            RefreshDisplayedStudents(_allStudents);
            RefreshDisplayedWorks();
        }

        // Pomocnicza metoda przeliczająca średnie w locie przy odświeżaniu widoku klasy
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

                    if (workRecord != null && workRecord.IsAbsent)
                    {
                        // NB nie wpływa na dzielnik średniej
                    }
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

            foreach (var work in _allWorks)
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

        private void AddStudent_Click(object sender, RoutedEventArgs e)
        {
            var existingNumbers = _allStudents.Select(s => s.JournalNumber).ToList();
            
            var dialog = new AddStudentDialog(existingNumbers) 
            { 
                Owner = Window.GetWindow(this) 
            };

            if (dialog.ShowDialog() == true && (!string.IsNullOrEmpty(dialog.FirstName) || !string.IsNullOrEmpty(dialog.LastName)))
            {
                int nextNumber = dialog.JournalNumber ?? (_allStudents.Any() ? _allStudents.Max(s => s.JournalNumber) + 1 : 1);

                var newStudent = new Student 
                { 
                    FirstName = dialog.FirstName, 
                    LastName = dialog.LastName, 
                    SchoolClassId = _currentClass.Id,
                    JournalNumber = nextNumber
                };

                _dbContext.Students.Add(newStudent);
                _dbContext.SaveChanges();

                _allStudents.Add(newStudent);
                RefreshDisplayedStudents(_allStudents);
                RefreshDisplayedWorks();
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
                    SchoolClassId = _currentClass.Id,
                    Tasks = dialog.Tasks 
                };

                _dbContext.WrittenWorks.Add(newWork);
                _dbContext.SaveChanges();

                _allWorks.Add(newWork);
                RefreshDisplayedWorks();

                MessageBox.Show($"{dialog.WorkType} '{newWork.Title}' (Zadań: {dialog.Tasks.Count}) została pomyślnie dodana do bazy.", 
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

        private void StudentsDataGrid_InitializingNewItem(object sender, InitializingNewItemEventArgs e)
        {
            if (e.NewItem is Student newStudent)
            {
                newStudent.JournalNumber = _allStudents.Any() ? _allStudents.Max(s => s.JournalNumber) + 1 : 1;
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
                        if (student.Id == 0) 
                        {
                            DisplayedStudents.Remove(student);
                        }
                        return;
                    }

                    if (student.JournalNumber > 0)
                    {
                        bool isDuplicate = _allStudents.Any(s => s != student && s.JournalNumber == student.JournalNumber);
                        if (isDuplicate)
                        {
                            MessageBox.Show($"Numerek {student.JournalNumber} jest już przypisany do innego ucznia. Zmiany zostały cofnięte.", 
                                            "Konflikt numerów", MessageBoxButton.OK, MessageBoxImage.Warning);
                            
                            if (student.Id == 0)
                            {
                                DisplayedStudents.Remove(student);
                            }
                            else
                            {
                                _dbContext.Entry(student).Reload(); 
                                StudentsDataGrid.Items.Refresh();
                            }
                            return; 
                        }
                    }

                    try
                    {
                        if (student.Id == 0)
                        {
                            if (student.JournalNumber <= 0)
                                student.JournalNumber = _allStudents.Any() ? _allStudents.Max(s => s.JournalNumber) + 1 : 1;

                            student.SchoolClassId = _currentClass.Id;
                            _dbContext.Students.Add(student);
                            
                            if (!_allStudents.Contains(student))
                                _allStudents.Add(student);
                        }
                        else
                        {
                            _dbContext.Students.Update(student);
                        }
                        
                        _dbContext.SaveChanges();
                        StudentsDataGrid.Items.Refresh(); 
                        RefreshDisplayedWorks();
                    }
                    catch (System.Exception ex)
                    {
                        MessageBox.Show($"Błąd zapisu bazy: {ex.Message}", "Ajajajaj! Błąd...", MessageBoxButton.OK, MessageBoxImage.Error);
                    }

                }, DispatcherPriority.Background);
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
                var detailsWindow = new StudentDetailsWindow(student);
                
                detailsWindow.StudentUpdated += (s, args) => 
                {
                    _dbContext.Entry(student).Reload();
                };
                
                detailsWindow.Show();
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
                        .Where(w => w.SchoolClassId == _currentClass.Id)
                        .Include(w => w.Tasks)
                        .ToList();
                        
                    RefreshDisplayedWorks();
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

                resultsWindow.Show();
            }
        }

        private void DeleteWork_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is ClassWorkViewModel vm)
            {
                var result = MessageBox.Show($"Czy na pewno chcesz usunąć pracę '{vm.Title}' i wszystkie powiązane z nią dane punktowe?", 
                                             "Potwierdzenie usunięcia", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    _dbContext.WrittenWorks.Remove(vm.OriginalWork);
                    _dbContext.SaveChanges();
                    
                    _allWorks.Remove(vm.OriginalWork);
                    RefreshDisplayedWorks();
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
}