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
        private int _classId;
        private int? _editRetakeId;
        private List<Student> _allClassStudents = new List<Student>();

        public ObservableCollection<Student> AvailableStudents { get; set; } = new ObservableCollection<Student>();
        public ObservableCollection<Student> SelectedStudents { get; set; } = new ObservableCollection<Student>();

        // Konstruktor przyjmujący opcjonalne ID do edycji
        public PlanRetakeDialog(int classId, int? editRetakeId = null)
        {
            InitializeComponent();
            _dbContext = new AppDbContext();
            _classId = classId;
            _editRetakeId = editRetakeId;

            if (_editRetakeId.HasValue)
            {
                this.Title = "Edytuj zaplanowaną poprawę";
            }

            LoadData();
        }

        private void LoadData()
        {
            var works = _dbContext.WrittenWorks.Where(w => w.SchoolClassId == _classId && !w.IsIndividual).ToList();
            WorkComboBox.ItemsSource = works;

            _allClassStudents = _dbContext.Students.Where(s => s.SchoolClassId == _classId).OrderBy(s => s.JournalNumber).ToList();

            // TRYB EDYCJI: Ładujemy dane z bazy
            if (_editRetakeId.HasValue)
            {
                var existingRetake = _dbContext.PlannedRetakes
                    .Include(r => r.Attendees).ThenInclude(a => a.Student)
                    .FirstOrDefault(r => r.Id == _editRetakeId.Value);

                if (existingRetake != null)
                {
                    WorkComboBox.SelectedItem = works.FirstOrDefault(w => w.Id == existingRetake.WrittenWorkId);
                    DatePicker.SelectedDate = existingRetake.Date;
                    TimeTextBox.Text = existingRetake.Time;

                    foreach (var attendee in existingRetake.Attendees)
                    {
                        SelectedStudents.Add(attendee.Student);
                    }
                }
            }
            else
            {
                if (works.Any()) WorkComboBox.SelectedIndex = 0;
            }
            
            // Wypełnianie listy dostępnych uczniów (z pominięciem tych już wybranych w trybie edycji)
            foreach (var s in _allClassStudents)
            {
                if (!SelectedStudents.Any(sel => sel.Id == s.Id))
                {
                    AvailableStudents.Add(s);
                }
            }

            AvailableStudentsList.ItemsSource = AvailableStudents;
            SelectedStudentsList.ItemsSource = SelectedStudents;
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
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

            // WALIDACJA GODZINY - Obsługa różnych formatów (15:15, 1515, 930, 0930, 9:30)
            string timeStr = TimeTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(timeStr))
            {
                // Usuwamy ewentualne spacje wplątane przez pomyłkę
                timeStr = timeStr.Replace(" ", "");

                // Zamiana 3 cyfr na 4 cyfry (np. 930 -> 0930)
                if (timeStr.Length == 3 && timeStr.All(char.IsDigit))
                {
                    timeStr = "0" + timeStr;
                }
                
                // Zamiana 4 cyfr na format z dwukropkiem (np. 0930 -> 09:30, 1515 -> 15:15)
                if (timeStr.Length == 4 && timeStr.All(char.IsDigit))
                {
                    timeStr = timeStr.Insert(2, ":");
                }
                
                // Zamiana formatu "H:mm" na "HH:mm" (np. 9:30 -> 09:30)
                if (timeStr.Length == 4 && timeStr.IndexOf(':') == 1)
                {
                    timeStr = "0" + timeStr;
                }

                // Ostateczna weryfikacja poprawności godziny i minuty
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
                
                // Usuwamy dotychczasowe powiązania uczniów z bazy, by wpisać zaktualizowaną listę
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