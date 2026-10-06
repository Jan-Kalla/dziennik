using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using GradebookApp.Services;

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
            _dataService.ApplyMigrations(); // Automatyczna migracja bazy wywoływana z serwisu
            
            ClassesList = new ObservableCollection<SchoolClass>(_dataService.GetAllClasses());
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
            }
            else
            {
                MessageBox.Show("Nazwa klasy nie może być pusta.", "Ajajajaj! Błąd walidacji...", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void AddClassButton_Click(object sender, RoutedEventArgs e) => AddNewClass();

        private void NewClassNameTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter) AddNewClass();
        }
        
        private void AddGlobalWorkButton_Click(object sender, RoutedEventArgs e)
        {
            var classes = _dataService.GetAllClasses();
            if (classes.Count == 0)
            {
                MessageBox.Show("W bazie nie ma żadnych klas. Dodaj najpierw klasę.", "AJajajaj! Brak klas...", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new AddGlobalWorkDialog(classes) { Owner = this };

            if (dialog.ShowDialog() == true && dialog.SelectedClassIds.Any())
            {
                _dataService.AddGlobalWorkToClasses(
                    dialog.SelectedClassIds, dialog.WorkTitle, dialog.WorkType, 
                    dialog.MaxFinalPoints, dialog.DateWritten, dialog.DateEntered, dialog.Tasks);

                MessageBox.Show($"Ocena '{dialog.WorkTitle}' została pomyślnie dodana do {dialog.SelectedClassIds.Count} klas.", 
                                "Sukces", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

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
                    ClassesListBox.Items.Refresh();
                }
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            ClassDetailsView.Visibility = Visibility.Collapsed;
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
            _dataService.Dispose(); // Bezpieczne zwalnianie zasobów bazy przy zamykaniu okna
            base.OnClosed(e);
        }
    }
}