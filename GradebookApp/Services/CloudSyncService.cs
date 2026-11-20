using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Supabase;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace GradebookApp.Services
{
    // Modele mapujące nasze dane na tabele w chmurze Supabase
    [Table("cloud_works")]
    public class CloudWork : BaseModel
    {
        [Supabase.Postgrest.Attributes.PrimaryKey("id", false)] public long Id { get; set; }
        [Column("title")] public string Title { get; set; } = string.Empty;
        [Column("work_type")] public string WorkType { get; set; } = string.Empty;
        [Column("class_name")] public string ClassName { get; set; } = string.Empty;
        [Column("max_points")] public double MaxPoints { get; set; }
        [Column("has_groups")] public bool HasGroups { get; set; }
        [Column("last_sync")] public DateTime LastSync { get; set; }
    }

    [Table("cloud_tasks")]
    public class CloudTask : BaseModel
    {
        [Supabase.Postgrest.Attributes.PrimaryKey("id", false)] public long Id { get; set; }
        [Column("work_id")] public long WorkId { get; set; }
        [Column("task_number")] public int TaskNumber { get; set; }
        [Column("group_name")] public string GroupName { get; set; } = string.Empty;
        [Column("max_l1")] public double MaxL1 { get; set; }
        [Column("max_l2")] public double MaxL2 { get; set; }
        [Column("max_l3")] public double MaxL3 { get; set; }
    }

    [Table("retake_signups")]
    public class CloudRetakeSignup : BaseModel
    {
        [Supabase.Postgrest.Attributes.PrimaryKey("id", false)] public long Id { get; set; }
        [Column("work_id")] public long WorkId { get; set; }
        [Column("class_name")] public string ClassName { get; set; } = string.Empty;
        [Column("journal_number")] public int JournalNumber { get; set; }
    }

    [Table("student_activations")]
    public class CloudStudentActivation : BaseModel
    {
        [Supabase.Postgrest.Attributes.PrimaryKey("id", false)] public long Id { get; set; }
        [Column("class_name")] public string ClassName { get; set; } = string.Empty;
        [Column("journal_number")] public int JournalNumber { get; set; }
        [Column("student_name")] public string StudentName { get; set; } = string.Empty;
        [Column("activation_code")] public string ActivationCode { get; set; } = string.Empty;
        [Column("device_id")] public string? DeviceId { get; set; }
        [Column("device_hash")] public string? DeviceHash { get; set; }
        [Column("status")] public string Status { get; set; } = "active"; // "active", "used", "revoked"
        [Column("activated_at")] public DateTime? ActivatedAt { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    [Table("cloud_teacher_slots")]
    public class CloudTeacherSlot : BaseModel
    {
        [Supabase.Postgrest.Attributes.PrimaryKey("id", false)] public long Id { get; set; }
        [Column("day_of_week")] public int DayOfWeek { get; set; } // 1=Mon, 2=Tue, 3=Wed, 4=Thu, 5=Fri
        [Column("day_name")] public string DayName { get; set; } = string.Empty;
        [Column("time_slot")] public string TimeSlot { get; set; } = string.Empty;
        [Column("class_name")] public string ClassName { get; set; } = string.Empty;
        [Column("room")] public string Room { get; set; } = "Sala 214";
        [Column("is_available")] public bool IsAvailable { get; set; } = true;
    }

    public class CloudSyncService
    {
        private readonly string _url;
        private readonly string _key;
        private readonly Supabase.Client? _client;

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_url) && !string.IsNullOrWhiteSpace(_key) && _client != null;

        public CloudSyncService()
        {
            _url = AppConfig.Settings.Supabase.Url;
            _key = AppConfig.Settings.Supabase.Key;

            if (!string.IsNullOrWhiteSpace(_url) && !string.IsNullOrWhiteSpace(_key))
            {
                var options = new SupabaseOptions { AutoConnectRealtime = false };
                _client = new Supabase.Client(_url, _key, options);
            }
        }

        private void EnsureConfigured()
        {
            if (_client == null || string.IsNullOrWhiteSpace(_url) || string.IsNullOrWhiteSpace(_key))
            {
                throw new InvalidOperationException("Brak konfiguracji połączenia z Supabase.\nUpewnij się, że plik 'appsettings.json' w katalogu aplikacji zawiera poprawne wartości 'Url' oraz 'Key'.");
            }
        }

        public static string GenerateUniqueCode(string className, int journalNumber)
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var cleanClass = new string(className.Where(char.IsLetterOrDigit).ToArray()).ToUpper();
            if (cleanClass.Length > 4) cleanClass = cleanClass.Substring(0, 4);
            var rnd = new Random();
            var randomSuffix = new string(Enumerable.Range(0, 4).Select(_ => chars[rnd.Next(chars.Length)]).ToArray());
            return $"MAT-{cleanClass}-{journalNumber:D2}-{randomSuffix}";
        }

        public async Task<string> SynchronizeWithCloudAsync()
        {
            EnsureConfigured();
            await _client!.InitializeAsync();

            using var db = new AppDbContext();
            
            // Pobieramy tylko prace dla całych klas i WYKLUCZAMY "aktywność"
            var localWorks = db.WrittenWorks
                .Include(w => w.Tasks)
                .Include(w => w.SchoolClass)
                .Where(w => !w.IsIndividual && (w.WorkType == null || w.WorkType.ToLower() != "aktywność"))
                .ToList();

            // Pobieramy aktualny stan chmury
            var remoteWorksResponse = await _client.From<CloudWork>().Get();
            var remoteTasksResponse = await _client.From<CloudTask>().Get();

            var remoteWorks = remoteWorksResponse.Models;
            var remoteTasks = remoteTasksResponse.Models;

            var cloudWorksToUpsert = new List<CloudWork>();
            var cloudTasksToUpsert = new List<CloudTask>();
            bool hasChanges = false;

            // 1. Wykrywanie prac do USUNIĘCIA z chmury
            var localWorkIds = localWorks.Select(w => (long)w.Id).ToList();
            var worksToDelete = remoteWorks.Where(rw => !localWorkIds.Contains(rw.Id)).ToList();
            if (worksToDelete.Any())
            {
                hasChanges = true;
                foreach (var w in worksToDelete) await _client.From<CloudWork>().Delete(w);
            }

            // 2. Mapowanie i wykrywanie ZMIAN w istniejących/nowych pracach
            foreach (var lw in localWorks)
            {
                var cw = new CloudWork
                {
                    Id = lw.Id, 
                    Title = lw.Title ?? string.Empty, 
                    WorkType = lw.WorkType ?? string.Empty, 
                    ClassName = lw.SchoolClass != null ? lw.SchoolClass.Name : string.Empty,
                    MaxPoints = lw.MaxFinalPoints, 
                    HasGroups = lw.HasGroups, 
                    LastSync = DateTime.UtcNow
                };
                cloudWorksToUpsert.Add(cw);

                var rw = remoteWorks.FirstOrDefault(x => x.Id == cw.Id);
                if (rw == null || rw.Title != cw.Title || rw.WorkType != cw.WorkType || rw.MaxPoints != cw.MaxPoints || rw.HasGroups != cw.HasGroups)
                    hasChanges = true;

                foreach (var lt in lw.Tasks)
                {
                    var ct = new CloudTask
                    {
                        Id = lt.Id, WorkId = lw.Id, TaskNumber = lt.TaskNumber, GroupName = string.IsNullOrEmpty(lt.GroupName) ? "A" : lt.GroupName,
                        MaxL1 = lt.MaxPointsLevel1 ?? 0, MaxL2 = lt.MaxPointsLevel2 ?? 0, MaxL3 = lt.MaxPointsLevel3 ?? 0
                    };
                    cloudTasksToUpsert.Add(ct);

                    var rt = remoteTasks.FirstOrDefault(x => x.Id == ct.Id);
                    if (rt == null || rt.TaskNumber != ct.TaskNumber || rt.GroupName != ct.GroupName || rt.MaxL1 != ct.MaxL1 || rt.MaxL2 != ct.MaxL2 || rt.MaxL3 != ct.MaxL3)
                        hasChanges = true;
                }
            }

            // 3. Wykrywanie zadań do USUNIĘCIA (zmniejszono liczbę zadań w pracy pisemnej)
            var localTaskIds = cloudTasksToUpsert.Select(t => t.Id).ToList();
            var tasksToDelete = remoteTasks.Where(rt => !localTaskIds.Contains(rt.Id) && localWorkIds.Contains(rt.WorkId)).ToList();
            if (tasksToDelete.Any())
            {
                hasChanges = true;
                foreach (var t in tasksToDelete) await _client.From<CloudTask>().Delete(t);
            }

            // 4. Wypychanie zmian prac na serwer
            if (cloudWorksToUpsert.Any()) await _client.From<CloudWork>().Upsert(cloudWorksToUpsert);
            if (cloudTasksToUpsert.Any()) await _client.From<CloudTask>().Upsert(cloudTasksToUpsert);

            // 5. Synchronizacja planu lekcji i godzin dostępności z zakładki Lekcja
            try
            {
                await SyncTeacherScheduleToCloudAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CloudSync] Ostrzeżenie przy syncu planu lekcji: {ex.Message}");
            }

            return hasChanges 
                ? "Wykryto zmiany i zsynchronizowano pomyślnie z chmurą (prace oraz plan tygodniowy)!" 
                : "Chmura jest już aktualna. Plan tygodniowy został odświeżony w chmurze!";
        }

        // =========================================================================
        // SYNCHRONIZACJA PLANU TYGODNIOWEGO Z ZAKŁADKI LEKCJA
        // =========================================================================
        public async Task<int> SyncTeacherScheduleToCloudAsync()
        {
            EnsureConfigured();
            await _client!.InitializeAsync();

            using var db = new AppDbContext();
            var templates = db.LessonTemplateItems
                .Where(t => !string.IsNullOrWhiteSpace(t.TimeSlot))
                .ToList();

            var distinctSlots = new List<CloudTeacherSlot>();
            long slotIdCounter = 1;

            var dayNameMap = new Dictionary<DayOfWeek, (int num, string name)>
            {
                { DayOfWeek.Monday, (1, "Poniedziałek") },
                { DayOfWeek.Tuesday, (2, "Wtorek") },
                { DayOfWeek.Wednesday, (3, "Środa") },
                { DayOfWeek.Thursday, (4, "Czwartek") },
                { DayOfWeek.Friday, (5, "Piątek") }
            };

            foreach (var t in templates)
            {
                string cleanSlot = t.TimeSlot.Trim();
                if (string.IsNullOrWhiteSpace(cleanSlot)) continue;

                if (dayNameMap.TryGetValue(t.DayOfWeek, out var dayInfo))
                {
                    distinctSlots.Add(new CloudTeacherSlot
                    {
                        Id = slotIdCounter++,
                        DayOfWeek = dayInfo.num,
                        DayName = dayInfo.name,
                        TimeSlot = cleanSlot,
                        ClassName = t.ClassName ?? string.Empty,
                        Room = "Sala 214",
                        IsAvailable = true
                    });
                }
            }

            if (distinctSlots.Any())
            {
                // Czyścimy stare sloty lub robimy Upsert
                try
                {
                    await _client.From<CloudTeacherSlot>().Upsert(distinctSlots);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CloudSync] Upsert cloud_teacher_slots error: {ex.Message}");
                }
            }

            return distinctSlots.Count;
        }

        // =========================================================================
        // KODY AKTYWACYJNE UCZNIÓW (DLA MATAPP)
        // =========================================================================
        public async Task<List<CloudStudentActivation>> GetStudentActivationsAsync(string className)
        {
            EnsureConfigured();
            await _client!.InitializeAsync();

            try
            {
                var response = await _client.From<CloudStudentActivation>()
                    .Where(a => a.ClassName == className)
                    .Get();

                return response.Models.OrderBy(a => a.JournalNumber).ToList();
            }
            catch
            {
                return new List<CloudStudentActivation>();
            }
        }

        public async Task<List<CloudStudentActivation>> GenerateActivationCodesForClassAsync(int classId, string className)
        {
            EnsureConfigured();
            await _client!.InitializeAsync();

            using var db = new AppDbContext();
            var students = db.Students
                .Where(s => s.SchoolClassId == classId)
                .OrderBy(s => s.JournalNumber)
                .ToList();

            var existingCodes = await GetStudentActivationsAsync(className);
            var resultList = new List<CloudStudentActivation>();

            foreach (var student in students)
            {
                var existing = existingCodes.FirstOrDefault(c => c.JournalNumber == student.JournalNumber);
                if (existing != null && !string.IsNullOrWhiteSpace(existing.ActivationCode))
                {
                    resultList.Add(existing);
                }
                else
                {
                    var newRecord = new CloudStudentActivation
                    {
                        ClassName = className,
                        JournalNumber = student.JournalNumber,
                        StudentName = student.FullName,
                        ActivationCode = GenerateUniqueCode(className, student.JournalNumber),
                        Status = "active",
                        CreatedAt = DateTime.UtcNow
                    };
                    resultList.Add(newRecord);
                }
            }

            if (resultList.Any())
            {
                await _client.From<CloudStudentActivation>().Upsert(resultList);
            }

            return resultList;
        }

        public async Task<CloudStudentActivation> RegenerateActivationCodeForStudentAsync(string className, int journalNumber, string studentName)
        {
            EnsureConfigured();
            await _client!.InitializeAsync();

            var newRecord = new CloudStudentActivation
            {
                ClassName = className,
                JournalNumber = journalNumber,
                StudentName = studentName,
                ActivationCode = GenerateUniqueCode(className, journalNumber),
                DeviceId = null,
                DeviceHash = null,
                ActivatedAt = null,
                Status = "active", // Nowy kod unieważnia poprzednie powiązanie z telefonem!
                CreatedAt = DateTime.UtcNow
            };

            await _client.From<CloudStudentActivation>().Upsert(newRecord);
            return newRecord;
        }

        public async Task<(int addedCount, string message)> FetchAndApplyRetakeSignupsAsync()
        {
            EnsureConfigured();
            await _client!.InitializeAsync();

            var response = await _client.From<CloudRetakeSignup>().Get();
            var signups = response.Models;

            if (signups == null || !signups.Any())
            {
                return (0, "Skrzynka podawcza w chmurze jest pusta. Żaden uczeń nie zapisał się na poprawę od czasu ostatniej synchronizacji.");
            }

            int addedCount = 0;

            using (var db = new AppDbContext())
            {
                var groupedSignups = signups.GroupBy(s => new { s.WorkId, s.ClassName });

                foreach (var group in groupedSignups)
                {
                    var schoolClass = db.Classes.FirstOrDefault(c => c.Name == group.Key.ClassName);
                    if (schoolClass == null) continue;

                    var students = db.Students.Where(s => s.SchoolClassId == schoolClass.Id).ToList();
                    var work = db.WrittenWorks.FirstOrDefault(w => w.Id == group.Key.WorkId);
                    if (work == null) continue;

                    foreach (var signup in group)
                    {
                        var student = students.FirstOrDefault(s => s.JournalNumber == signup.JournalNumber);
                        if (student != null)
                        {
                            var record = db.StudentWorkRecords.FirstOrDefault(r => r.StudentId == student.Id && r.WrittenWorkId == work.Id);
                            if (record == null)
                            {
                                record = new StudentWorkRecord { StudentId = student.Id, WrittenWorkId = work.Id, WantsToRetake = true };
                                db.StudentWorkRecords.Add(record);
                            }
                            else
                            {
                                record.WantsToRetake = true;
                            }
                            addedCount++;
                        }
                    }
                }
                db.SaveChanges();
            }

            // Usuwamy przetworzone zgłoszenia z chmury
            foreach (var signup in signups)
            {
                await _client.From<CloudRetakeSignup>().Delete(signup);
            }

            return (addedCount, $"Pomyślnie pobrano zgłoszenia. Zaktualizowano listę chętnych o {addedCount} uczniów.");
        }
    }
}
