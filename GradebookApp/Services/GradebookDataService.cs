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
        
        public WorkStatisticsResult CalculateStatistics(int? classId, List<int>? workIds, bool includeFailedRetakes = false, bool includeStillFailedRetakes = false)
        {
            var result = new WorkStatisticsResult();
            
            var recordsQuery = _dbContext.StudentWorkRecords
                                  .Include(r => r.Student)
                                  .Where(r => r.IsRetakeActive);

            if (classId.HasValue) recordsQuery = recordsQuery.Where(r => r.Student.SchoolClassId == classId.Value);
            if (workIds != null && workIds.Any()) recordsQuery = recordsQuery.Where(r => workIds.Contains(r.WrittenWorkId));

            var records = recordsQuery.ToList();
            if (!records.Any()) return result;

            var fetchedWorkIds = records.Select(r => r.WrittenWorkId).Distinct().ToList();
            var works = _dbContext.WrittenWorks.Include(w => w.Tasks).Where(w => fetchedWorkIds.Contains(w.Id)).ToDictionary(w => w.Id);

            var studentIds = records.Select(r => r.StudentId).Distinct().ToList();
            var scoresQuery = _dbContext.StudentTaskScores.Include(s => s.WrittenWorkTask).AsQueryable();
            
            if (classId.HasValue) scoresQuery = scoresQuery.Where(s => s.Student.SchoolClassId == classId.Value);
            else scoresQuery = scoresQuery.Where(s => studentIds.Contains(s.StudentId));

            if (workIds != null && workIds.Any()) scoresQuery = scoresQuery.Where(s => workIds.Contains(s.WrittenWorkTask.WrittenWorkId));

            var scores = scoresQuery.ToList();

            double totalPercentagePointsGain = 0, gainFrom1 = 0, gainFrom2 = 0;
            int studentsWithValidRetakeScores = 0, studentsGainFrom1 = 0, studentsGainFrom2 = 0;
            int actualGradedRetakesCount = 0; // Poprawiony licznik prac

            foreach (var record in records)
            {
                if (!works.TryGetValue(record.WrittenWorkId, out var work) || work.MaxFinalPoints <= 0) continue;

                var studentScores = scores.Where(s => s.StudentId == record.StudentId && s.WrittenWorkTask.WrittenWorkId == work.Id).ToList();
                
                // Ignorujemy tych, którzy nie dostali jeszcze ocen (naprawia licznik 28->20)
                bool hasRetakeScores = studentScores.Any(s => s.RetakePointsLevel1.HasValue || s.RetakePointsLevel2.HasValue || s.RetakePointsLevel3.HasValue);
                if (!hasRetakeScores) continue;

                actualGradedRetakesCount++;

                var baseTasks = work.Tasks.AsEnumerable();
                var retakeTasks = work.Tasks.AsEnumerable();

                if (work.HasGroups)
                {
                    if (!string.IsNullOrEmpty(record.Group)) baseTasks = work.Tasks.Where(t => t.GroupName == record.Group);
                    
                    if (!string.IsNullOrEmpty(record.RetakeGroup)) retakeTasks = work.Tasks.Where(t => t.GroupName == record.RetakeGroup);
                    else if (!string.IsNullOrEmpty(record.Group)) retakeTasks = work.Tasks.Where(t => t.GroupName == record.Group);
                }

                double baseScore = GradeCalculator.CalculateWorkScore(work.MaxFinalPoints, baseTasks, studentScores, false);
                double retakeScore = GradeCalculator.CalculateWorkScore(work.MaxFinalPoints, retakeTasks, studentScores, true);

                double basePercentage = (baseScore / work.MaxFinalPoints) * 100.0;
                double retakePercentage = (retakeScore / work.MaxFinalPoints) * 100.0;
                
                double pointGain = retakeScore > baseScore ? retakeScore - baseScore : 0;
                double pctGain = (pointGain / work.MaxFinalPoints) * 100.0;

                bool isBase1 = basePercentage < 40.0; 
                bool isSuccess = retakeScore > baseScore; 
                bool isStillFailed = retakePercentage < 40.0;

                if (isBase1)
                {
                    if (isSuccess && !isStillFailed) result.SuccessfulRetakesFrom1To2Plus++;
                    else if (isSuccess && isStillFailed) result.SuccessfulRetakesFrom1To1++;
                    else result.FailedRetakesFrom1++;

                    bool includeInAverage = false;
                    if (isSuccess && !isStillFailed) includeInAverage = true; 
                    if (isSuccess && isStillFailed && includeStillFailedRetakes) includeInAverage = true; 
                    if (!isSuccess && includeFailedRetakes) includeInAverage = true; 

                    if (includeInAverage)
                    {
                        gainFrom1 += pctGain; studentsGainFrom1++; 
                        totalPercentagePointsGain += pctGain; studentsWithValidRetakeScores++;
                    }
                }
                else
                {
                    if (isSuccess) result.SuccessfulRetakesFrom2Plus++; 
                    else result.FailedRetakesFrom2Plus++;
                    
                    if (isSuccess || (!isSuccess && includeFailedRetakes))
                    {
                        gainFrom2 += pctGain; studentsGainFrom2++; 
                        totalPercentagePointsGain += pctGain; studentsWithValidRetakeScores++;
                    }
                }
            }

            result.TotalRetakes = actualGradedRetakesCount; 
            if (studentsWithValidRetakeScores > 0) result.AveragePercentagePointGain = totalPercentagePointsGain / studentsWithValidRetakeScores;
            if (studentsGainFrom1 > 0) result.AveragePercentagePointGainFrom1 = gainFrom1 / studentsGainFrom1;
            if (studentsGainFrom2 > 0) result.AveragePercentagePointGainFrom2Plus = gainFrom2 / studentsGainFrom2;

            return result;
        }

        public void ApplyMigrations() => _dbContext.Database.Migrate();
        public void Dispose() => _dbContext.Dispose();

        public List<SchoolClass> GetAllClasses() => _dbContext.Classes.ToList();

        public SchoolClass AddClass(string className)
        {
            var newClass = new SchoolClass { Name = className };
            _dbContext.Classes.Add(newClass); _dbContext.SaveChanges(); return newClass;
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

        public List<WorkTemplate> GetAllTemplates() => _dbContext.WorkTemplates.Include(t => t.Tasks).ToList();
        
        public void AddTemplate(WorkTemplate template)
        {
            _dbContext.WorkTemplates.Add(template); _dbContext.SaveChanges();
        }

        public void UpdateTemplate(WorkTemplate template, List<WorkTemplateTask> generatedTasks, string newTitle, double maxPoints, string type, bool hasGroups)
        {
            var tracked = _dbContext.WorkTemplates.Include(t => t.Tasks).FirstOrDefault(t => t.Id == template.Id);
            if (tracked == null) return;

            foreach (var oldTask in tracked.Tasks.ToList())
            {
                string oldGrp = string.IsNullOrEmpty(oldTask.GroupName) ? "A" : oldTask.GroupName;
                var match = generatedTasks.FirstOrDefault(t => (string.IsNullOrEmpty(t.GroupName) ? "A" : t.GroupName) == oldGrp && t.TaskNumber == oldTask.TaskNumber);
                
                if (match != null)
                {
                    oldTask.MaxPointsLevel1 = match.MaxPointsLevel1; oldTask.MaxPointsLevel2 = match.MaxPointsLevel2; oldTask.MaxPointsLevel3 = match.MaxPointsLevel3;
                    oldTask.GroupName = match.GroupName;
                    generatedTasks.Remove(match);
                }
                else _dbContext.WorkTemplateTasks.Remove(oldTask);
            }
            foreach (var newTask in generatedTasks) tracked.Tasks.Add(newTask);
            
            tracked.Title = newTitle; tracked.MaxFinalPoints = maxPoints; tracked.WorkType = type; tracked.HasGroups = hasGroups;
            _dbContext.SaveChanges();
        }

        public void DeleteTemplate(int templateId)
        {
            var tracked = _dbContext.WorkTemplates.Include(t => t.Tasks).FirstOrDefault(t => t.Id == templateId);
            if (tracked != null) { _dbContext.WorkTemplateTasks.RemoveRange(tracked.Tasks); _dbContext.WorkTemplates.Remove(tracked); _dbContext.SaveChanges(); }
        }

        public List<CalendarEventViewModel> GetGlobalCalendarEvents(bool upcomingOnly, bool includeAllWorks)
        {
            _dbContext.ChangeTracker.Clear(); 
            var events = new List<CalendarEventViewModel>();
            var today = DateTime.Today;

            var retakesQuery = _dbContext.PlannedRetakes.Include(r => r.WrittenWork).ThenInclude(w => w.SchoolClass).Include(r => r.Attendees).AsQueryable();
            if (upcomingOnly) retakesQuery = retakesQuery.Where(r => r.Date >= today);
            
            foreach(var r in retakesQuery.ToList()) {
                events.Add(new CalendarEventViewModel {
                    EventId = r.Id, WorkId = r.WrittenWorkId, IsRetake = true, SortDate = r.Date,
                    DateDisplay = r.Date.ToString("dd.MM.yyyy"), TimeDisplay = string.IsNullOrEmpty(r.Time) ? "-" : r.Time,
                    WorkType = r.WrittenWork.WorkType, Title = r.WrittenWork.Title, ClassName = r.WrittenWork.SchoolClass.Name,
                    AttendeeIds = r.Attendees.Select(a => a.StudentId).ToList(), AttendeesCountDisplay = r.Attendees.Count.ToString()
                });
            }

            if (includeAllWorks)
            {
                var worksQuery = _dbContext.WrittenWorks.Include(w => w.SchoolClass).ThenInclude(c => c.Students).Where(w => !w.IsIndividual).AsQueryable();
                if (upcomingOnly) worksQuery = worksQuery.Where(w => w.DateWritten >= today);

                foreach(var w in worksQuery.ToList()) {
                    events.Add(new CalendarEventViewModel {
                        EventId = w.Id, WorkId = w.Id, IsRetake = false, SortDate = w.DateWritten ?? DateTime.MinValue,
                        DateDisplay = w.DateWritten?.ToString("dd.MM.yyyy") ?? "Brak", TimeDisplay = "-",
                        WorkType = w.WorkType, Title = w.Title, ClassName = w.SchoolClass.Name, 
                        AttendeesCountDisplay = w.SchoolClass.Students.Count.ToString() // <--- ZLICZA PRAWDZIWĄ ILOŚĆ
                    });
                }
            }
            return events.OrderBy(e => e.SortDate).ToList();
        }

        

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
            _dbContext.Students.Add(student); _dbContext.SaveChanges(); return student;
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

                    // NOWOŚĆ: To samo filtrowanie dla ogólnego przeliczania klasy
                    var baseTasks = work.Tasks;
                    var retakeTasks = work.Tasks;

                    if (work.HasGroups && workRecord != null)
                    {
                        if (!string.IsNullOrEmpty(workRecord.Group))
                            baseTasks = work.Tasks.Where(t => t.GroupName == workRecord.Group).ToList();
                        
                        if (!string.IsNullOrEmpty(workRecord.RetakeGroup))
                            retakeTasks = work.Tasks.Where(t => t.GroupName == workRecord.RetakeGroup).ToList();
                        else if (!string.IsNullOrEmpty(workRecord.Group))
                            retakeTasks = work.Tasks.Where(t => t.GroupName == workRecord.Group).ToList();
                    }

                    double baseSum = (hasBaseScores || hasRetakeScores) ? GradeCalculator.CalculateWorkScore(work.MaxFinalPoints, baseTasks, workScores, false) : 0;
                    double retakeSum = (hasBaseScores || hasRetakeScores) ? GradeCalculator.CalculateWorkScore(work.MaxFinalPoints, retakeTasks, workScores, true) : 0;

                    if (workRecord != null && workRecord.IsRetakeActive && hasRetakeScores && retakeSum > baseSum)
                    { totalEarnedP += retakeSum; totalPossibleM += work.MaxFinalPoints; }
                    else if (hasBaseScores)
                    { totalEarnedP += baseSum; totalPossibleM += work.MaxFinalPoints; }
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

        // NOWOŚĆ: Błyskawiczne sprawdzenie, czy w ogóle są jakieś oceny z danej pracy
        public bool HasWorkAnyScores(int workId)
        {
            return _dbContext.StudentTaskScores.Any(s => 
                s.WrittenWorkTask.WrittenWorkId == workId && 
                (s.PointsLevel1 != null || s.PointsLevel2 != null || s.PointsLevel3 != null || 
                 s.RetakePointsLevel1 != null || s.RetakePointsLevel2 != null || s.RetakePointsLevel3 != null));
        }

        public List<PlannedRetake> GetCalendarRetakes(int classId, bool upcomingOnly)
        {
            _dbContext.ChangeTracker.Clear();
            var query = _dbContext.PlannedRetakes.Include(r => r.WrittenWork).Include(r => r.Attendees).ThenInclude(a => a.Student)
                .Where(r => r.WrittenWork.SchoolClassId == classId);
            
            if (upcomingOnly) query = query.Where(r => r.Date >= DateTime.Today);
            return query.ToList();
        }

        public void DeletePlannedRetake(int retakeId)
        {
            var retake = _dbContext.PlannedRetakes.Include(r => r.Attendees).FirstOrDefault(r => r.Id == retakeId);
            if (retake != null) 
            { 
                _dbContext.PlannedRetakeStudents.RemoveRange(retake.Attendees);
                _dbContext.PlannedRetakes.Remove(retake); 
                _dbContext.SaveChanges(); 
            }
        }

        public void RemoveStudentFromRetake(int retakeId, int studentId)
        {
            var attendee = _dbContext.PlannedRetakeStudents.FirstOrDefault(a => a.PlannedRetakeId == retakeId && a.StudentId == studentId);
            if (attendee != null) { _dbContext.PlannedRetakeStudents.Remove(attendee); _dbContext.SaveChanges(); }
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
                if (record != null)
                {
                    if (record.IsAbsent) group = "NB";
                    else group = string.IsNullOrEmpty(record.Group) ? "-" : record.Group;
                }

                details.Add(new RetakeStudentDetailViewModel { StudentId = student.Id, StudentName = student.FullName, OriginalGroup = group });
            }
            return details.OrderBy(d => d.StudentName).ToList();
        }

        public WrittenWork AddWork(WrittenWork work)
        {
            _dbContext.WrittenWorks.Add(work); _dbContext.SaveChanges(); return work;
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
                string oldGrp = string.IsNullOrEmpty(oldTask.GroupName) ? "A" : oldTask.GroupName;
                
                var match = generatedTasks.FirstOrDefault(t => 
                    (string.IsNullOrEmpty(t.GroupName) ? "A" : t.GroupName) == oldGrp && 
                    t.TaskNumber == oldTask.TaskNumber);

                if (match != null)
                {
                    oldTask.MaxPointsLevel1 = match.MaxPointsLevel1; 
                    oldTask.MaxPointsLevel2 = match.MaxPointsLevel2; 
                    oldTask.MaxPointsLevel3 = match.MaxPointsLevel3;
                    oldTask.GroupName = match.GroupName; 
                    generatedTasks.Remove(match); 
                }
                else 
                {
                    _dbContext.WrittenWorkTasks.Remove(oldTask); 
                }
            }
            
            foreach (var newTask in generatedTasks) 
            {
                trackedWork.Tasks.Add(newTask);
            }
            
            trackedWork.Title = newTitle; trackedWork.MaxFinalPoints = maxPoints; trackedWork.WorkType = type; trackedWork.HasGroups = hasGroups; trackedWork.DateWritten = dateWritten; trackedWork.DateEntered = dateEntered;

            var allTaskIds = trackedWork.Tasks.Where(t => t.Id != 0).Select(t => t.Id).ToList();
            if (allTaskIds.Any())
            {
                var allScores = _dbContext.StudentTaskScores.Where(s => allTaskIds.Contains(s.WrittenWorkTaskId)).ToList();
                foreach(var t in trackedWork.Tasks.Where(t => t.Id != 0))
                {
                    var tScores = allScores.Where(s => s.WrittenWorkTaskId == t.Id).ToList();
                    foreach(var s in tScores)
                    {
                        if (t.MaxPointsLevel1 == 0 || t.MaxPointsLevel1 == null) { s.PointsLevel1 = null; s.RetakePointsLevel1 = null; }
                        if (t.MaxPointsLevel2 == 0 || t.MaxPointsLevel2 == null) { s.PointsLevel2 = null; s.RetakePointsLevel2 = null; }
                        if (t.MaxPointsLevel3 == 0 || t.MaxPointsLevel3 == null) { s.PointsLevel3 = null; s.RetakePointsLevel3 = null; }
                    }
                }
            }

            _dbContext.SaveChanges();
        }

        public void DeleteWork(WrittenWork work)
        {
            var tracked = _dbContext.WrittenWorks.Find(work.Id);
            if (tracked != null) { _dbContext.WrittenWorks.Remove(tracked); _dbContext.SaveChanges(); }
        }

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
                
                if (workRecord != null && workRecord.IsAbsent) 
                {
                    scoreText = "NB"; 
                }
                else 
                {
                    // NOWOŚĆ: Filtrowanie zadań po przypisanej grupie ucznia (A, B, itd.)
                    var baseTasks = work.Tasks;
                    var retakeTasks = work.Tasks;

                    if (work.HasGroups && workRecord != null)
                    {
                        if (!string.IsNullOrEmpty(workRecord.Group))
                            baseTasks = work.Tasks.Where(t => t.GroupName == workRecord.Group).ToList();
                        
                        if (!string.IsNullOrEmpty(workRecord.RetakeGroup))
                            retakeTasks = work.Tasks.Where(t => t.GroupName == workRecord.RetakeGroup).ToList();
                        else if (!string.IsNullOrEmpty(workRecord.Group))
                            retakeTasks = work.Tasks.Where(t => t.GroupName == workRecord.Group).ToList();
                    }

                    // Obliczenia używają teraz przefiltrowanej liczby zadań!
                    double baseSum = (hasBase || hasRetake) ? GradeCalculator.CalculateWorkScore(work.MaxFinalPoints, baseTasks, workScores, false) : 0;
                    double retakeSum = (hasBase || hasRetake) ? GradeCalculator.CalculateWorkScore(work.MaxFinalPoints, retakeTasks, workScores, true) : 0;
                    
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
            _dbContext.WrittenWorks.Add(newWork); _dbContext.SaveChanges(); return newWork;
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
        // NOWOŚĆ: Masowe przesuwanie terminów popraw
        public int ShiftRetakeDeadlines(int workId, int classId, int days)
        {
            var work = _dbContext.WrittenWorks.Find(workId);
            if (work == null) return 0;

            var students = _dbContext.Students.Where(s => s.SchoolClassId == classId).ToList();
            var records = _dbContext.StudentWorkRecords.Where(r => r.WrittenWorkId == workId).ToList();
            int updatedCount = 0;

            foreach (var student in students)
            {
                var record = records.FirstOrDefault(r => r.StudentId == student.Id);
                
                // Odczytujemy bazową datę wpisania (indywidualną lub globalną)
                DateTime? baseEntered = record?.CustomDateEntered ?? work.DateEntered;
                if (!baseEntered.HasValue) continue; // Jeśli ocena nie została jeszcze w ogóle wpisana, nie ma z czego liczyć terminu

                // Jeśli uczeń nie ma jeszcze rekordu, tworzymy go, by móc wymusić nową datę
                if (record == null)
                {
                    record = new StudentWorkRecord { StudentId = student.Id, WrittenWorkId = workId };
                    _dbContext.StudentWorkRecords.Add(record);
                }

                // Pobieramy obecny termin poprawy (z bazy) lub wyliczamy domyślne 14 dni
                DateTime currentDeadline = record.RetakeDeadline ?? baseEntered.Value.AddDays(14);
                
                // Nadpisujemy nową datę do bazy danych
                record.RetakeDeadline = currentDeadline.AddDays(days);
                updatedCount++;
            }
            
            _dbContext.SaveChanges();
            return updatedCount;
        }
    }
    
}