using System;
using System.Windows;
using System.Windows.Controls;
using GradebookApp.ViewModels;
using GradebookApp.Services;

namespace GradebookApp
{
    public partial class StudentDetailsControl : UserControl
    {
        private readonly GradebookDataService _dataService;
        private Student _currentStudent = null!;

        public event EventHandler? StudentUpdated;

        public StudentDetailsControl()
        {
            InitializeComponent();
            _dataService = new GradebookDataService();
        }

        public void LoadStudentData(Student student)
        {
            _currentStudent = student;
            
            var dbStudent = _dataService.GetStudentById(student.Id);
            if (dbStudent == null) return;

            StudentNameText.Text = dbStudent.FullName;

            // Cała logika pobierania i kalkulacji schowana za jedną czystą metodą
            var result = _dataService.GetStudentWorksAndRecalculateAverage(dbStudent);
            
            if (dbStudent.AveragePercentage == result.Average)
            {
                StudentUpdated?.Invoke(this, EventArgs.Empty);
            }

            StudentAverageText.Text = $"- Średnia: {result.Average:0}%";
            StudentWorksDataGrid.ItemsSource = result.WorksList;
        }

        private void AddIndividualGrade_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AddWorkDialog { Owner = Window.GetWindow(this) };

            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.WorkTitle))
            {
                var newWork = _dataService.AddIndividualWork(
                    _currentStudent.Id, _currentStudent.SchoolClassId, dialog.WorkTitle, 
                    dialog.WorkType, dialog.MaxFinalPoints, dialog.DateWritten, dialog.DateEntered, dialog.Tasks);

                if (newWork.WorkType.ToLower() == "aktywność")
                {
                    _dataService.SaveActivityScore(_currentStudent.Id, newWork);
                    LoadStudentData(_currentStudent);
                }
                else
                {
                    LoadStudentData(_currentStudent);

                    var gradeWindow = new StudentWorkDetailsWindow(_currentStudent.Id, newWork.Id) { Owner = Window.GetWindow(this) };
                    gradeWindow.DataSavedEvent += (s, ev) => LoadStudentData(_currentStudent);
                    gradeWindow.ShowDialog();
                }
            }
        }

        private void GradeWork_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is StudentWorkViewModel vm)
            {
                var window = new StudentWorkDetailsWindow(_currentStudent.Id, vm.WorkId) { Owner = Window.GetWindow(this) };
                window.DataSavedEvent += (s, ev) => LoadStudentData(_currentStudent);
                window.ShowDialog();
            }
        }

        // Metoda podłączona do opcji menu kontekstowego "Usuń ocenę" w pliku XAML
        private void DeleteIndividualGrade_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var contextMenu = menuItem?.Parent as ContextMenu;
            var button = contextMenu?.PlacementTarget as Button;

            if (button?.DataContext is StudentWorkViewModel vm)
            {
                var work = _dataService.GetWorkById(vm.WorkId);
                if (work != null)
                {
                    if (work.IsIndividual)
                    {
                        var result = MessageBox.Show($"Czy na pewno chcesz usunąć ocenę '{work.Title}'?", 
                                                     "Potwierdzenie usunięcia", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (result == MessageBoxResult.Yes)
                        {
                            _dataService.DeleteWork(work);
                            LoadStudentData(_currentStudent);
                        }
                    }
                    else
                    {
                        MessageBox.Show("To jest ocena globalna przypisana do całej klasy. Aby usunąć ją całkowicie, przejdź do widoku klasy.", 
                                        "Ajajajaj! Zablokowane", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
        }
    }
}