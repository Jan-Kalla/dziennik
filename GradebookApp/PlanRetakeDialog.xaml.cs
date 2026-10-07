using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;

namespace GradebookApp
{
    public partial class PlanRetakeDialog : Window
    {
        private AppDbContext _dbContext;
        private int? _classId;
        private int? _editRetakeId;
        private List<Student> _allClassStudents = new List<Student>();
        private bool _isInitializing = true;

        public ObservableCollection<Student> AvailableStudents { get; set; } = new ObservableCollection<Student>();
        public ObservableCollection<Student> SelectedStudents { get; set; } = new ObservableCollection<Student>();

        // Konstruktor obsługujący zarówno widok klasy (sztywne classId) jak i globalny (classId = null)
        public PlanRetakeDialog(int? classId = null, int? editRetakeId = null)
        {
            InitializeComponent();
            _dbContext = new AppDbContext();
            _classId = classId;
            _editRetakeId = editRetakeId;

            if (_editRetakeId.HasValue)
            {
                this.Title = "Edytuj zaplanowaną poprawę";
                ClassComboBox.IsEnabled = false; // Blokujemy zmianę klasy podczas edycji
            }
            else if (_classId.HasValue)
            {
                ClassComboBox.IsEnabled = false; // Blokujemy zmianę klasy, bo otworzono okno z widoku konkretnej klasy
            }

            LoadInitialData();
        }

        private void LoadInitialData()
        {
            var allClasses = _dbContext.Classes.OrderBy(c => c.Name).ToList();
            ClassComboBox.ItemsSource = allClasses;

            if (_editRetakeId.HasValue)
            {
                var existingRetake = _dbContext.PlannedRetakes
                    .Include(r => r.WrittenWork)
                    .Include(r => r.Attendees).ThenInclude(a => a.Student)
                    .FirstOrDefault(r => r.Id == _editRetakeId.Value);

                if (existingRetake != null)
                {
                    _classId = existingRetake.WrittenWork.SchoolClassId;
                    ClassComboBox.SelectedItem = allClasses.FirstOrDefault(c => c.Id == _classId);
                    
                    LoadClassData();

                    WorkComboBox.SelectedItem = (WorkComboBox.ItemsSource as List<WrittenWork>)?.FirstOrDefault(w => w.Id == existingRetake.WrittenWorkId);
                    DatePicker.SelectedDate = existingRetake.Date;
                    TimeTextBox.Text = existingRetake.Time;

                    foreach (var attendee in existingRetake.Attendees)
                    {
                        var student = _allClassStudents.FirstOrDefault(s => s.Id == attendee.StudentId);
                        if (student != null)
                        {
                            AvailableStudents.Remove(student);
                            SelectedStudents.Add(student);
                        }
                    }
                }
            }
            else if (_classId.HasValue)
            {
                ClassComboBox.SelectedItem = allClasses.FirstOrDefault(c => c.Id == _classId);
                LoadClassData();
            }

            AvailableStudentsList.ItemsSource = AvailableStudents;
            SelectedStudentsList.ItemsSource = SelectedStudents;
            
            _isInitializing = false;
        }

        private void ClassComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            
            if (ClassComboBox.SelectedItem is SchoolClass selectedClass)
            {
                _classId = selectedClass.Id;
                LoadClassData();
            }
        }

        private void LoadClassData()
        {
            if (!_classId.HasValue) return;

            var works = _dbContext.WrittenWorks.Where(w => w.SchoolClassId == _classId.Value && !w.IsIndividual).ToList();
            WorkComboBox.ItemsSource = works;
            
            if (works.Any() && !_editRetakeId.HasValue) 
                WorkComboBox.SelectedIndex = 0;

            _allClassStudents = _dbContext.Students.Where(s => s.SchoolClassId == _classId.Value).OrderBy(s => s.JournalNumber).ToList();

            AvailableStudents.Clear();
            SelectedStudents.Clear();

            foreach (var s in _allClassStudents)
            {
                AvailableStudents.Add(s);
            }

            SearchTextBox.Text = string.Empty;
            SearchTextBox_TextChanged(this, null!);
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SearchTextBox == null) return;
            string query = SearchTextBox.Text.Trim().ToLower();
            AvailableStudents.Clear();

            var filtered = _allClassStudents
                .Where(s => !SelectedStudents.Any(sel => sel.Id == s.Id))
                .Where(s => s.FullName.ToLower().Contains(query))
                .ToList();

            foreach(var s in filtered)
            {
                AvailableStudents.Add(s);
            }
        }

        private void AvailableStudent_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && item.DataContext is Student student)
            {
                AvailableStudents.Remove(student);
                SelectedStudents.Add(student);
                SearchTextBox.Text = string.Empty; 
            }
        }

        private void SelectedStudent_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && item.DataContext is Student student)
            {
                SelectedStudents.Remove(student);
                SearchTextBox_TextChanged(this, null!); 
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!_classId.HasValue)
            {
                MessageBox.Show("Wybierz klasę z listy.", "Ajajajaj! Błąd...", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (WorkComboBox.SelectedItem is not WrittenWork selectedWork)
            {
                MessageBox.Show("Wybierz pracę pisemną.", "Ajajajaj! Błąd...", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!DatePicker.SelectedDate.HasValue)
            {
                MessageBox.Show("Wybierz datę poprawy z kalendarza.", "Ajajajaj! Błąd...", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (SelectedStudents.Count == 0)
            {
                MessageBox.Show("Wybierz przynajmniej jednego ucznia.", "Ajajajaj! Błąd...", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string timeStr = TimeTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(timeStr))
            {
                timeStr = timeStr.Replace(" ", "");
                if (timeStr.Length == 3 && timeStr.All(char.IsDigit)) timeStr = "0" + timeStr;
                if (timeStr.Length == 4 && timeStr.All(char.IsDigit)) timeStr = timeStr.Insert(2, ":");
                if (timeStr.Length == 4 && timeStr.IndexOf(':') == 1) timeStr = "0" + timeStr;

                if (!Regex.IsMatch(timeStr, @"^([0-1][0-9]|2[0-3]):[0-5][0-9]$"))
                {
                    MessageBox.Show("Podaj poprawną godzinę (np. 14:30, 1515, 0930 lub 930).", 
                                    "Ajajajaj! Nieprawidłowa godzina...", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            PlannedRetake retake;

            if (_editRetakeId.HasValue)
            {
                retake = _dbContext.PlannedRetakes.First(r => r.Id == _editRetakeId.Value);
                var existingAttendees = _dbContext.PlannedRetakeStudents.Where(a => a.PlannedRetakeId == _editRetakeId.Value);
                _dbContext.PlannedRetakeStudents.RemoveRange(existingAttendees);
            }
            else
            {
                retake = new PlannedRetake();
                _dbContext.PlannedRetakes.Add(retake);
            }

            retake.WrittenWorkId = selectedWork.Id;
            retake.Date = DatePicker.SelectedDate.Value;
            retake.Time = timeStr;

            foreach (var student in SelectedStudents)
            {
                retake.Attendees.Add(new PlannedRetakeStudent
                {
                    StudentId = student.Id
                });
            }

            _dbContext.SaveChanges();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}