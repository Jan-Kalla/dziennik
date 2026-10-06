using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using GradebookApp.ViewModels;

namespace GradebookApp.Services
{
    public class GradebookDataService : IDisposable
    {
        private readonly AppDbContext _dbContext;

        public GradebookDataService()
        {
            _dbContext = new AppDbContext();
        }

        public void ApplyMigrations() => _dbContext.Database.Migrate();
        public void Dispose() => _dbContext.Dispose();

        // ====================================================================
        // ZARZĄDZANIE KLASAMI I GLOBALNYMI PRACAMI
        // ====================================================================
        public List<SchoolClass> GetAllClasses() => _dbContext.Classes.ToList();

        public SchoolClass AddClass(string className)
        {
            var newClass = new SchoolClass { Name = className };
            _dbContext.Classes.Add(newClass);
            _dbContext.SaveChanges();
            return newClass;
        }

        public void UpdateClassName(SchoolClass schoolClass, string newName)
        {
            var tracked = _dbContext.Classes.Find(schoolClass.Id);
            if (tracked != null) { tracked.Name = newName; _dbContext.SaveChanges(); }
        }

        public void DeleteClass(SchoolClass schoolClass)
        {
            var tracked = _dbContext.Classes.Find(schoolClass.Id);
            if (tracked != null) { _dbContext.Classes.Remove(tracked); _dbContext.SaveChanges(); }
        }

        public void AddGlobalWorkToClasses(List<int> classIds, string title, string type, double maxPoints, DateTime? written, DateTime? entered, List<WrittenWorkTask> tasks)
        {
            foreach (var classId in classIds)
            {
                var newWork = new WrittenWork 
                { 
                    Title = title, WorkType = type, MaxFinalPoints = maxPoints, DateWritten = written, DateEntered = entered, SchoolClassId = classId,
                    Tasks = tasks.Select(t => new WrittenWorkTask { TaskNumber = t.TaskNumber, MaxPointsLevel1 = t.MaxPointsLevel1, MaxPointsLevel2 = t.MaxPointsLevel2, MaxPointsLevel3 = t.MaxPointsLevel3 }).ToList()
                };
                _dbContext.WrittenWorks.Add(newWork);
            }
            _dbContext.SaveChanges();
        }

        public List<RetakeStudentDetailViewModel> GetRetakeStudentDetails(int workId, List<int> attendeeIds)
        {
            var details = new List<RetakeStudentDetailViewModel>();
            var students = _dbContext.Students.Where(s => attendeeIds.Contains(s.Id)).ToList();
            var records = _dbContext.StudentWorkRecords.Where(r => r.WrittenWorkId == workId && attendeeIds.Contains(r.StudentId)).ToList();
            var allScores = _dbContext.StudentTaskScores.Where(s => s.WrittenWorkTask.WrittenWorkId == workId && attendeeIds.Contains(s.StudentId)).ToList();

            foreach (var student in students)
            {
                var record = records.FirstOrDefault(r => r.StudentId == student.Id);
                var scores = allScores.Where(s => s.StudentId == student.Id).ToList();

                string group = "-";
                string eventType = "Pierwsze podejście";

                if (record != null)
                {
                    if (record.IsAbsent)
                    {
                        group = "NB";
                    }
                    else
                    {
                        group = string.IsNullOrEmpty(record.Group) ? "-" : record.Group;
                    }
                }

                // Jeżeli uczeń ma wpisane jakiekolwiek bazowe oceny punktowe (nie jest pusto) i nie jest NB, traktujemy to jako poprawę.
                bool hasBaseScores = scores.Any(s => s.PointsLevel1.HasValue || s.PointsLevel2.HasValue || s.PointsLevel3.HasValue);
                if (hasBaseScores && (record == null || !record.IsAbsent))
                {
                    eventType = "Poprawa";
                }

                details.Add(new RetakeStudentDetailViewModel
                {
                    StudentName = student.FullName,
                    OriginalGroup = group,
                    EventType = eventType
                });
            }

            return details.OrderBy(d => d.StudentName).ToList();
        }

