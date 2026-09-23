using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;

namespace GradebookApp
{
    public partial class WorkDetailsWindow : Window
    {
        private AppDbContext _dbContext;
        private WrittenWork _work = null!; // Dodano "= null!"
        public ObservableCollection<WrittenWorkTask> TasksCollection { get; set; } = new ObservableCollection<WrittenWorkTask>();

        public WorkDetailsWindow(int workId)
        {
            InitializeComponent();
            _dbContext = new AppDbContext();
            
            // Pobranie pracy wraz z zadaniami prosto z bazy (niezależny kontekst)
            _work = _dbContext.WrittenWorks.Include(w => w.Tasks).FirstOrDefault(w => w.Id == workId)!;

            if (_work != null)
            {
                TitleTextBox.Text = _work.Title;
                DateWrittenPicker.SelectedDate = _work.DateWritten;
                DateEnteredPicker.SelectedDate = _work.DateEntered;

                // Ustawienie odpowiedniego rodzaju pracy w ComboBox
                foreach (ComboBoxItem item in WorkTypeComboBox.Items)
                {
                    if (item.Content.ToString() == _work.WorkType)
                    {
                        WorkTypeComboBox.SelectedItem = item;
                        break;
                    }
                }

                // Wgranie zadań do kolekcji obserwowalnej i sortowanie po numerze
                TasksCollection = new ObservableCollection<WrittenWorkTask>(_work.Tasks.OrderBy(t => t.TaskNumber));
                TasksDataGrid.ItemsSource = TasksCollection;
                
                // Ustawienie liczby i włączenie nasłuchiwania na zmiany (dopiero po inicjalizacji, żeby nie wyczyściło danych)
                TaskCountTextBox.Text = TasksCollection.Count.ToString();
                TaskCountTextBox.TextChanged += TaskCountTextBox_TextChanged;
            }
        }

        private void TaskCountTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(TaskCountTextBox.Text.Trim(), out int count) && count > 0 && count <= 100)
            {
                AdjustTasksCollection(count);
            }
        }

        private void AdjustTasksCollection(int targetCount)
        {
            // Dodawanie nowych zadań
            while (TasksCollection.Count < targetCount)
            {
                var newTask = new WrittenWorkTask { TaskNumber = TasksCollection.Count + 1, WrittenWorkId = _work.Id };
                TasksCollection.Add(newTask);
                _dbContext.WrittenWorkTasks.Add(newTask); // Rejestracja w bazie
            }
            
            // Usuwanie nadmiarowych zadań z końca
            while (TasksCollection.Count > targetCount)
            {
                var taskToRemove = TasksCollection.Last();
                TasksCollection.Remove(taskToRemove);
                
                if (taskToRemove.Id != 0) // Jeśli zadanie już istniało w bazie, musimy je usunąć fizycznie
                {
                    _dbContext.WrittenWorkTasks.Remove(taskToRemove);
                }
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            TasksDataGrid.CommitEdit(DataGridEditingUnit.Row, true);

            string newTitle = TitleTextBox.Text.Trim();
            if (string.IsNullOrEmpty(newTitle))
            {
                MessageBox.Show("Podaj tytuł pracy pisemnej.", "Ajajajaj! Błąd...", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (TasksCollection.Count == 0)
            {
                MessageBox.Show("Praca pisemna musi zawierać przynajmniej jedno zadanie.", "Ajajajaj! Błąd...", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Twarde wymuszenie wypełnienia wszystkich komórek
            bool hasEmptyFields = TasksCollection.Any(t => 
                t.MaxPointsLevel1 == null || 
                t.MaxPointsLevel2 == null || 
                t.MaxPointsLevel3 == null);

            if (hasEmptyFields)
            {
                MessageBox.Show("Musisz wypełnić wszystkie pola punktacji. Wpisz 0, jeśli dany poziom nie jest punktowany.", 
                                "Ajajajaj! Brakujące dane...", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool hasNegativePoints = TasksCollection.Any(t => 
                t.MaxPointsLevel1 < 0 || t.MaxPointsLevel2 < 0 || t.MaxPointsLevel3 < 0);

            if (hasNegativePoints)
            {
                MessageBox.Show("Liczba punktów nie może być ujemna.", "Ajajajaj! Błąd wartości...", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Zapisanie właściwości tekstowych
            _work.Title = newTitle;
            _work.WorkType = (WorkTypeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "inne";
            _work.DateWritten = DateWrittenPicker.SelectedDate;
            _work.DateEntered = DateEnteredPicker.SelectedDate;

            // Zapis z odizolowanego kontekstu prosto do SQLite
            _dbContext.SaveChanges();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false; // Kontekst _dbContext zostaje porzucony bez zapisywania, wpisane modyfikacje znikają
        }
    }
}