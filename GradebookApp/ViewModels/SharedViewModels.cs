using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace GradebookApp.ViewModels
{
    // ---------------------------------------------------------
    // Modele dla widoku wyników pracy (WorkResultsWindow)
    // ---------------------------------------------------------
    public class WorkResultViewModel
    {
        public int StudentId { get; set; }
        public int JournalNumber { get; set; }
        public string StudentName { get; set; } = string.Empty;
        public string DateEnteredDisplay { get; set; } = string.Empty;
        public string DeadlineDisplay { get; set; } = string.Empty;
        public string ScoreDisplay { get; set; } = string.Empty;
        public string GroupDisplay { get; set; } = string.Empty;
    }

    // ---------------------------------------------------------
    // Modele dla szczegółów klasy (ClassDetailsControl)
    // ---------------------------------------------------------
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

    public class RetakeStudentDetailViewModel
    {
        public string StudentName { get; set; } = string.Empty;
        public string OriginalGroup { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
    }

    public class CalendarEventViewModel
    {
        public int EventId { get; set; }
        public int WorkId { get; set; } // Dodane: ID samej pracy pisemnej
        public bool IsRetake { get; set; }
        public DateTime SortDate { get; set; }
        public string DateDisplay { get; set; } = string.Empty;
        public string TimeDisplay { get; set; } = string.Empty;
        public string WorkType { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public List<int> AttendeeIds { get; set; } = new List<int>(); // Dodane: lista ID uczniów
        public string ClassName { get; set; } = string.Empty; // DODAJ TĘ LINIJKĘ

    }

    // ---------------------------------------------------------
    // Modele dla szczegółów ucznia (StudentDetailsControl)
    // ---------------------------------------------------------
    public class StudentWorkViewModel
    {
        public int WorkId { get; set; }
        public string WorkType { get; set; } = string.Empty; 
        public string WorkTitle { get; set; } = string.Empty;
        public string DeadlineDisplay { get; set; } = string.Empty; 
        public string ScoreDisplay { get; set; } = string.Empty;
        public string GroupDisplay { get; set; } = string.Empty; 
    }

    // ---------------------------------------------------------
    // Model dla globalnego dodawania ocen (AddGlobalWorkDialog)
    // ---------------------------------------------------------
    public class ClassSelectionItem
    {
        public int ClassId { get; set; }
        public string ClassName { get; set; } = string.Empty;
        public bool IsSelected { get; set; }
    }

    // ---------------------------------------------------------
    // Model z walidacją dla punktacji (StudentWorkDetailsWindow)
    // ---------------------------------------------------------
    public class TaskScoreViewModel : INotifyPropertyChanged
    {
        public int? BaseTaskId { get; set; }
        public int? RetakeTaskId { get; set; }
        public int TaskNumber { get; set; }

        public Visibility BaseVisibility => BaseTaskId.HasValue ? Visibility.Visible : Visibility.Collapsed;
        public Visibility BaseDashVisibility => BaseTaskId.HasValue ? Visibility.Collapsed : Visibility.Visible;
        
        public Visibility RetakeVisibility => RetakeTaskId.HasValue ? Visibility.Visible : Visibility.Collapsed;
        public Visibility RetakeDashVisibility => RetakeTaskId.HasValue ? Visibility.Collapsed : Visibility.Visible;

        private double? _maxL1;
        public double? MaxL1 { get => _maxL1; set { _maxL1 = value; OnPropertyChanged(); } }
        private double? _maxL2;
        public double? MaxL2 { get => _maxL2; set { _maxL2 = value; OnPropertyChanged(); } }
        private double? _maxL3;
        public double? MaxL3 { get => _maxL3; set { _maxL3 = value; OnPropertyChanged(); } }
        
        private double? _retakeMaxL1;
        public double? RetakeMaxL1 { get => _retakeMaxL1; set { _retakeMaxL1 = value; OnPropertyChanged(); } }
        private double? _retakeMaxL2;
        public double? RetakeMaxL2 { get => _retakeMaxL2; set { _retakeMaxL2 = value; OnPropertyChanged(); } }
        private double? _retakeMaxL3;
        public double? RetakeMaxL3 { get => _retakeMaxL3; set { _retakeMaxL3 = value; OnPropertyChanged(); } }

        private double? _scoreL1;
        public double? ScoreL1 
        { 
            get => _scoreL1; 
            set { if (value.HasValue && value.Value > (MaxL1 ?? 0)) { _scoreL1 = null; } else { _scoreL1 = value; if(value != null) { _scoreL2 = null; _scoreL3 = null; OnBothChangedT1(); } } OnPropertyChanged(); } 
        }
        
        private double? _scoreL2;
        public double? ScoreL2 
        { 
            get => _scoreL2; 
            set { if (value.HasValue && value.Value > (MaxL2 ?? 0)) { _scoreL2 = null; } else { _scoreL2 = value; if(value != null) { _scoreL1 = null; _scoreL3 = null; OnBothChangedT1(); } } OnPropertyChanged(); } 
        }
        
        private double? _scoreL3;
        public double? ScoreL3 
        { 
            get => _scoreL3; 
            set { if (value.HasValue && value.Value > (MaxL3 ?? 0)) { _scoreL3 = null; } else { _scoreL3 = value; if(value != null) { _scoreL1 = null; _scoreL2 = null; OnBothChangedT1(); } } OnPropertyChanged(); } 
        }

        private void OnBothChangedT1() { OnPropertyChanged(nameof(ScoreL1)); OnPropertyChanged(nameof(ScoreL2)); OnPropertyChanged(nameof(ScoreL3)); }

        private double? _retakeScoreL1;
        public double? RetakeScoreL1 
        { 
            get => _retakeScoreL1; 
            set { if (value.HasValue && value.Value > (RetakeMaxL1 ?? 0)) { _retakeScoreL1 = null; } else { _retakeScoreL1 = value; if(value != null) { _retakeScoreL2 = null; _retakeScoreL3 = null; OnBothChangedRetake(); } } OnPropertyChanged(); } 
        }
        
        private double? _retakeScoreL2;
        public double? RetakeScoreL2 
        { 
            get => _retakeScoreL2; 
            set { if (value.HasValue && value.Value > (RetakeMaxL2 ?? 0)) { _retakeScoreL2 = null; } else { _retakeScoreL2 = value; if(value != null) { _retakeScoreL1 = null; _retakeScoreL3 = null; OnBothChangedRetake(); } } OnPropertyChanged(); } 
        }
        
        private double? _retakeScoreL3;
        public double? RetakeScoreL3 
        { 
            get => _retakeScoreL3; 
            set { if (value.HasValue && value.Value > (RetakeMaxL3 ?? 0)) { _retakeScoreL3 = null; } else { _retakeScoreL3 = value; if(value != null) { _retakeScoreL1 = null; _retakeScoreL2 = null; OnBothChangedRetake(); } } OnPropertyChanged(); } 
        }

        private void OnBothChangedRetake() { OnPropertyChanged(nameof(RetakeScoreL1)); OnPropertyChanged(nameof(RetakeScoreL2)); OnPropertyChanged(nameof(RetakeScoreL3)); }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}