        // ====================================================================
        // ZARZĄDZANIE UCZNIAMI I WIDOK KLASY
        // ====================================================================
        public List<Student> GetStudentsByClass(int classId)
        {
            _dbContext.ChangeTracker.Clear();
            return _dbContext.Students.Where(s => s.SchoolClassId == classId).Include(s => s.WrittenWorks).ToList();
        }

        public List<WrittenWork> GetWorksByClass(int classId)
        {
            _dbContext.ChangeTracker.Clear();
            return _dbContext.WrittenWorks.Where(w => w.SchoolClassId == classId).Include(w => w.Tasks).ToList();
        }

        public Student AddStudent(string firstName, string lastName, int classId, int journalNumber)
        {
            var student = new Student { FirstName = firstName, LastName = lastName, SchoolClassId = classId, JournalNumber = journalNumber };
            _dbContext.Students.Add(student);
            _dbContext.SaveChanges();
            return student;
        }

        public void UpdateStudent(Student student)
        {
            var tracked = _dbContext.Students.Find(student.Id);
            if (tracked != null)
            {
                tracked.FirstName = student.FirstName; tracked.LastName = student.LastName; 
                tracked.JournalNumber = student.JournalNumber; tracked.AveragePercentage = student.AveragePercentage;
                _dbContext.SaveChanges();
            }
        }

        public void DeleteStudent(Student student)
        {
            var tracked = _dbContext.Students.Find(student.Id);
            if (tracked != null) { _dbContext.Students.Remove(tracked); _dbContext.SaveChanges(); }
        }

        public void RecalculateClassAverages(int classId)
        {
            var allStudents = GetStudentsByClass(classId);
            var classWorks = GetWorksByClass(classId);
            var allScores = _dbContext.StudentTaskScores.Where(s => s.Student.SchoolClassId == classId).ToList();
            var allRecords = _dbContext.StudentWorkRecords.Where(r => r.Student.SchoolClassId == classId).ToList();

            foreach (var student in allStudents)
            {
                var studentScores = allScores.Where(s => s.StudentId == student.Id).ToList();
                var studentRecords = allRecords.Where(r => r.StudentId == student.Id).ToList();

                double totalEarnedP = 0, totalPossibleM = 0;

                foreach (var work in classWorks)
                {
                    var workScores = studentScores.Where(s => work.Tasks.Any(t => t.Id == s.WrittenWorkTaskId)).ToList();
                    var workRecord = studentRecords.FirstOrDefault(r => r.WrittenWorkId == work.Id);
                    
                    bool hasBaseScores = workScores.Any(s => s.PointsLevel1.HasValue || s.PointsLevel2.HasValue || s.PointsLevel3.HasValue);
                    bool hasRetakeScores = workScores.Any(s => s.RetakePointsLevel1.HasValue || s.RetakePointsLevel2.HasValue || s.RetakePointsLevel3.HasValue);

                    if (workRecord != null && workRecord.IsAbsent) continue;

                    double baseSum = (hasBaseScores || hasRetakeScores) ? GradeCalculator.CalculateWorkScore(work.MaxFinalPoints, work.Tasks, workScores, false) : 0;
                    double retakeSum = (hasBaseScores || hasRetakeScores) ? GradeCalculator.CalculateWorkScore(work.MaxFinalPoints, work.Tasks, workScores, true) : 0;

                    if (workRecord != null && workRecord.IsRetakeActive && hasRetakeScores && retakeSum > baseSum)
                    {
                        totalEarnedP += retakeSum; totalPossibleM += work.MaxFinalPoints;
                    }
                    else if (hasBaseScores)
                    {
                        totalEarnedP += baseSum; totalPossibleM += work.MaxFinalPoints;
                    }
                }

                double average = totalPossibleM > 0 ? Math.Round((totalEarnedP / totalPossibleM) * 100, 0, MidpointRounding.AwayFromZero) : 0;
                if (student.AveragePercentage != average)
                {
                    var tracked = _dbContext.Students.Find(student.Id);
                    if(tracked != null) { tracked.AveragePercentage = average; _dbContext.SaveChanges(); }
                }
            }
        }

