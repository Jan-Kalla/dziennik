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
        public ObservableCollection<WrittenWork> DisplayedWorks { get; set; } = new ObservableCollection<WrittenWork>();

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
            
            // Czyszczenie cache EF Core by mieć 100% pewność na świeże dane ze średniej przy Load
            _dbContext.ChangeTracker.Clear();
            
            _allStudents = _dbContext.Students
                .Where(s => s.SchoolClassId == schoolClass.Id)
                .Include(s => s.WrittenWorks)
                .ToList();

            _allWorks = _dbContext.WrittenWorks
                .Where(w => w.SchoolClassId == schoolClass.Id)
                .Include(w => w.Tasks)
                .ToList();

            SearchTextBox.Text = string.Empty;
            RefreshDisplayedStudents(_allStudents);
            RefreshDisplayedWorks();
        }

        private void RefreshDisplayedWorks()
        {
            DisplayedWorks.Clear();
            foreach (var work in _allWorks)
            {
                DisplayedWorks.Add(work);
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
                // Zamiast otwierać nowe okno, wysyłamy sygnał do okna głównego (rodzica), 
                // aby ukryło listę klasy i pokazało panel szczegółów ucznia.
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
                }
            }
        }

        private void WorkDetailsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is WrittenWork work)
            {
                var dialog = new WorkDetailsWindow(work.Id)
                {
                    Owner = Window.GetWindow(this)
                };

                if (dialog.ShowDialog() == true)
                {
                    _dbContext.Entry(work).State = EntityState.Detached;
                    if (work.Tasks != null)
                    {
                        foreach (var task in work.Tasks.ToList())
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

        // LPM - Otwarcie zbiorczej tabeli wyników dla danej pracy pisemnej
        private void WorkResultsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is WrittenWork work)
            {
                var resultsWindow = new WorkResultsWindow(work.Id, _currentClass.Id)
                {
                    Owner = Window.GetWindow(this)
                };

                resultsWindow.Show();
            }
        }

        private void DeleteWork_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is WrittenWork work)
            {
                var result = MessageBox.Show($"Czy na pewno chcesz usunąć pracę '{work.Title}' i wszystkie powiązane z nią dane punktowe?", 
                                             "Potwierdzenie usunięcia", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    _dbContext.WrittenWorks.Remove(work);
                    _dbContext.SaveChanges();
                    
                    _allWorks.Remove(work);
                    RefreshDisplayedWorks();
                }
            }
        }

        // NOWE: Metoda do odświeżania tabeli ręcznie
        private void RefreshTable_Click(object sender, RoutedEventArgs e)
        {
            if (_currentClass != null)
            {
                LoadClassData(_currentClass);
            }
        }
    }
}