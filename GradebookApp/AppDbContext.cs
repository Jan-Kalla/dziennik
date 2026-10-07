using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace GradebookApp
{
    public class SchoolClass
    {
        public int Id { get; set; } 
        public string Name { get; set; } = string.Empty; 
        public List<Student> Students { get; set; } = new List<Student>();
        public List<WrittenWork> WrittenWorks { get; set; } = new List<WrittenWork>();
    }

    public class Student : INotifyPropertyChanged
    {
        public int Id { get; set; }
        
        private int _journalNumber;
        public int JournalNumber { get => _journalNumber; set { _journalNumber = value; OnPropertyChanged(); } }

        private string _firstName = string.Empty;
        public string FirstName { get => _firstName; set { _firstName = value; OnPropertyChanged(); OnPropertyChanged(nameof(FullName)); } }

        private string _lastName = string.Empty;
        public string LastName { get => _lastName; set { _lastName = value; OnPropertyChanged(); OnPropertyChanged(nameof(FullName)); } }

        public string FullName => $"{FirstName} {LastName}";

        private double _averagePercentage;
        public double AveragePercentage { get => _averagePercentage; set { _averagePercentage = value; OnPropertyChanged(); } }

        public int SchoolClassId { get; set; }
        public SchoolClass SchoolClass { get; set; } = null!;
        public List<WrittenWork> WrittenWorks { get; set; } = new List<WrittenWork>();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class WrittenWork
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty; 
        public string WorkType { get; set; } = string.Empty; 
        public double MaxFinalPoints { get; set; }
        public DateTime? DateWritten { get; set; } 
        public DateTime? DateEntered { get; set; } 

        public bool IsIndividual { get; set; } 
        public int? IndividualStudentId { get; set; }

        public bool HasGroups { get; set; }

        public int SchoolClassId { get; set; }
        public SchoolClass SchoolClass { get; set; } = null!;

        public List<Student> Students { get; set; } = new List<Student>();
        public List<WrittenWorkTask> Tasks { get; set; } = new List<WrittenWorkTask>(); 
    }

    public class WrittenWorkTask
    {
        public int Id { get; set; }
        public int TaskNumber { get; set; } 
        public double? MaxPointsLevel1 { get; set; }
        public double? MaxPointsLevel2 { get; set; }
        public double? MaxPointsLevel3 { get; set; }

        public string? GroupName { get; set; }

        public int WrittenWorkId { get; set; }
        public WrittenWork WrittenWork { get; set; } = null!;
    }

    public class StudentWorkRecord
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public Student Student { get; set; } = null!;
        public int WrittenWorkId { get; set; }
        public WrittenWork WrittenWork { get; set; } = null!;
        
        public bool IsAbsent { get; set; }
        
        public string? Group { get; set; } 
        public DateTime? CustomDateWritten { get; set; }
        public DateTime? CustomDateEntered { get; set; }
        
        public bool IsRetakeActive { get; set; }
        public string? RetakeGroup { get; set; } 
        public DateTime? RetakeDeadline { get; set; }
        public DateTime? RetakeDateWritten { get; set; }
        public DateTime? RetakeDateEntered { get; set; }
    }

    public class StudentTaskScore
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public Student Student { get; set; } = null!;
        public int WrittenWorkTaskId { get; set; }
        public WrittenWorkTask WrittenWorkTask { get; set; } = null!;
        
        public double? PointsLevel1 { get; set; }
        public double? PointsLevel2 { get; set; }
        public double? PointsLevel3 { get; set; }
        
        public double? RetakePointsLevel1 { get; set; }
        public double? RetakePointsLevel2 { get; set; }
        public double? RetakePointsLevel3 { get; set; }
    }

    public class PlannedRetake
    {
        public int Id { get; set; }
        public int WrittenWorkId { get; set; }
        public WrittenWork WrittenWork { get; set; } = null!;
        public DateTime Date { get; set; }
        public string Time { get; set; } = string.Empty;
        public List<PlannedRetakeStudent> Attendees { get; set; } = new List<PlannedRetakeStudent>();
    }

    public class PlannedRetakeStudent
    {
        public int Id { get; set; }
        public int PlannedRetakeId { get; set; }
        public PlannedRetake PlannedRetake { get; set; } = null!;
        public int StudentId { get; set; }
        public Student Student { get; set; } = null!;
    }

    // Nowe klasy Szablonów:
    public class WorkTemplate
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string WorkType { get; set; } = string.Empty;
        public double MaxFinalPoints { get; set; }
        public bool HasGroups { get; set; }
        public List<WorkTemplateTask> Tasks { get; set; } = new List<WorkTemplateTask>();
    }

    public class WorkTemplateTask
    {
        public int Id { get; set; }
        public int TaskNumber { get; set; }
        public double? MaxPointsLevel1 { get; set; }
        public double? MaxPointsLevel2 { get; set; }
        public double? MaxPointsLevel3 { get; set; }
        public string? GroupName { get; set; }
        
        public int WorkTemplateId { get; set; }
        public WorkTemplate WorkTemplate { get; set; } = null!;
    }

    public class AppDbContext : DbContext
    {
        public DbSet<SchoolClass> Classes { get; set; }
        public DbSet<Student> Students { get; set; }
        public DbSet<WrittenWork> WrittenWorks { get; set; }
        public DbSet<WrittenWorkTask> WrittenWorkTasks { get; set; }
        public DbSet<StudentTaskScore> StudentTaskScores { get; set; }
        public DbSet<StudentWorkRecord> StudentWorkRecords { get; set; } 
        public DbSet<PlannedRetake> PlannedRetakes { get; set; }
        public DbSet<PlannedRetakeStudent> PlannedRetakeStudents { get; set; }
        
        public DbSet<WorkTemplate> WorkTemplates { get; set; }
        public DbSet<WorkTemplateTask> WorkTemplateTasks { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite("Data Source=gradebook.db")
                          .LogTo(message => System.Diagnostics.Debug.WriteLine(message), Microsoft.Extensions.Logging.LogLevel.Information);
        }
    }

    public static class GradeCalculator
    {
        public static double CalculateWorkScore(double M, IEnumerable<WrittenWorkTask> tasks, IEnumerable<StudentTaskScore> scores, bool isRetake)
        {
            if (tasks == null || !tasks.Any() || M <= 0) return 0;

            double alpha1 = 0.54;
            double alpha2 = 0.9;
            
            int n = tasks.Count(); 
            double sumQ1 = 0, sumQ2 = 0, sumQ3 = 0;

            foreach (var task in tasks)
            {
                var score = scores.FirstOrDefault(s => s.WrittenWorkTaskId == task.Id);
                
                double p1 = isRetake ? (score?.RetakePointsLevel1 ?? 0) : (score?.PointsLevel1 ?? 0);
                double p2 = isRetake ? (score?.RetakePointsLevel2 ?? 0) : (score?.PointsLevel2 ?? 0);
                double p3 = isRetake ? (score?.RetakePointsLevel3 ?? 0) : (score?.PointsLevel3 ?? 0);

                double P1 = task.MaxPointsLevel1 ?? 0;
                double P2 = task.MaxPointsLevel2 ?? 0;
                double P3 = task.MaxPointsLevel3 ?? 0;

                sumQ1 += (P1 > 0) ? (p1 / P1) : 0;
                sumQ2 += (P2 > 0) ? (p2 / P2) : 0;
                sumQ3 += (P3 > 0) ? (p3 / P3) : 0;
            }

            double r1 = sumQ1 / n;
            double r2 = sumQ2 / n;
            double r3 = sumQ3 / n;

            return Math.Round(M * (alpha1 * r1 + alpha2 * r2 + r3), 0, MidpointRounding.AwayFromZero);
        }
    }
}