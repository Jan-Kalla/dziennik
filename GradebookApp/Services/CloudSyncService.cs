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
        [Column("slot_date")] public string? SlotDate { get; set; }
        [Column("slot_time")] public string? SlotTime { get; set; }
        [Column("status")] public string Status { get; set; } = "pending";
        [Column("student_note")] public string? StudentNote { get; set; }
        [Column("created_at")] public DateTime? CreatedAt { get; set; }
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
        [Column("room")] public string Room { get; set; } = string.Empty;
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
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long slotIdCounter = 1;
            string configuredRoom = AppConfig.Settings.DefaultRoom?.Trim() ?? string.Empty;

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
                    // Uznajemy godzinę za dostępną dla ucznia na poprawę TYLKO wtedy, gdy oznaczono ją jako 'Poprawy' lub 'Konsultacje'
                    bool isRetakeSlot = !string.IsNullOrWhiteSpace(t.ClassName) &&
                        (t.ClassName.Trim().Equals("Poprawy", StringComparison.OrdinalIgnoreCase) ||
                         t.ClassName.Trim().Equals("Poprawa", StringComparison.OrdinalIgnoreCase) ||
                         t.ClassName.IndexOf("popraw", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         t.ClassName.IndexOf("konsultacj", StringComparison.OrdinalIgnoreCase) >= 0);

                    if (isRetakeSlot)
                    {
                        string key = $"{dayInfo.num}_{cleanSlot}";
                        if (seenKeys.Add(key))
                        {
                            distinctSlots.Add(new CloudTeacherSlot
                            {
                                Id = slotIdCounter++,
                                DayOfWeek = dayInfo.num,
                                DayName = dayInfo.name,
                                TimeSlot = cleanSlot,
                                ClassName = t.ClassName ?? "Poprawy",
                                Room = configuredRoom,
                                IsAvailable = true
                            });
                        }
                    }
                }
            }

            // Czyścimy poprzednie sloty w chmurze i wstawiamy wyłącznie godziny zdefiniowane na poprawy
            try
            {
                var existing = await _client.From<CloudTeacherSlot>().Get();
                if (existing.Models != null && existing.Models.Any())
                {
                    foreach (var oldSlot in existing.Models)
                    {
                        await _client.From<CloudTeacherSlot>().Delete(oldSlot);
                    }
                }

                if (distinctSlots.Any())
                {
                    await _client.From<CloudTeacherSlot>().Insert(distinctSlots);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CloudSync] Sync cloud_teacher_slots error: {ex.Message}");
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

            // Zabezpieczenie przed ewentualnymi zduplikowanymi numerami w dzienniku
            var distinctStudents = students
                .GroupBy(s => s.JournalNumber)
                .Select(g => g.First())
                .ToList();

            var existingCodes = await GetStudentActivationsAsync(className);
            var resultList = new List<CloudStudentActivation>();

            foreach (var student in distinctStudents)
            {
                var existing = existingCodes.FirstOrDefault(c => c.JournalNumber == student.JournalNumber);
                if (existing != null && !string.IsNullOrWhiteSpace(existing.ActivationCode))
                {
                    // Uczeń ma już wygenerowany kod w chmurze - zachowujemy bez zmian
                    resultList.Add(existing);
                }
                else if (existing != null)
                {
                    // Uczeń istnieje w chmurze, ale nie ma jeszcze kodu - aktualizujemy jego rekord o znanym Id
                    existing.ActivationCode = GenerateUniqueCode(className, student.JournalNumber);
                    existing.StudentName = student.FullName;
                    existing.Status = "active";
                    try
                    {
                        await _client.From<CloudStudentActivation>().Upsert(existing);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CloudSync] Upsert existing activation error: {ex.Message}");
                    }
                    resultList.Add(existing);
                }
                else
                {
                    // Nowy uczeń bez rekordu w chmurze - wstawiamy przez Insert, aby baza nadała Id z sekwencji BIGSERIAL
                    // Zapobiega to błędowi PostgreSQL 21000 (ON CONFLICT DO UPDATE duplicate constrained values z id: 0)
                    var newRecord = new CloudStudentActivation
                    {
                        ClassName = className,
                        JournalNumber = student.JournalNumber,
                        StudentName = student.FullName,
                        ActivationCode = GenerateUniqueCode(className, student.JournalNumber),
                        Status = "active",
                        CreatedAt = DateTime.UtcNow
                    };

                    try
                    {
                        var inserted = await _client.From<CloudStudentActivation>().Insert(newRecord);
                        if (inserted.Models != null && inserted.Models.Any())
                        {
                            resultList.Add(inserted.Models.First());
                        }
                        else
                        {
                            resultList.Add(newRecord);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CloudSync] Insert new activation error #{student.JournalNumber}: {ex.Message}");
                    }
                }
            }

            return resultList;
        }

        public async Task<CloudStudentActivation> RegenerateActivationCodeForStudentAsync(string className, int journalNumber, string studentName)
        {
            EnsureConfigured();
            await _client!.InitializeAsync();

            var existingCodes = await GetStudentActivationsAsync(className);
            var existing = existingCodes.FirstOrDefault(c => c.JournalNumber == journalNumber);

            if (existing != null)
            {
                existing.ActivationCode = GenerateUniqueCode(className, journalNumber);
                existing.StudentName = studentName;
                existing.DeviceId = null;
                existing.DeviceHash = null;
                existing.ActivatedAt = null;
                existing.Status = "active"; // Nowy kod unieważnia poprzednie powiązanie z telefonem!
                await _client.From<CloudStudentActivation>().Upsert(existing);
                return existing;
            }
            else
            {
                var newRecord = new CloudStudentActivation
                {
                    ClassName = className,
                    JournalNumber = journalNumber,
                    StudentName = studentName,
                    ActivationCode = GenerateUniqueCode(className, journalNumber),
                    DeviceId = null,
                    DeviceHash = null,
                    ActivatedAt = null,
                    Status = "active",
                    CreatedAt = DateTime.UtcNow
                };

                var inserted = await _client.From<CloudStudentActivation>().Insert(newRecord);
                return inserted.Models?.FirstOrDefault() ?? newRecord;
            }
        }

        public async Task<(int addedCount, string message)> FetchAndApplyRetakeSignupsAsync()
        {
            EnsureConfigured();
            await _client!.InitializeAsync();

            var response = await _client.From<CloudRetakeSignup>()
                .Where(s => s.Status == "pending")
                .Get();
            var signups = response.Models;

            if (signups == null || !signups.Any())
            {
                return (0, "Skrzynka podawcza w chmurze jest pusta. Żaden uczeń nie oczekuje obecnie na potwierdzenie poprawy.");
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
                                record = new StudentWorkRecord
                                {
                                    StudentId = student.Id,
                                    WrittenWorkId = work.Id,
                                    WantsToRetake = true,
                                    RequestedSlotDate = signup.SlotDate,
                                    RequestedSlotTime = signup.SlotTime,
                                    RequestedStudentNote = signup.StudentNote
                                };
                                db.StudentWorkRecords.Add(record);
                            }
                            else
                            {
                                record.WantsToRetake = true;
                                record.RequestedSlotDate = signup.SlotDate;
                                record.RequestedSlotTime = signup.SlotTime;
                                record.RequestedStudentNote = signup.StudentNote;
                            }
                            addedCount++;
                        }
                    }
                }
                db.SaveChanges();
            }

            // Nie usuwamy zgłoszeń! Pozostają w chmurze, a po zatwierdzeniu przez nauczyciela
            // ich status zostanie zmieniony na 'confirmed', co wywoła powiadomienie u ucznia.
            return (addedCount, $"Pomyślnie pobrano zgłoszenia. Zaktualizowano listę chętnych o {addedCount} uczniów.");
        }

        // =========================================================================
        // POTWIERDZENIE POPRAWY W CHMURZE (WYZWALA POWIADOMIENIE DLA UCZNIA)
        // =========================================================================
        public async Task ConfirmRetakeForStudentsAsync(long workId, string className, List<int> journalNumbers, string slotDate, string slotTime)
        {
            EnsureConfigured();
            await _client!.InitializeAsync();

            try
            {
                var response = await _client.From<CloudRetakeSignup>()
                    .Where(s => s.WorkId == workId && s.ClassName == className)
                    .Get();

                var existingSignups = response.Models ?? new List<CloudRetakeSignup>();
                var toUpsert = new List<CloudRetakeSignup>();

                foreach (var jNum in journalNumbers)
                {
                    var existing = existingSignups.FirstOrDefault(s => s.JournalNumber == jNum);
                    if (existing != null)
                    {
                        existing.Status = "confirmed";
                        existing.SlotDate = slotDate;
                        existing.SlotTime = slotTime;
                        toUpsert.Add(existing);
                    }
                    else
                    {
                        toUpsert.Add(new CloudRetakeSignup
                        {
                            WorkId = workId,
                            ClassName = className,
                            JournalNumber = jNum,
                            SlotDate = slotDate,
                            SlotTime = slotTime,
                            Status = "confirmed",
                            StudentNote = "Zatwierdzono przez nauczyciela"
                        });
                    }
                }

                foreach (var item in toUpsert)
                {
                    try
                    {
                        if (item.Id > 0)
                        {
                            await _client.From<CloudRetakeSignup>().Update(item);
                        }
                        else
                        {
                            await _client.From<CloudRetakeSignup>().Insert(item);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CloudSync] Error saving signup #{item.JournalNumber}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CloudSync] Błąd zatwierdzania zgłoszenia w chmurze: {ex.Message}");
            }
        }
    }
}
