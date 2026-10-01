using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        
        // Trzymamy w pamięci stałą listę wszystkich pobranych wyników, by mieć po czym wyszukiwać
        private List<WorkResultViewModel> _allResults = new List<WorkResultViewModel>();
        
        // Zmienna widokowa podpięta pod tabelę - zmienia się na żywo w trakcie wpisywania liter
        public ObservableCollection<WorkResultViewModel> DisplayedResults { get; set; } = new ObservableCollection<WorkResultViewModel>();

        public WorkResultsWindow(int workId, int classId)
        {
            InitializeComponent();
            _dbContext = new AppDbContext();
            _workId = workId;
            _classId = classId;
            
            ResultsDataGrid.ItemsSource = DisplayedResults;
            LoadData();
        }

        private void LoadData()
        {
            _dbContext.ChangeTracker.Clear();
            _allResults.Clear();

            var work = _dbContext.WrittenWorks.Include(w => w.Tasks).FirstOrDefault(w => w.Id == _workId);
            var students = _dbContext.Students.Where(s => s.SchoolClassId == _classId).OrderBy(s => s.JournalNumber).ToList();
            
            if (work == null) return;

            TitleText.Text = $"Wyniki: {work.WorkType.ToUpper()} - {work.Title}";

            var scores = _dbContext.StudentTaskScores.Where(s => s.WrittenWorkTask.WrittenWorkId == _workId).ToList();
            var records = _dbContext.StudentWorkRecords.Where(r => r.WrittenWorkId == _workId).ToList();

            foreach (var student in students)
            {
                var studentScores = scores.Where(s => s.StudentId == student.Id).ToList();
                var record = records.FirstOrDefault(r => r.StudentId == student.Id);

                bool hasBaseScores = studentScores.Any(s => s.PointsLevel1.HasValue || s.PointsLevel2.HasValue || s.PointsLevel3.HasValue);
                bool hasRetakeScores = studentScores.Any(s => s.RetakePointsLevel1.HasValue || s.RetakePointsLevel2.HasValue || s.RetakePointsLevel3.HasValue);

                string scoreText = "Brak ocen";
                string groupText = "-";
                bool useRetake = false;

                if (record != null && record.IsAbsent)
                {
                    scoreText = "NB";
                }
                else
                {
                    double baseSum = 0;
                    double retakeSum = 0;

                    if (hasBaseScores || hasRetakeScores)
                    {
                        baseSum = GradeCalculator.CalculateWorkScore(work.MaxFinalPoints, work.Tasks, studentScores, false);
                        retakeSum = GradeCalculator.CalculateWorkScore(work.MaxFinalPoints, work.Tasks, studentScores, true);
                    }

                    useRetake = record != null && record.IsRetakeActive && hasRetakeScores && retakeSum > baseSum;

                    if (useRetake)
                    {
                        scoreText = $"{retakeSum:0} / {work.MaxFinalPoints:0} pkt (popr.)";
                    }
                    else if (hasBaseScores)
                    {
                        scoreText = $"{baseSum:0} / {work.MaxFinalPoints:0} pkt";
                    }
                }

                if (record != null)
                {
                    if (useRetake && !string.IsNullOrWhiteSpace(record.RetakeGroup))
                        groupText = record.RetakeGroup;
                    else if (!string.IsNullOrWhiteSpace(record.Group))
                        groupText = record.Group;
                }

                DateTime? baseEntered = record?.CustomDateEntered ?? work.DateEntered;
                bool isRetakeDone = record != null && record.IsRetakeActive && hasRetakeScores;
                string deadlineStr = "-";

                if (isRetakeDone)
                {
                    deadlineStr = "Poprawiono";
                }
                else if (baseEntered.HasValue)
                {
                    DateTime deadlineDate = record?.RetakeDeadline ?? baseEntered.Value.AddDays(14);
                    deadlineStr = deadlineDate.ToString("dd.MM.yyyy");
                }

                string enteredStr = baseEntered?.ToString("dd.MM.yyyy") ?? "Brak";

                _allResults.Add(new WorkResultViewModel
                {
                    StudentId = student.Id,
                    JournalNumber = student.JournalNumber,
                    StudentName = student.FullName,
                    DateEnteredDisplay = enteredStr,
                    DeadlineDisplay = deadlineStr,
                    ScoreDisplay = scoreText,
                    GroupDisplay = groupText
                });
            }

            // Odtwarzamy filtr wyszukiwarki (gdy wracamy z oceniania, wyszukiwarka zostaje zachowana!)
            SearchTextBox_TextChanged(this, null!);
        }

        private void RefreshDisplayedResults(List<WorkResultViewModel> resultsToShow)
        {
            DisplayedResults.Clear();
            foreach (var res in resultsToShow)
            {
                DisplayedResults.Add(res);
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SearchTextBox == null) return;
            
            string query = SearchTextBox.Text.Trim().ToLower();

            if (string.IsNullOrEmpty(query))
            {
                RefreshDisplayedResults(_allResults);
            }
            else
            {
                var filtered = _allResults.Where(r => r.StudentName.ToLower().Contains(query)).ToList();
                RefreshDisplayedResults(filtered);
            }
        }

        private void GradeStudent_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is WorkResultViewModel vm)
            {
                var window = new StudentWorkDetailsWindow(vm.StudentId, _workId)
                {
                    Owner = this
                };

                // Kiedy nauczyciel zapisze oceny i zamknie tamto okno, ładujemy od nowa dane w tym oknie
                window.DataSavedEvent += (s, ev) => 
                {
                    LoadData();
                };

                window.Show();
            }
        }
    }

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
}