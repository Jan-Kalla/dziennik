using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace GradebookApp
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<SchoolClass> ClassesList { get; set; }
        private AppDbContext _dbContext;

        public MainWindow()
        {
            InitializeComponent();

            _dbContext = new AppDbContext();
            _dbContext.Database.EnsureCreated();

            var classesFromDb = _dbContext.Classes.ToList();
            
            ClassesList = new ObservableCollection<SchoolClass>(classesFromDb);
            ClassesListBox.ItemsSource = ClassesList;
            
            SharedClassControl.StudentDetailsRequested += SharedClassControl_StudentDetailsRequested;
        }

        private void AddNewClass()
        {
            string newName = NewClassNameTextBox.Text.Trim();
            
            if (!string.IsNullOrEmpty(newName))
            {
                var newClass = new SchoolClass { Name = newName };

                _dbContext.Classes.Add(newClass);
                _dbContext.SaveChanges();

                ClassesList.Add(newClass);
                NewClassNameTextBox.Clear();
            }
            else
            {
                MessageBox.Show("Nazwa klasy nie może być pusta.", "Ajajajaj! Błąd walidacji...", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void AddClassButton_Click(object sender, RoutedEventArgs e)
        {
            AddNewClass();
        }

        private void NewClassNameTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                AddNewClass();
            }
        }
        
        // NOWA METODA: Globalne dodawanie prac pisemnych
        private void AddGlobalWorkButton_Click(object sender, RoutedEventArgs e)
        {
            var classes = _dbContext.Classes.ToList();
            if (classes.Count == 0)
            {
                MessageBox.Show("W bazie nie ma żadnych klas. Dodaj najpierw klasę.", "AJajajaj! Brak klas...", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new AddGlobalWorkDialog(classes)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                var selectedClasses = dialog.SelectedClassIds;
                if (!selectedClasses.Any()) return;

                foreach (var classId in selectedClasses)
                {
                    var newWork = new WrittenWork 
                    { 
                        Title = dialog.WorkTitle, 
                        WorkType = dialog.WorkType,
                        MaxFinalPoints = dialog.MaxFinalPoints,
                        DateWritten = dialog.DateWritten,
                        DateEntered = dialog.DateEntered,
                        SchoolClassId = classId,
                        
                        // Konieczne jest utworzenie nowych obiektów zadań dla każdej klasy z osobna
                        Tasks = dialog.Tasks.Select(t => new WrittenWorkTask 
                        {
                            TaskNumber = t.TaskNumber,
                            MaxPointsLevel1 = t.MaxPointsLevel1,
                            MaxPointsLevel2 = t.MaxPointsLevel2,
                            MaxPointsLevel3 = t.MaxPointsLevel3
                        }).ToList()
                    };

                    _dbContext.WrittenWorks.Add(newWork);
                }

                _dbContext.SaveChanges();
                MessageBox.Show($"Praca pisemna '{dialog.WorkTitle}' została pomyślnie dodana do {selectedClasses.Count} klas.", 
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
                MessageBoxResult result = MessageBox.Show(
                    $"Czy na pewno chcesz usunąć klasę {selectedClass.Name} i wszystkie przypisane do niej dane?", 
                    "Potwierdzenie usunięcia", 
                    MessageBoxButton.YesNo, 
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    _dbContext.Classes.Remove(selectedClass);
                    _dbContext.SaveChanges();
                    ClassesList.Remove(selectedClass);
                }
            }
        }

        private void EditClass_Click(object sender, RoutedEventArgs e)
        {
            if (ClassesListBox.SelectedItem is SchoolClass selectedClass)
            {
                var dialog = new EditClassDialog(selectedClass.Name)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() == true)
                {
                    string newName = dialog.NewName;

                    if (!string.IsNullOrEmpty(newName) && newName != selectedClass.Name)
                    {
                        selectedClass.Name = newName;
                        _dbContext.Classes.Update(selectedClass);
                        _dbContext.SaveChanges();
                        ClassesListBox.Items.Refresh();
                    }
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
                var newWindow = new ClassDetailsWindow(selectedClass);
                newWindow.Show();
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
    }
}