        public int GetGradedStudentsCountForWork(int classId, WrittenWork work)
        {
            var taskIds = work.Tasks.Select(t => t.Id).ToList();
            var absentIds = _dbContext.StudentWorkRecords.Where(r => r.WrittenWorkId == work.Id && r.IsAbsent).Select(r => r.StudentId).ToList();
            return _dbContext.StudentTaskScores
                .Where(s => taskIds.Contains(s.WrittenWorkTaskId) && !absentIds.Contains(s.StudentId) && 
                            (s.PointsLevel1 != null || s.PointsLevel2 != null || s.PointsLevel3 != null || s.RetakePointsLevel1 != null || s.RetakePointsLevel2 != null || s.RetakePointsLevel3 != null))
                .Select(s => s.StudentId).Distinct().Count();
        }

        public List<PlannedRetake> GetUpcomingRetakes(int classId, DateTime fromDate)
        {
            return _dbContext.PlannedRetakes.Include(r => r.WrittenWork).Include(r => r.Attendees).ThenInclude(a => a.Student)
                .Where(r => r.WrittenWork.SchoolClassId == classId && r.Date >= fromDate).ToList();
        }

        public void DeletePlannedRetake(int retakeId)
        {
            var retake = _dbContext.PlannedRetakes.Find(retakeId);
            if (retake != null) { _dbContext.PlannedRetakes.Remove(retake); _dbContext.SaveChanges(); }
        }

        // ====================================================================
        // ZARZĄDZANIE PRACAMI PISEMNYMI
        // ====================================================================
        public WrittenWork AddWork(WrittenWork work)
        {
            _dbContext.WrittenWorks.Add(work);
            _dbContext.SaveChanges();
            return work;
        }

        public WrittenWork? GetWorkById(int workId) => _dbContext.WrittenWorks.Find(workId);
        
        public WrittenWork? GetWorkByIdWithTasks(int workId)
        {
            _dbContext.ChangeTracker.Clear();
            return _dbContext.WrittenWorks.Include(w => w.Tasks).FirstOrDefault(w => w.Id == workId);
        }

        public void UpdateWorkAndTasks(WrittenWork work, List<WrittenWorkTask> generatedTasks, string newTitle, double maxPoints, string type, bool hasGroups, DateTime? dateWritten, DateTime? dateEntered)
        {
            var trackedWork = _dbContext.WrittenWorks.Include(w => w.Tasks).FirstOrDefault(w => w.Id == work.Id);
            if (trackedWork == null) return;

            foreach (var oldTask in trackedWork.Tasks.ToList())
            {
                var match = generatedTasks.FirstOrDefault(t => t.GroupName == oldTask.GroupName && t.TaskNumber == oldTask.TaskNumber);
                if (match != null)
                {
                    oldTask.MaxPointsLevel1 = match.MaxPointsLevel1; oldTask.MaxPointsLevel2 = match.MaxPointsLevel2; oldTask.MaxPointsLevel3 = match.MaxPointsLevel3;
                    generatedTasks.Remove(match);
                }
                else
                {
                    _dbContext.WrittenWorkTasks.Remove(oldTask);
                }
            }
            
            foreach (var newTask in generatedTasks) trackedWork.Tasks.Add(newTask);

            trackedWork.Title = newTitle; trackedWork.MaxFinalPoints = maxPoints; trackedWork.WorkType = type; trackedWork.HasGroups = hasGroups; trackedWork.DateWritten = dateWritten; trackedWork.DateEntered = dateEntered;
            _dbContext.SaveChanges();
        }

        public void DeleteWork(WrittenWork work)
        {
            var tracked = _dbContext.WrittenWorks.Find(work.Id);
            if (tracked != null) { _dbContext.WrittenWorks.Remove(tracked); _dbContext.SaveChanges(); }
        }

