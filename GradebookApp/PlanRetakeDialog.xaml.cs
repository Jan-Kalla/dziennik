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
        private int? _preselectedWorkId;
        private List<int>? _preselectedStudentIds;
        private List<Student> _allClassStudents = new List<Student>();
        private bool _isInitializing = true;

        public ObservableCollection<Student> AvailableStudents { get; set; } = new ObservableCollection<Student>();
        public ObservableCollection<Student> SelectedStudents { get; set; } = new ObservableCollection<Student>();

        public string SelectedTime
        {
            get => TimeComboBox.SelectedItem as string ?? TimeComboBox.Text?.Trim() ?? "";
            set
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                string normalized = SmartTimeParser.ExtractStartTime(value);

                var items = TimeComboBox.ItemsSource as List<string>;
                if (items != null)
                {
                    var match = items.FirstOrDefault(t => t == value || t == normalized);
                    if (match != null)
                    {
                        TimeComboBox.SelectedItem = match;
                    }
                    else
                    {
                        items.Add(value);
                        items = items.OrderBy(t => TimeSpan.TryParse(t, out var ts) ? ts : TimeSpan.Zero).ToList();
                        TimeComboBox.ItemsSource = null;
                        TimeComboBox.ItemsSource = items;
                        TimeComboBox.SelectedItem = value;
                    }
                }
            }
        }

        // Konstruktor obsługujący wszystko (edycję, otwieranie z klasy oraz bezpośrednie zlecenia z oczekujących zgłoszeń)
        public PlanRetakeDialog(int? classId = null, int? editRetakeId = null, int? preselectedWorkId = null, List<int>? preselectedStudentIds = null)
        {
            InitializeComponent();
            _dbContext = new AppDbContext();
            _classId = classId;
            _editRetakeId = editRetakeId;
            _preselectedWorkId = preselectedWorkId;
            _preselectedStudentIds = preselectedStudentIds;

            if (_editRetakeId.HasValue || _classId.HasValue)
            {
                if (_editRetakeId.HasValue) this.Title = "Edytuj zaplanowaną poprawę";
                ClassComboBox.IsEnabled = false; // Blokujemy zmianę klasy
            }

            LoadInitialData();
        }

        private void LoadInitialData()
        {
            var allClasses = _dbContext.Classes.OrderBy(c => c.Name).ToList();
            ClassComboBox.ItemsSource = allClasses;

            LoadLessonTimes();

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
                    SelectedTime = existingRetake.Time;

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

        private void LoadLessonTimes()
        {
            var times = SmartTimeParser.GetLessonStartTimes(_dbContext);
            TimeComboBox.ItemsSource = times;
            if (times.Any())
            {
                TimeComboBox.SelectedIndex = 0;
            }
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
            {
                if (_preselectedWorkId.HasValue)
                    WorkComboBox.SelectedItem = works.FirstOrDefault(w => w.Id == _preselectedWorkId.Value);
                else
                    WorkComboBox.SelectedIndex = 0;
            }

            _allClassStudents = _dbContext.Students.Where(s => s.SchoolClassId == _classId.Value).OrderBy(s => s.JournalNumber).ToList();

            AvailableStudents.Clear();
            SelectedStudents.Clear();

            // Zrzucanie od razu zaznaczonych uczniów z pamięci okna (jeżeli dostaliśmy jakieś IDs od zewnętrznego okna zgłoszeń)
            if (_preselectedStudentIds != null && !_editRetakeId.HasValue)
            {
                var toSelect = _allClassStudents.Where(s => _preselectedStudentIds.Contains(s.Id)).ToList();
                foreach (var s in toSelect) SelectedStudents.Add(s);
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

            string timeStr = SelectedTime;
            if (string.IsNullOrWhiteSpace(timeStr))
            {
                MessageBox.Show("Wybierz godzinę rozpoczęcia poprawy z listy.", 
                                "Ajajajaj! Brak godziny...", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            timeStr = SmartTimeParser.ExtractStartTime(timeStr);

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