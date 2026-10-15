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

    public class CloudSyncService
    {
        // TUTAJ WKLEJ SWOJE DANE Z SUPABASE
        private readonly string _url = "https://pnrnuwwneqoqfzcamauw.supabase.co";
        private readonly string _key = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InBucm51d3duZXFvcWZ6Y2FtYXV3Iiwicm9sZSI6InNlcnZpY2Vfcm9sZSIsImlhdCI6MTc5MTk3MTk5NSwiZXhwIjoyMTA3NTQ3OTk1fQ.tRhMlOH9jJfAJNODbWnJpW1xFBvp3wPl3vzKXo9DKRc"; 
        private Supabase.Client _client;

        public CloudSyncService()
        {
            var options = new SupabaseOptions { AutoConnectRealtime = false };
            _client = new Supabase.Client(_url, _key, options);
        }

        public async Task<string> SynchronizeWithCloudAsync()
        {
            await _client.InitializeAsync();

            using var db = new AppDbContext();
            
            // NOWOŚĆ: Pobieramy tylko prace dla całych klas i WYKLUCZAMY "aktywność"
            var localWorks = db.WrittenWorks
                .Include(w => w.Tasks)
                .Include(w => w.SchoolClass)
                .Where(w => !w.IsIndividual && w.WorkType.ToLower() != "aktywność")
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
            // Skoro wykluczyliśmy "aktywność" z localWorks, chmura automatycznie usunie je teraz ze swoich serwerów!
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
                    Id = lw.Id, Title = lw.Title, WorkType = lw.WorkType, ClassName = lw.SchoolClass.Name,
                    MaxPoints = lw.MaxFinalPoints, HasGroups = lw.HasGroups, LastSync = DateTime.UtcNow
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

            // Jeśli algorytm nie wykrył żadnej różnicy miedzy bazami, przerywamy transfer
            if (!hasChanges) return "Dane bazy aktualne.";

            // 4. Wypychanie zmian na serwer (Upsert = zaktualizuj lub wstaw nowe)
            if (cloudWorksToUpsert.Any()) await _client.From<CloudWork>().Upsert(cloudWorksToUpsert);
            if (cloudTasksToUpsert.Any()) await _client.From<CloudTask>().Upsert(cloudTasksToUpsert);

            return "Zsynchronizowano pomyślnie!";
        }
    }
}