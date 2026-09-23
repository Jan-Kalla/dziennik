using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace GradebookApp
{
    public partial class AddWorkDialog : Window
    {
        public string WorkTitle { get; private set; } = string.Empty;
        public string WorkType { get; private set; } = string.Empty;
        public double MaxFinalPoints { get; private set; } // NOWE
        public DateTime? DateWritten { get; private set; }
        public DateTime? DateEntered { get; private set; }
        
        public List<WrittenWorkTask> Tasks { get; private set; } = new List<WrittenWorkTask>();
        public ObservableCollection<WrittenWorkTask> TasksCollection { get; set; } = new ObservableCollection<WrittenWorkTask>();

        public AddWorkDialog()
        {
            InitializeComponent();
            TasksDataGrid.ItemsSource = TasksCollection;
            TitleTextBox.Focus();
        }

        private void TaskCountTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(TaskCountTextBox.Text.Trim(), out int count) && count > 0 && count <= 100)
            {
                AdjustTasksCollection(count);
            }
            else if (string.IsNullOrWhiteSpace(TaskCountTextBox.Text))
            {
                TasksCollection.Clear();
            }
        }

        private void AdjustTasksCollection(int targetCount)
        {
            while (TasksCollection.Count < targetCount)
            {
                TasksCollection.Add(new WrittenWorkTask { TaskNumber = TasksCollection.Count + 1 });
            }
            while (TasksCollection.Count > targetCount)
            {
                TasksCollection.RemoveAt(TasksCollection.Count - 1);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            TasksDataGrid.CommitEdit(DataGridEditingUnit.Row, true);

            WorkTitle = TitleTextBox.Text.Trim();
            WorkType = (WorkTypeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "inne";

            if (string.IsNullOrEmpty(WorkTitle))
            {
                MessageBox.Show("Podaj tytuł pracy pisemnej.", "Ajajajaj! Błąd...", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // WALIDACJA M
            if (!double.TryParse(MaxPointsTextBox.Text.Trim(), out double m) || m <= 0)
            {
                MessageBox.Show("Podaj poprawną wartość dla M (Max punktów końcowych musi być większe od 0).", "Ajajajaj! Błąd...", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (TasksCollection.Count == 0)
            {
                MessageBox.Show("Praca pisemna musi zawierać przynajmniej jedno zadanie.", "Ajajajaj! Błąd...", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool hasEmptyFields = TasksCollection.Any(t => t.MaxPointsLevel1 == null || t.MaxPointsLevel2 == null || t.MaxPointsLevel3 == null);
            if (hasEmptyFields)
            {
                MessageBox.Show("Musisz wypełnić wszystkie pola punktacji. Wpisz 0, jeśli poziom nie jest punktowany.", "Brakujące dane", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool hasNegativePoints = TasksCollection.Any(t => t.MaxPointsLevel1 < 0 || t.MaxPointsLevel2 < 0 || t.MaxPointsLevel3 < 0);
            if (hasNegativePoints)
            {
                MessageBox.Show("Liczba punktów nie może być ujemna.", "AJajajaj! Błąd wartości...", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MaxFinalPoints = m;
            DateWritten = DateWrittenPicker.SelectedDate;
            DateEntered = DateEnteredPicker.SelectedDate;
            
            Tasks = TasksCollection.ToList();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}