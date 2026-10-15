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

        private void PlanGlobalRetake_Click(object sender, RoutedEventArgs e)
        {
            if (new PlanRetakeDialog() { Owner = this }.ShowDialog() == true) RefreshGlobalCalendar();
        }
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