        // ====================================================================
        // SZCZEGÓŁY UCZNIA I ZAPISYWANIE WYNIKÓW
        // ====================================================================
        public Student? GetStudentById(int studentId)
        {
            _dbContext.ChangeTracker.Clear();
            return _dbContext.Students.Include(s => s.SchoolClass).FirstOrDefault(s => s.Id == studentId);
        }

        public (List<StudentWorkViewModel> WorksList, double Average) GetStudentWorksAndRecalculateAverage(Student dbStudent)
        {
            var classWorks = _dbContext.WrittenWorks.Include(w => w.Tasks).Where(w => w.SchoolClassId == dbStudent.SchoolClassId && (!w.IsIndividual || w.IndividualStudentId == dbStudent.Id)).ToList();
            var studentScores = _dbContext.StudentTaskScores.Where(s => s.StudentId == dbStudent.Id).ToList();
            var studentRecords = _dbContext.StudentWorkRecords.Where(r => r.StudentId == dbStudent.Id).ToList();
            var worksList = new List<StudentWorkViewModel>();
            double totalEarnedP = 0, totalPossibleM = 0;

            foreach (var work in classWorks)
            {
                var workScores = studentScores.Where(s => work.Tasks.Any(t => t.Id == s.WrittenWorkTaskId)).ToList();
                var workRecord = studentRecords.FirstOrDefault(r => r.WrittenWorkId == work.Id);
                
                bool hasBase = workScores.Any(s => s.PointsLevel1.HasValue || s.PointsLevel2.HasValue || s.PointsLevel3.HasValue);
                bool hasRetake = workScores.Any(s => s.RetakePointsLevel1.HasValue || s.RetakePointsLevel2.HasValue || s.RetakePointsLevel3.HasValue);

                string scoreText = "Brak ocen", groupText = "-"; 
                bool useRetake = false;
                
                if (workRecord != null && workRecord.IsAbsent) scoreText = "NB"; 
                else 
                {
                    double baseSum = (hasBase || hasRetake) ? GradeCalculator.CalculateWorkScore(work.MaxFinalPoints, work.Tasks, workScores, false) : 0;
                    double retakeSum = (hasBase || hasRetake) ? GradeCalculator.CalculateWorkScore(work.MaxFinalPoints, work.Tasks, workScores, true) : 0;
                    useRetake = workRecord != null && workRecord.IsRetakeActive && hasRetake && retakeSum > baseSum;

                    if (useRetake) { scoreText = $"{retakeSum:0} / {work.MaxFinalPoints:0} pkt (popr.)"; totalEarnedP += retakeSum; totalPossibleM += work.MaxFinalPoints; }
                    else if (hasBase) { scoreText = $"{baseSum:0} / {work.MaxFinalPoints:0} pkt"; totalEarnedP += baseSum; totalPossibleM += work.MaxFinalPoints; }
                }

                if (workRecord != null) groupText = (useRetake && !string.IsNullOrWhiteSpace(workRecord.RetakeGroup)) ? workRecord.RetakeGroup : (!string.IsNullOrWhiteSpace(workRecord.Group) ? workRecord.Group : "-");

                bool isRetakeDone = workRecord != null && workRecord.IsRetakeActive && hasRetake;
                string deadlineStr = isRetakeDone ? "Poprawiono" : (workRecord?.RetakeDeadline ?? (workRecord?.CustomDateEntered ?? work.DateEntered)?.AddDays(14))?.ToString("dd.MM.yyyy") ?? "-";

                worksList.Add(new StudentWorkViewModel { WorkId = work.Id, WorkType = work.WorkType, WorkTitle = work.Title, DeadlineDisplay = deadlineStr, ScoreDisplay = scoreText, GroupDisplay = groupText });
            }

            double average = totalPossibleM > 0 ? Math.Round((totalEarnedP / totalPossibleM) * 100, 0, MidpointRounding.AwayFromZero) : 0;
            if (dbStudent.AveragePercentage != average) 
            { 
                var tracked = _dbContext.Students.Find(dbStudent.Id);
                if(tracked != null) { tracked.AveragePercentage = average; _dbContext.SaveChanges(); }
            }

            return (worksList, average);
        }

