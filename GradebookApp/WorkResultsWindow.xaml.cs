using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;

namespace GradebookApp
{
    public partial class WorkResultsWindow : Window
    {
        private AppDbContext _dbContext;
        private int _workId;
        private int _classId;

        public WorkResultsWindow(int workId, int classId)
        {
            InitializeComponent();
            _dbContext = new AppDbContext();
            _workId = workId;
            _classId = classId;

            LoadData();
        }

        private void LoadData()
        {
            _dbContext.ChangeTracker.Clear(); 

            var work = _dbContext.WrittenWorks.Include(w => w.Tasks).FirstOrDefault(w => w.Id == _workId);
            var students = _dbContext.Students.Where(s => s.SchoolClassId == _classId).OrderBy(s => s.JournalNumber).ToList();
            
            if (work == null || students.Count == 0) return;

            WorkTitleText.Text = $"Wyniki: {work.WorkType.ToUpper()} - {work.Title}";

            var workRecords = _dbContext.StudentWorkRecords.Where(r => r.WrittenWorkId == _workId).ToList();
            var taskIds = work.Tasks.Select(t => t.Id).ToList();
            var allScores = _dbContext.StudentTaskScores.Where(s => taskIds.Contains(s.WrittenWorkTaskId)).ToList();

            var resultsList = new List<StudentWorkResultViewModel>();

            foreach (var student in students)
            {
                var record = workRecords.FirstOrDefault(r => r.StudentId == student.Id);
                var studentScores = allScores.Where(s => s.StudentId == student.Id).ToList();

                string dateWritten = "Brak";
                string dateEntered = "Brak";
                string retakeDateWritten = "-";
                string retakeDateEntered = "-";
                string scoreText = "Brak ocen";

                if (record != null && record.IsAbsent)
                {
                    dateWritten = "NB";
                    dateEntered = "NB";
                    scoreText = "NB";
                }
                else
                {
                    DateTime? written = record?.CustomDateWritten ?? work.DateWritten;
                    DateTime? entered = record?.CustomDateEntered ?? work.DateEntered;

                    dateWritten = written?.ToString("dd.MM.yyyy") ?? "Brak";
                    dateEntered = entered?.ToString("dd.MM.yyyy") ?? "Brak";

                    // Wyciąganie dat z poprawy
                    if (record != null && record.IsRetakeActive)
                    {
                        retakeDateWritten = record.RetakeDateWritten?.ToString("dd.MM.yyyy") ?? "-";
                        retakeDateEntered = record.RetakeDateEntered?.ToString("dd.MM.yyyy") ?? "-";
                    }

                    if (studentScores.Any())
                    {
                        double baseSum = GradeCalculator.CalculateWorkScore(work.MaxFinalPoints, work.Tasks, studentScores, false);
                        
                        if (record != null && record.IsRetakeActive)
                        {
                            double retakeSum = GradeCalculator.CalculateWorkScore(work.MaxFinalPoints, work.Tasks, studentScores, true);
                            if (retakeSum > baseSum)
                            {
                                scoreText = $"{retakeSum} / {work.MaxFinalPoints} pkt (popr.)";
                            }
                            else
                            {
                                scoreText = $"{baseSum} / {work.MaxFinalPoints} pkt";
                            }
                        }
                        else
                        {
                            scoreText = $"{baseSum} / {work.MaxFinalPoints} pkt";
                        }
                    }
                }

                resultsList.Add(new StudentWorkResultViewModel
                {
                    StudentId = student.Id,
                    JournalNumber = student.JournalNumber,
                    FullName = student.FullName,
                    DateWrittenDisplay = dateWritten,
                    DateEnteredDisplay = dateEntered,
                    RetakeDateWrittenDisplay = retakeDateWritten,
                    RetakeDateEnteredDisplay = retakeDateEntered,
                    ScoreDisplay = scoreText
                });
            }

            ResultsDataGrid.ItemsSource = resultsList;
        }

        private void GradeStudent_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is StudentWorkResultViewModel vm)
            {
                var window = new StudentWorkDetailsWindow(vm.StudentId, _workId)
                {
                    Owner = Window.GetWindow(this)
                };

                window.DataSavedEvent += (s, ev) => 
                {
                    LoadData();
                };

                window.Show();
            }
        }
    }

    public class StudentWorkResultViewModel
    {
        public int StudentId { get; set; }
        public int JournalNumber { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string DateWrittenDisplay { get; set; } = string.Empty;
        public string DateEnteredDisplay { get; set; } = string.Empty;
        
        // NOWE WŁAŚCIWOŚCI WIDOKU
        public string RetakeDateWrittenDisplay { get; set; } = string.Empty;
        public string RetakeDateEnteredDisplay { get; set; } = string.Empty;
        
        public string ScoreDisplay { get; set; } = string.Empty;
    }
}