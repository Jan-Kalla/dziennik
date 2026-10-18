using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls; 
using GradebookApp.Services;
using GradebookApp.ViewModels;

namespace GradebookApp
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<SchoolClass> ClassesList { get; set; }
        private readonly GradebookDataService _dataService;

        public MainWindow()
        {
            InitializeComponent();

            _dataService = new GradebookDataService();
            _dataService.ApplyMigrations(); 
            
            ClassesList = new ObservableCollection<SchoolClass>(_dataService.GetAllClasses().OrderBy(c => c.Name));
            ClassesListBox.ItemsSource = ClassesList;
            
            SharedClassControl.StudentDetailsRequested += SharedClassControl_StudentDetailsRequested;
        }

        private void AddNewClass()
        {
            string newName = NewClassNameTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(newName))
            {
                var newClass = _dataService.AddClass(newName);
                ClassesList.Add(newClass);
                NewClassNameTextBox.Clear();
                ClassesList = new ObservableCollection<SchoolClass>(ClassesList.OrderBy(c => c.Name));
                ClassesListBox.ItemsSource = ClassesList;
            }
            else MessageBox.Show("Nazwa klasy nie może być pusta.", "Ajajajaj! Błąd walidacji...", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void AddClassButton_Click(object sender, RoutedEventArgs e) => AddNewClass();

        private void NewClassNameTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter) AddNewClass();
        }
        
        private void OpenTemplateManager_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TemplateManagerWindow() { Owner = this };
            dialog.ShowDialog();
        }

        // --- SEKCJA KALENDARZA GLOBALNEGO ---
        private void GlobalCalendarButton_Click(object sender, RoutedEventArgs e)
        {
            ClassesListView.Visibility = Visibility.Collapsed;
            GlobalCalendarView.Visibility = Visibility.Visible;
            RefreshGlobalCalendar();
        }

        private void RefreshGlobalCalendar_Click(object sender, RoutedEventArgs e) => RefreshGlobalCalendar();

        private void RefreshGlobalCalendar()
        {
            if (_dataService == null) return; 

            bool upcomingOnly = ShowUpcomingGlobalOnlyCheckBox.IsChecked == true;
            bool includeAll = ShowAllGlobalWorksCheckBox.IsChecked == true;
            GlobalCalendarDataGrid.ItemsSource = _dataService.GetGlobalCalendarEvents(upcomingOnly, includeAll);
        }

        private void ViewGlobalRetakeDetails_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is CalendarEventViewModel vm)
            {
                if (vm.IsRetake) { 
                    new RetakeDetailsDialog(vm.EventId, vm.WorkId, vm.Title, vm.AttendeeIds) { Owner = this }.ShowDialog(); 
                    RefreshGlobalCalendar(); 
                }
                else MessageBox.Show("To jest pierwszy termin. Piszą go wszyscy.", "Informacja", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void EditGlobalRetake_Click(object sender, RoutedEventArgs e)
        {
            CalendarEventViewModel? vm = (sender as MenuItem)?.Parent is ContextMenu cm && cm.PlacementTarget is Button btn ? btn.DataContext as CalendarEventViewModel : GlobalCalendarDataGrid.SelectedItem as CalendarEventViewModel;
            if (vm != null)
            {
                if (!vm.IsRetake) { MessageBox.Show("Możesz edytować tylko poprawy.", "Zablokowane", MessageBoxButton.OK, MessageBoxImage.Information); return; }
                if (new PlanRetakeDialog(null, vm.EventId) { Owner = this }.ShowDialog() == true) RefreshGlobalCalendar();
            }
        }

        private void DeleteGlobalRetake_Click(object sender, RoutedEventArgs e)
        {
            CalendarEventViewModel? vm = (sender as MenuItem)?.Parent is ContextMenu cm && cm.PlacementTarget is Button btn ? btn.DataContext as CalendarEventViewModel : GlobalCalendarDataGrid.SelectedItem as CalendarEventViewModel;
            if (vm != null)
            {
                if (!vm.IsRetake) { MessageBox.Show("Tylko poprawy mogą być stąd usuwane.", "Zablokowane", MessageBoxButton.OK, MessageBoxImage.Information); return; }
                if (MessageBox.Show($"Usunąć poprawę '{vm.Title}'?", "Potwierdzenie", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    _dataService.DeletePlannedRetake(vm.EventId); RefreshGlobalCalendar();
                }
            }
        }

        // ... (poprzedni kod)
        private void PlanGlobalRetake_Click(object sender, RoutedEventArgs e)
        {
            if (new PlanRetakeDialog() { Owner = this }.ShowDialog() == true) RefreshGlobalCalendar();
        }
        private void ViewRetakeRequests_Click(object sender, RoutedEventArgs e)
        {
            var window = new RetakeRequestsWindow() { Owner = this };
            window.ShowDialog();
        }

        // =========================================================================
        // NOWOŚĆ: POBIERANIE ZAPISÓW UCZNIÓW BEZPOŚREDNIO Z KALKULATORA WEBOWEGO
        // =========================================================================
        private async void SyncRetakes_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null) { btn.IsEnabled = false; btn.Content = "Pobieranie..."; }

            try
            {
                // TODO: Skopiuj swoje prawdziwe klucze z pliku CloudSyncService.cs
                string supabaseUrl = "https://pnrnuwwneqoqfzcamauw.supabase.co";
                string supabaseKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InBucm51d3duZXFvcWZ6Y2FtYXV3Iiwicm9sZSI6InNlcnZpY2Vfcm9sZSIsImlhdCI6MTc5MTk3MTk5NSwiZXhwIjoyMTA3NTQ3OTk1fQ.tRhMlOH9jJfAJNODbWnJpW1xFBvp3wPl3vzKXo9DKRc";

                using (var client = new System.Net.Http.HttpClient())
                {
                    client.DefaultRequestHeaders.Add("apikey", supabaseKey);
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {supabaseKey}");
                    
                    // 1. Pobieranie danych z Supabase (z nowej tabeli retake_signups)
                    var response = await client.GetAsync($"{supabaseUrl}/rest/v1/retake_signups?select=*");
                    response.EnsureSuccessStatusCode();
                    
                    var jsonString = await response.Content.ReadAsStringAsync();
                    var signups = System.Text.Json.JsonSerializer.Deserialize<List<RetakeSignupDto>>(
                        jsonString, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (signups == null || !signups.Any())
                    {
                        MessageBox.Show("Skrzynka podawcza w chmurze jest pusta. Żaden uczeń nie zapisał się na poprawę od czasu ostatniej synchronizacji.", "Aktualizacja", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    int addedCount = 0;

                    // 2. Mieszanie chmury z lokalną bazą danych
                    using (var db = new AppDbContext())
                    {
                        // Grupujemy zgłoszenia po pracy pisemnej i klasie dla wydajności
                        var groupedSignups = signups.GroupBy(s => new { s.work_id, s.class_name });

                        foreach (var group in groupedSignups)
                        {
                            var schoolClass = db.Classes.FirstOrDefault(c => c.Name == group.Key.class_name);
                            if (schoolClass == null) continue;

                            var students = db.Students.Where(s => s.SchoolClassId == schoolClass.Id).ToList();
                            var work = db.WrittenWorks.FirstOrDefault(w => w.Id == group.Key.work_id);
                            if (work == null) continue;

                            foreach (var signup in group)
                            {
                                // Namierzamy ucznia po jego numerku
                                var student = students.FirstOrDefault(s => s.JournalNumber == signup.journal_number);
                                if (student != null)
                                {
                                    // Zapisujemy chęć poprawy w Twojej lokalnej tabeli StudentWorkRecords
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
                        db.SaveChanges(); // Zapis w lokalnym SQLite
                    }

                    // 3. Czyszczenie skrzynki podawczej w chmurze (żeby nie pobierać tych samych zgłoszeń podwójnie)
                    string idsToDelete = string.Join(",", signups.Select(s => s.id));
                    await client.DeleteAsync($"{supabaseUrl}/rest/v1/retake_signups?id=in.({idsToDelete})");

                    MessageBox.Show($"Pomyślnie pobrano zgłoszenia. Zaktualizowano listę chętnych o {addedCount} uczniów.", "Sukces", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Wystąpił błąd podczas pobierania zgłoszeń: {ex.Message}", "Błąd sieci", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (btn != null) { btn.IsEnabled = true; btn.Content = "Pobierz zapisy na poprawy (z chmury)"; }
            }
        }
        
        // Klasa pomocnicza używana przez system JSON do odczytu danych z Supabase
        public class RetakeSignupDto
        {
            public long id { get; set; }
            public long work_id { get; set; }
            public string class_name { get; set; } = "";
            public int journal_number { get; set; }
        }
        // =========================================================================
        // ------------------------------------

        // --- SEKCJA SYNCHRONIZACJI Z CHMURĄ ---
        private async void SyncCloud_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null) 
            { 
                btn.IsEnabled = false; 
                btn.Content = "Synchronizacja..."; 
            }
            
            try
            {
                var syncService = new Services.CloudSyncService();
                string result = await syncService.SynchronizeWithCloudAsync();
                
                MessageBox.Show(result, "Status synchronizacji", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Błąd podczas łączenia z chmurą: {ex.Message}", "Błąd sieci", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (btn != null) 
                { 
                    btn.IsEnabled = true; 
                    btn.Content = "☁ Synchronizuj"; 
                }
            }
        }
        // ------------------------------------

        private void ClassItem_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.ListBoxItem item && item.DataContext is SchoolClass selectedClass)
            {
                ClassesListView.Visibility = Visibility.Collapsed;
                ClassDetailsView.Visibility = Visibility.Visible;
                SharedClassControl.LoadClassData(selectedClass);
            }
        }

        private void DeleteClass_Click(object sender, RoutedEventArgs e)
        {
            if (ClassesListBox.SelectedItem is SchoolClass selectedClass)
            {
                var result = MessageBox.Show($"Czy na pewno chcesz usunąć klasę {selectedClass.Name} i wszystkie przypisane do niej dane?", 
                                             "Potwierdzenie usunięcia", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    _dataService.DeleteClass(selectedClass);
                    ClassesList.Remove(selectedClass);
                }
            }
        }

        private void EditClass_Click(object sender, RoutedEventArgs e)
        {
            if (ClassesListBox.SelectedItem is SchoolClass selectedClass)
            {
                var dialog = new EditClassDialog(selectedClass.Name) { Owner = this };

                if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.NewName) && dialog.NewName != selectedClass.Name)
                {
                    _dataService.UpdateClassName(selectedClass, dialog.NewName);
                    ClassesList = new ObservableCollection<SchoolClass>(_dataService.GetAllClasses().OrderBy(c => c.Name));
                    ClassesListBox.ItemsSource = ClassesList;
                }
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            ClassDetailsView.Visibility = Visibility.Collapsed;
            GlobalCalendarView.Visibility = Visibility.Collapsed;
            ClassesListView.Visibility = Visibility.Visible;
        }

        private void OpenClassNewWindow_Click(object sender, RoutedEventArgs e)
        {
            if (ClassesListBox.SelectedItem is SchoolClass selectedClass)
            {
                var newWindow = new ClassDetailsWindow(selectedClass) { Owner = this };
                newWindow.ShowDialog();
            }
        }

        private void SharedClassControl_StudentDetailsRequested(object? sender, Student student)
        {
            ClassDetailsView.Visibility = Visibility.Collapsed;
            StudentDetailsView.Visibility = Visibility.Visible;
            SharedStudentControl.LoadStudentData(student);
        }

        private void BackToClassFromStudent_Click(object sender, RoutedEventArgs e)
        {
            StudentDetailsView.Visibility = Visibility.Collapsed;
            ClassDetailsView.Visibility = Visibility.Visible;
        }

        protected override void OnClosed(System.EventArgs e)
        {
            _dataService.Dispose(); 
            base.OnClosed(e);
        }
    }
}