        public WrittenWork AddIndividualWork(int studentId, int classId, string title, string type, double maxPoints, DateTime? written, DateTime? entered, List<WrittenWorkTask> tasks)
        {
            var newWork = new WrittenWork { Title = title, WorkType = type, MaxFinalPoints = maxPoints, DateWritten = written, DateEntered = entered, SchoolClassId = classId, Tasks = tasks, IsIndividual = true, IndividualStudentId = studentId };
            _dbContext.WrittenWorks.Add(newWork);
            _dbContext.SaveChanges();
            return newWork;
        }

        public void SaveActivityScore(int studentId, WrittenWork work)
        {
            _dbContext.StudentWorkRecords.Add(new StudentWorkRecord { StudentId = studentId, WrittenWorkId = work.Id, CustomDateWritten = work.DateWritten, CustomDateEntered = work.DateEntered ?? DateTime.Today });
            _dbContext.StudentTaskScores.Add(new StudentTaskScore { StudentId = studentId, WrittenWorkTaskId = work.Tasks.First().Id, PointsLevel3 = work.MaxFinalPoints });
            _dbContext.SaveChanges();
        }

        public List<StudentTaskScore> GetStudentScores(int studentId) => _dbContext.StudentTaskScores.Where(s => s.StudentId == studentId).ToList();
        
        public StudentWorkRecord? GetWorkRecord(int studentId, int workId) => _dbContext.StudentWorkRecords.FirstOrDefault(r => r.StudentId == studentId && r.WrittenWorkId == workId);

        public void SaveStudentWorkRecordAndScores(StudentWorkRecord updatedRecord, List<StudentTaskScore> generatedScores, bool isRetakeActive)
        {
            var existingRecord = _dbContext.StudentWorkRecords.FirstOrDefault(r => r.StudentId == updatedRecord.StudentId && r.WrittenWorkId == updatedRecord.WrittenWorkId);
            if (existingRecord == null) _dbContext.StudentWorkRecords.Add(updatedRecord);
            else
            {
                existingRecord.Group = updatedRecord.Group; existingRecord.CustomDateWritten = updatedRecord.CustomDateWritten; existingRecord.CustomDateEntered = updatedRecord.CustomDateEntered;
                existingRecord.IsAbsent = updatedRecord.IsAbsent; existingRecord.RetakeGroup = updatedRecord.RetakeGroup; existingRecord.IsRetakeActive = updatedRecord.IsRetakeActive;
                existingRecord.RetakeDeadline = updatedRecord.RetakeDeadline; existingRecord.RetakeDateWritten = updatedRecord.RetakeDateWritten; existingRecord.RetakeDateEntered = updatedRecord.RetakeDateEntered;
            }

            foreach (var score in generatedScores)
            {
                var existingScore = _dbContext.StudentTaskScores.FirstOrDefault(s => s.StudentId == score.StudentId && s.WrittenWorkTaskId == score.WrittenWorkTaskId);
                if (existingScore == null) _dbContext.StudentTaskScores.Add(score);
                else
                {
                    if (score.PointsLevel1 != null || score.PointsLevel2 != null || score.PointsLevel3 != null)
                    {
                        existingScore.PointsLevel1 = score.PointsLevel1; existingScore.PointsLevel2 = score.PointsLevel2; existingScore.PointsLevel3 = score.PointsLevel3;
                    }
                    if (isRetakeActive)
                    {
                        existingScore.RetakePointsLevel1 = score.RetakePointsLevel1; existingScore.RetakePointsLevel2 = score.RetakePointsLevel2; existingScore.RetakePointsLevel3 = score.RetakePointsLevel3;
                    }
                    else
                    {
                        existingScore.RetakePointsLevel1 = null; existingScore.RetakePointsLevel2 = null; existingScore.RetakePointsLevel3 = null;
                    }
                }
            }
            _dbContext.SaveChanges();
        }
    }
}