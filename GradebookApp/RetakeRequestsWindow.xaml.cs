using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
    }

    public partial class RetakeRequestsWindow : Window
    {
        public ObservableCollection<RetakeRequestViewModel> Requests { get; set; } = new ObservableCollection<RetakeRequestViewModel>();

        public RetakeRequestsWindow()
        {
            InitializeComponent();
            LoadData();
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

                var viewModels = records.Select(r => new RetakeRequestViewModel
                {
                    RecordId = r.Id,
                    StudentId = r.StudentId,
                    WorkId = r.WrittenWorkId,
                    ClassId = r.Student.SchoolClassId,
                    JournalNumber = r.Student.JournalNumber,
                    StudentName = r.Student.FullName,
                    GroupHeader = $"Klasa {r.Student.SchoolClass.Name} | {r.WrittenWork.WorkType.ToUpper()}: {r.WrittenWork.Title}"
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
                    var existingRetake = db.PlannedRetakes.Include(pr => pr.Attendees).FirstOrDefault(pr => pr.WrittenWorkId == workId);
                    
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
                            if (record != null) record.WantsToRetake = false;
                        }
                        
                        db.SaveChanges();
                        MessageBox.Show($"Termin tej poprawy znajduje się już w kalendarzu globalnym!\nDopisano do niego {added} uczniów z listy.", "Gotowe!", MessageBoxButton.OK, MessageBoxImage.Information);
                        LoadData(); // Odświeża tabelkę zdejmując załatwionych uczniów
                    }
                    else
                    {
                        // 2. TERMINU JESZCZE NIE MA -> Otwieramy okno planowania, przekazując z góry wyselekcjonowane dane!
                        var dialog = new PlanRetakeDialog(classId, null, workId, studentIds) { Owner = this };
                        
                        if (dialog.ShowDialog() == true)
                        {
                            // Po udanym zaplanowaniu nowej daty zdejmujemy flagi WantsToRetake
                            var records = db.StudentWorkRecords.Where(r => r.WrittenWorkId == workId && studentIds.Contains(r.StudentId)).ToList();
                            foreach(var r in records) r.WantsToRetake = false;
                            
                            db.SaveChanges();
                            LoadData();
                        }
                    }
                }
            }
        }
        // =========================================================================

        private void RemoveRequest_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is RetakeRequestViewModel vm)
            {
                using (var db = new AppDbContext())
                {
                    var record = db.StudentWorkRecords.Find(vm.RecordId);
                    if (record != null)
                    {
                        record.WantsToRetake = false;
                        db.SaveChanges();
                    }
                }
                Requests.Remove(vm);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}