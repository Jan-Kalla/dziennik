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

                DateTime? baseWritten = record?.CustomDateWritten ?? work.DateWritten;
                DateTime? baseEntered = record?.CustomDateEntered ?? work.DateEntered;
                DateTime? retakeWritten = record?.RetakeDateWritten;
                DateTime? retakeEntered = record?.RetakeDateEntered;

                bool hasBaseScores = studentScores.Any(s => s.PointsLevel1.HasValue || s.PointsLevel2.HasValue || s.PointsLevel3.HasValue);
                bool hasRetakeScores = studentScores.Any(s => s.RetakePointsLevel1.HasValue || s.RetakePointsLevel2.HasValue || s.RetakePointsLevel3.HasValue);

                double baseSum = 0;
                double retakeSum = 0;

                if (studentScores.Any())
                {
                    baseSum = GradeCalculator.CalculateWorkScore(work.MaxFinalPoints, work.Tasks, studentScores, false);
                    retakeSum = GradeCalculator.CalculateWorkScore(work.MaxFinalPoints, work.Tasks, studentScores, true);
                }

                // isRetakeDone to nasze zabezpieczenie przed przedłużaniem czasu w nieskończoność
                bool isRetakeDone = record != null && record.IsRetakeActive && hasRetakeScores;
                bool useRetakeDates = isRetakeDone && retakeSum > baseSum;

                string dateWritten = "";
                string dateEntered = "";
                string scoreText = "";

                if (useRetakeDates)
                {
                    dateWritten = retakeWritten?.ToString("dd.MM.yyyy") ?? baseWritten?.ToString("dd.MM.yyyy") ?? "Brak";
                    dateEntered = retakeEntered?.ToString("dd.MM.yyyy") ?? baseEntered?.ToString("dd.MM.yyyy") ?? "Brak";
                    scoreText = $"{retakeSum:0} / {work.MaxFinalPoints:0} pkt (popr.)";
                }
                else
                {
                    // Uczeń jest nieobecny (NB) - priorytet absolutny, bezwzględnie ignorujemy wszelkie zera wpisane w bazie
                    if (record != null && record.IsAbsent)
                    {
                        dateWritten = "-"; 
                        dateEntered = baseEntered?.ToString("dd.MM.yyyy") ?? "Brak"; 
                        scoreText = "NB";
                    }
                    else if (hasBaseScores)
                    {
                        dateWritten = baseWritten?.ToString("dd.MM.yyyy") ?? "Brak";
                        dateEntered = baseEntered?.ToString("dd.MM.yyyy") ?? "Brak";
                        scoreText = $"{baseSum:0} / {work.MaxFinalPoints:0} pkt";
                    }
                    else
                    {
                        dateWritten = baseWritten?.ToString("dd.MM.yyyy") ?? "Brak";
                        dateEntered = baseEntered?.ToString("dd.MM.yyyy") ?? "Brak";
                        scoreText = "Brak ocen";
                    }
                }

                string deadlineStr = "";

                // Jeśli poprawa została już wpisana, blokujemy termin zgodnie z życzeniem
                if (isRetakeDone)
                {
                    deadlineStr = "Już poprawiono";
                }
                else
                {
                    // Ufamy w 100% temu, co zapisano w bazie danych. 
                    // Nieważne czy data została cofnięta, czy przesunięta do przodu.
                    // Jeżeli z jakiegoś powodu w bazie nic nie ma, automat awaryjnie wstawia bazową + 14 dni.
                    DateTime? deadlineDate = record?.RetakeDeadline ?? baseEntered?.AddDays(14);
                    deadlineStr = deadlineDate?.ToString("dd.MM.yyyy") ?? "-";
                }

                resultsList.Add(new StudentWorkResultViewModel
                {
                    StudentId = student.Id,
                    JournalNumber = student.JournalNumber,
                    FullName = student.FullName,
                    DateWrittenDisplay = dateWritten,
                    DateEnteredDisplay = dateEntered,
                    DeadlineDisplay = deadlineStr,
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
        public string DeadlineDisplay { get; set; } = string.Empty;
        public string ScoreDisplay { get; set; } = string.Empty;
    }
}