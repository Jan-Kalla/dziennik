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
        
        private void AddTemplateButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WorkEditorDialog(isTemplateMode: true) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                MessageBox.Show("Szablon został pomyślnie zapisany.", "Sukces", MessageBoxButton.OK, MessageBoxImage.Information);
            }
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
            bool includeAll = ShowAllGlobalWorksCheckBox.IsChecked == true;
            GlobalCalendarDataGrid.ItemsSource = _dataService.GetGlobalCalendarEvents(System.DateTime.Today, includeAll);
        }

        private void ViewGlobalRetakeDetails_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is CalendarEventViewModel vm)
            {
                if (vm.IsRetake)
                {
                    new RetakeDetailsDialog(vm.WorkId, vm.Title, vm.AttendeeIds) { Owner = this }.ShowDialog();
                }
                else
                {
                    MessageBox.Show("To jest pierwszy termin dla całej klasy. Piszą go domyślnie wszyscy obecni uczniowie.", "Informacja", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void PlanGlobalRetake_Click(object sender, RoutedEventArgs e)
        {
            // Otwiera dialog bez przypisania do konkretnej klasy (można wybrać klasę z ComboBoxa)
            var dialog = new PlanRetakeDialog() { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                RefreshGlobalCalendar();
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