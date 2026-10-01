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

            // Ładujemy oceny globalne klasy ORAZ oceny indywidualne przypisane WYŁĄCZNIE do tego ucznia
            var classWorks = _dbContext.WrittenWorks.Include(w => w.Tasks)
                                       .Where(w => w.SchoolClassId == dbStudent.SchoolClassId && 
                                                  (!w.IsIndividual || w.IndividualStudentId == dbStudent.Id))
                                       .ToList();
            
            var studentScores = _dbContext.StudentTaskScores.Where(s => s.StudentId == dbStudent.Id).ToList();
            var studentWorkRecords = _dbContext.StudentWorkRecords.Where(r => r.StudentId == dbStudent.Id).ToList();

            var worksList = new List<StudentWorkViewModel>();
            
            double totalEarnedP = 0;
            double totalPossibleM = 0;

            foreach (var work in classWorks)
            {
                var workScores = studentScores.Where(s => work.Tasks.Any(t => t.Id == s.WrittenWorkTaskId)).ToList();
                var workRecord = studentWorkRecords.FirstOrDefault(r => r.WrittenWorkId == work.Id);
                
                bool hasBaseScores = workScores.Any(s => s.PointsLevel1.HasValue || s.PointsLevel2.HasValue || s.PointsLevel3.HasValue);
                bool hasRetakeScores = workScores.Any(s => s.RetakePointsLevel1.HasValue || s.RetakePointsLevel2.HasValue || s.RetakePointsLevel3.HasValue);

                string scoreText = "Brak ocen";
                string groupText = "-"; 
                bool useRetake = false;
                
                if (workRecord != null && workRecord.IsAbsent)
                {
                    scoreText = "NB"; 
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

                    useRetake = workRecord != null && workRecord.IsRetakeActive && hasRetakeScores && retakeSum > baseSum;

                    if (useRetake)
                    {
                        scoreText = $"{retakeSum:0} / {work.MaxFinalPoints:0} pkt (popr.)";
                        totalEarnedP += retakeSum;
                        totalPossibleM += work.MaxFinalPoints;
                    }
                    else if (hasBaseScores)
                    {
                        scoreText = $"{baseSum:0} / {work.MaxFinalPoints:0} pkt";
                        totalEarnedP += baseSum;
                        totalPossibleM += work.MaxFinalPoints;
                    }
                }

                if (workRecord != null)
                {
                    if (useRetake && !string.IsNullOrWhiteSpace(workRecord.RetakeGroup))
                    {
                        groupText = workRecord.RetakeGroup;
                    }
                    else if (!string.IsNullOrWhiteSpace(workRecord.Group))
                    {
                        groupText = workRecord.Group;
                    }
                }

                DateTime? baseEntered = workRecord?.CustomDateEntered ?? work.DateEntered;
                bool isRetakeDone = workRecord != null && workRecord.IsRetakeActive && hasRetakeScores;
                string deadlineStr = "";

                if (isRetakeDone)
                {
                    deadlineStr = "Poprawiono";
                }
                else
                {
                    DateTime? deadlineDate = workRecord?.RetakeDeadline ?? baseEntered?.AddDays(14);
                    deadlineStr = deadlineDate?.ToString("dd.MM.yyyy") ?? "-";
                }

                worksList.Add(new StudentWorkViewModel
                {
                    WorkId = work.Id,
                    WorkType = work.WorkType, 
                    WorkTitle = work.Title,
                    DeadlineDisplay = deadlineStr, 
                    ScoreDisplay = scoreText,
                    GroupDisplay = groupText
                });
            }

            double average = 0;
            if (totalPossibleM > 0)
            {
                average = Math.Round((totalEarnedP / totalPossibleM) * 100, 0, MidpointRounding.AwayFromZero);
            }
            
            if (dbStudent.AveragePercentage != average)
            {
                dbStudent.AveragePercentage = average;
                _dbContext.SaveChanges();
                StudentUpdated?.Invoke(this, EventArgs.Empty);
            }

            StudentAverageText.Text = $"- Średnia: {average:0}%";
            StudentWorksDataGrid.ItemsSource = worksList;
        }

        private void AddIndividualGrade_Click(object sender, RoutedEventArgs e)
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
                    SchoolClassId = _currentStudent.SchoolClassId,
                    Tasks = dialog.Tasks,
                    IsIndividual = true, // Zaznaczamy jako indywidualna!
                    IndividualStudentId = _currentStudent.Id // Wiążemy z uczniem!
                };

                _dbContext.WrittenWorks.Add(newWork);
                _dbContext.SaveChanges();

                if (newWork.WorkType.ToLower() == "aktywność")
                {
                    // MEGA AUTOMAT: Pomijamy otwieranie okna i z palca ładujemy uczniowi punkty do bazy.
                    var record = new StudentWorkRecord { 
                        StudentId = _currentStudent.Id, 
                        WrittenWorkId = newWork.Id, 
                        CustomDateWritten = newWork.DateWritten,
                        CustomDateEntered = newWork.DateEntered ?? DateTime.Today
                    };
                    _dbContext.StudentWorkRecords.Add(record);
                    
                    var score = new StudentTaskScore { 
                        StudentId = _currentStudent.Id, 
                        WrittenWorkTaskId = newWork.Tasks.First().Id,
                        PointsLevel3 = newWork.MaxFinalPoints 
                    };
                    _dbContext.StudentTaskScores.Add(score);
                    _dbContext.SaveChanges();
                    
                    LoadStudentData(_currentStudent);
                }
                else
                {
                    // Tradycyjna ocena: odpalamy okienko
                    LoadStudentData(_currentStudent);

                    var gradeWindow = new StudentWorkDetailsWindow(_currentStudent.Id, newWork.Id)
                    {
                        Owner = Window.GetWindow(this)
                    };

                    gradeWindow.DataSavedEvent += (s, ev) => 
                    {
                        LoadStudentData(_currentStudent);
                    };

                    gradeWindow.Show();
                }
            }
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
        public string WorkType { get; set; } = string.Empty; 
        public string WorkTitle { get; set; } = string.Empty;
        public string DeadlineDisplay { get; set; } = string.Empty; // NOWE
        public string ScoreDisplay { get; set; } = string.Empty;
        public string GroupDisplay { get; set; } = string.Empty; 
    }
}