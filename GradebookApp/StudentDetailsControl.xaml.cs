using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;

namespace GradebookApp
{
    public partial class StudentDetailsControl : UserControl
    {
        private AppDbContext _dbContext;
        private Student _currentStudent = null!;

        public event EventHandler? StudentUpdated;

        public StudentDetailsControl()
        {
            InitializeComponent();
            _dbContext = new AppDbContext();
        }

        public void LoadStudentData(Student student)
        {
            _currentStudent = student;
            _dbContext.ChangeTracker.Clear();

            var dbStudent = _dbContext.Students.FirstOrDefault(s => s.Id == student.Id);
            if (dbStudent == null) return;

            StudentNameText.Text = dbStudent.FullName;

            var classWorks = _dbContext.WrittenWorks.Include(w => w.Tasks)
                                       .Where(w => w.SchoolClassId == dbStudent.SchoolClassId).ToList();
            var studentScores = _dbContext.StudentTaskScores.Where(s => s.StudentId == dbStudent.Id).ToList();
            var studentWorkRecords = _dbContext.StudentWorkRecords.Where(r => r.StudentId == dbStudent.Id).ToList();

            var worksList = new List<StudentWorkViewModel>();
            
            double totalEarnedP = 0;
            double totalPossibleM = 0;

            foreach (var work in classWorks)
            {
                var workScores = studentScores.Where(s => work.Tasks.Any(t => t.Id == s.WrittenWorkTaskId)).ToList();
                var workRecord = studentWorkRecords.FirstOrDefault(r => r.WrittenWorkId == work.Id);
                
                string scoreText = "Brak ocen";
                
                if (workRecord != null && workRecord.IsAbsent)
                {
                    scoreText = "NB"; 
                    // NB nie wpływa na dzielnik średniej
                }
                else if (workScores.Any())
                {
                    double baseSum = GradeCalculator.CalculateWorkScore(work.MaxFinalPoints, work.Tasks, workScores, false);
                    double finalScore = baseSum;

                    if (workRecord != null && workRecord.IsRetakeActive)
                    {
                        double retakeSum = GradeCalculator.CalculateWorkScore(work.MaxFinalPoints, work.Tasks, workScores, true);
                        if (retakeSum > baseSum)
                        {
                            finalScore = retakeSum;
                            scoreText = $"{finalScore} / {work.MaxFinalPoints} pkt (popr.)";
                        }
                        else
                        {
                            scoreText = $"{finalScore} / {work.MaxFinalPoints} pkt";
                        }
                    }
                    else
                    {
                        scoreText = $"{finalScore} / {work.MaxFinalPoints} pkt";
                    }
                    
                    totalEarnedP += finalScore;
                    totalPossibleM += work.MaxFinalPoints;
                }

                worksList.Add(new StudentWorkViewModel
                {
                    WorkId = work.Id,
                    WorkTitle = work.Title,
                    ScoreDisplay = scoreText
                });
            }

            // OBLICZANIE ŚREDNIEJ
            double average = 0;
            if (totalPossibleM > 0)
            {
                average = Math.Round((totalEarnedP / totalPossibleM) * 100, 2);
            }
            
            if (dbStudent.AveragePercentage != average)
            {
                dbStudent.AveragePercentage = average;
                _dbContext.SaveChanges();
                // Wysłanie sygnału o aktualizacji
                StudentUpdated?.Invoke(this, EventArgs.Empty);
            }

            StudentAverageText.Text = $"- Średnia: {average:0.##}%";
            StudentWorksDataGrid.ItemsSource = worksList;
        }

        private void GradeWork_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is StudentWorkViewModel vm)
            {
                var window = new StudentWorkDetailsWindow(_currentStudent.Id, vm.WorkId)
                {
                    Owner = Window.GetWindow(this)
                };

                window.DataSavedEvent += (s, ev) => 
                {
                    LoadStudentData(_currentStudent);
                };

                window.Show();
            }
        }
    }

    public class StudentWorkViewModel
    {
        public int WorkId { get; set; }
        public string WorkTitle { get; set; } = string.Empty;
        public string ScoreDisplay { get; set; } = string.Empty;
    }
}