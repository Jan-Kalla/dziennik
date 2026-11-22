using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;

namespace GradebookApp
{
    public class RetakeRequestViewModel
    {
        public int RecordId { get; set; }
        public int StudentId { get; set; }
        public int WorkId { get; set; }
        public int ClassId { get; set; }
        public int JournalNumber { get; set; }
        public string StudentName { get; set; } = "";
        public string GroupHeader { get; set; } = "";
        public string RequestedDate { get; set; } = "";
        public string RequestedTime { get; set; } = "";
        public string RequestedNote { get; set; } = "";
        public string DisplaySlotInfo { get; set; } = "";
    }

    public partial class RetakeRequestsWindow : Window
    {
        public ObservableCollection<RetakeRequestViewModel> Requests { get; set; } = new ObservableCollection<RetakeRequestViewModel>();

        public RetakeRequestsWindow()
        {
            InitializeComponent();
            LoadData();

            Loaded += async (s, e) =>
            {
                await SyncWithCloudAsync(silent: true);
            };
        }

        private async System.Threading.Tasks.Task SyncWithCloudAsync(bool silent = false)
        {
            try
            {
                var syncService = new Services.CloudSyncService();
                if (syncService.IsConfigured)
                {
                    var (added, msg) = await syncService.FetchAndApplyRetakeSignupsAsync();
                    LoadData();
                    if (!silent && added > 0)
                    {
                        MessageBox.Show($"Pobrano {added} nowych zgłoszeń z chmury!", "Zapisy na poprawy", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else if (!silent)
                    {
                        MessageBox.Show(msg, "Zapisy na poprawy", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (System.Exception ex)
            {
                if (!silent)
                {
                    MessageBox.Show($"Błąd pobierania zgłoszeń z chmury: {ex.Message}", "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void RefreshFromCloud_Click(object sender, RoutedEventArgs e)
        {
            if (Mouse.RightButton == MouseButtonState.Pressed) return;
            var btn = sender as Button;
            if (btn != null) { btn.IsEnabled = false; btn.Content = "Pobieranie..."; }
            await SyncWithCloudAsync(silent: false);
            if (btn != null) { btn.IsEnabled = true; btn.Content = "☁ Pobierz z chmury"; }
        }

        private void LoadData()
        {
            using (var db = new AppDbContext())
            {
                var records = db.StudentWorkRecords
                    .Include(r => r.Student).ThenInclude(s => s.SchoolClass)
                    .Include(r => r.WrittenWork)
                    .Where(r => r.WantsToRetake)
                    .ToList();

                var viewModels = records.Select(r =>
                {
                    string datePart = !string.IsNullOrWhiteSpace(r.RequestedSlotDate) ? r.RequestedSlotDate.Trim() : "";
                    string timePart = !string.IsNullOrWhiteSpace(r.RequestedSlotTime) ? r.RequestedSlotTime.Trim() : "";
                    string slotInfo = "";
                    if (!string.IsNullOrEmpty(datePart) && !string.IsNullOrEmpty(timePart))
                    {
                        slotInfo = $"{datePart} ({timePart})";
                    }
                    else if (!string.IsNullOrEmpty(datePart))
                    {
                        slotInfo = datePart;
                    }
                    else if (!string.IsNullOrEmpty(timePart))
                    {
                        slotInfo = timePart;
                    }
                    else
                    {
                        slotInfo = "Brak preferencji (dowolny termin)";
                    }

                    return new RetakeRequestViewModel
                    {
                        RecordId = r.Id,
                        StudentId = r.StudentId,
                        WorkId = r.WrittenWorkId,
                        ClassId = r.Student.SchoolClassId,
                        JournalNumber = r.Student.JournalNumber,
                        StudentName = r.Student.FullName,
                        GroupHeader = $"Klasa {r.Student.SchoolClass.Name} | {r.WrittenWork.WorkType.ToUpper()}: {r.WrittenWork.Title}",
                        RequestedDate = datePart,
                        RequestedTime = timePart,
                        RequestedNote = r.RequestedStudentNote?.Trim() ?? "",
                        DisplaySlotInfo = slotInfo
                    };
                })
                .OrderBy(v => v.GroupHeader)
                .ThenBy(v => v.JournalNumber)
                .ToList();

                Requests = new ObservableCollection<RetakeRequestViewModel>(viewModels);

                ICollectionView cv = CollectionViewSource.GetDefaultView(Requests);
                cv.GroupDescriptions.Add(new PropertyGroupDescription("GroupHeader"));
                
                RequestsDataGrid.ItemsSource = cv;
            }
        }

        // =========================================================================
        // NOWOŚĆ: MAGIA AUTOMATYCZNEGO PRZYPISYWANIA LUB OTWIERANIA OKNA PLANOWANIA
        // =========================================================================
        private void AssignToRetake_Click(object sender, RoutedEventArgs e)
        {
            if (Mouse.RightButton == MouseButtonState.Pressed) return;

            // Pobieramy całą grupę uczniów na podstawie klikniętego nagłówka
            if ((sender as Button)?.DataContext is CollectionViewGroup group)
            {
                var items = group.Items.Cast<RetakeRequestViewModel>().ToList();
                if (!items.Any()) return;

                // Ponieważ wszyscy w grupie należą do tej samej klasy i piszą tę samą pracę, bierzemy parametry z pierwszego:
                int workId = items.First().WorkId;
                int classId = items.First().ClassId;
                var studentIds = items.Select(i => i.StudentId).ToList();

                using (var db = new AppDbContext())
                {
                    // Szukamy, czy istnieje już zaplanowana poprawa dla tej pracy
                    var existingRetake = db.PlannedRetakes
                        .Include(pr => pr.Attendees)
                        .Include(pr => pr.WrittenWork).ThenInclude(w => w.SchoolClass)
                        .FirstOrDefault(pr => pr.WrittenWorkId == workId);
                    
                    if (existingRetake != null)
                    {
                        // 1. TERMIN JUŻ ISTNIEJE -> Dopisujemy "po cichu" do istniejącej poprawy
                        int added = 0;
                        foreach(var sId in studentIds)
                        {
                            if (!existingRetake.Attendees.Any(a => a.StudentId == sId))
                            {
                                existingRetake.Attendees.Add(new PlannedRetakeStudent { StudentId = sId });
                                added++;
                            }
                            
                            // Wyłączamy powiadomienie chęci poprawy
                            var record = db.StudentWorkRecords.FirstOrDefault(r => r.WrittenWorkId == workId && r.StudentId == sId);
                            if (record != null)
                            {
                                record.WantsToRetake = false;
                                record.RequestedSlotDate = null;
                                record.RequestedSlotTime = null;
                                record.RequestedStudentNote = null;
                            }
                        }
                        
                        db.SaveChanges();

                        // Automatyczna synchronizacja zatwierdzenia do chmury (powiadomienie dla ucznia)
                        try
                        {
                            var syncService = new Services.CloudSyncService();
                            if (syncService.IsConfigured && existingRetake.WrittenWork?.SchoolClass != null)
                            {
                                var jNums = db.Students.Where(s => studentIds.Contains(s.Id)).Select(s => s.JournalNumber).ToList();
                                string confirmedDate = existingRetake.Date.ToString("yyyy-MM-dd");
                                _ = System.Threading.Tasks.Task.Run(() => syncService.ConfirmRetakeForStudentsAsync(
                                    workId,
                                    existingRetake.WrittenWork.SchoolClass.Name,
                                    jNums,
                                    confirmedDate,
                                    existingRetake.Time
                                ));
                            }
                        }
                        catch { }

                        MessageBox.Show($"Termin tej poprawy znajduje się już w kalendarzu globalnym!\nDopisano do niego {added} uczniów z listy.", "Gotowe!", MessageBoxButton.OK, MessageBoxImage.Information);
                        LoadData(); // Odświeża tabelkę zdejmując załatwionych uczniów
                    }
                    else
                    {
                        // Sprawdzamy, czy uczeń wskazał preferowaną datę i godzinę
                        DateTime? preDate = null;
                        string? preTime = null;
                        var firstReq = items.FirstOrDefault(i => !string.IsNullOrEmpty(i.RequestedDate));
                        if (firstReq != null && System.DateTime.TryParse(firstReq.RequestedDate, out var parsedDate))
                        {
                            preDate = parsedDate;
                            preTime = firstReq.RequestedTime;
                        }

                        // 2. TERMINU JESZCZE NIE MA -> Otwieramy okno planowania, przekazując wyselekcjonowane dane wraz z terminem wskazanym przez ucznia!
                        var dialog = new PlanRetakeDialog(classId, null, workId, studentIds, preDate, preTime) { Owner = this };
                        
                        if (dialog.ShowDialog() == true)
                        {
                            // Po udanym zaplanowaniu nowej daty zdejmujemy flagi WantsToRetake
                            var records = db.StudentWorkRecords.Where(r => r.WrittenWorkId == workId && studentIds.Contains(r.StudentId)).ToList();
                            foreach(var r in records)
                            {
                                r.WantsToRetake = false;
                                r.RequestedSlotDate = null;
                                r.RequestedSlotTime = null;
                                r.RequestedStudentNote = null;
                            }
                            
                            db.SaveChanges();
                            LoadData();
                            (Owner as MainWindow)?.MainLessonTabControl?.RefreshDailyView();
                        }
                    }
                }
            }
        }
        // =========================================================================

        private void RemoveRequest_Click(object sender, RoutedEventArgs e)
        {
            if (Mouse.RightButton == MouseButtonState.Pressed) return;

            if ((sender as FrameworkElement)?.DataContext is RetakeRequestViewModel vm)
            {
                using (var db = new AppDbContext())
                {
                    var record = db.StudentWorkRecords.Find(vm.RecordId);
                    if (record != null)
                    {
                        record.WantsToRetake = false;
                        record.RequestedSlotDate = null;
                        record.RequestedSlotTime = null;
                        record.RequestedStudentNote = null;
                        db.SaveChanges();
                    }
                }
                Requests.Remove(vm);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}