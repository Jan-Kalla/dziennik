using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GradebookApp
{
    public class TaskRow : INotifyPropertyChanged
    {
        public int TaskNumber { get; set; }
        private string _l1 = ""; public string L1 { get => _l1; set { _l1 = value; OnPropertyChanged(); } }
        private string _l2 = ""; public string L2 { get => _l2; set { _l2 = value; OnPropertyChanged(); } }
        private string _l3 = ""; public string L3 { get => _l3; set { _l3 = value; OnPropertyChanged(); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public TaskRow Clone() => new TaskRow { TaskNumber = TaskNumber, L1 = L1, L2 = L2, L3 = L3 };
    }

    public partial class AddWorkDialog : Window
    {
        public string WorkTitle { get; private set; } = string.Empty;
        public string WorkType { get; private set; } = string.Empty;
        public double MaxFinalPoints { get; private set; } 
        public DateTime? DateWritten { get; private set; }
        public DateTime? DateEntered { get; private set; }
        public bool HasGroups => _groups.Count > 1; 
        public List<WrittenWorkTask> Tasks { get; private set; } = new List<WrittenWorkTask>();
        
        private ObservableCollection<string> _groups = new ObservableCollection<string> { "A", "B" };
        private Dictionary<string, ObservableCollection<TaskRow>> _divergedData = new Dictionary<string, ObservableCollection<TaskRow>>();
        public ObservableCollection<TaskRow> CurrentTasks { get; set; } = new ObservableCollection<TaskRow>();

        private bool _isInitializing = true;

        public AddWorkDialog()
        {
            InitializeComponent();
            GroupsComboBox.ItemsSource = _groups;
            GroupsComboBox.SelectedIndex = 0;
            TasksDataGrid.ItemsSource = CurrentTasks;
            TaskCountTextBox.Text = "2";
            _isInitializing = false;
            TitleTextBox.Focus();
        }

        private void WorkTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MaxPointsLabel == null) return; 
            if ((WorkTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() == "aktywność")
            {
                GroupManagementPanel.Visibility = TasksDataGrid.Visibility = ApplyToAllCheckBox.Visibility = Visibility.Collapsed;
                MaxPointsLabel.Text = "Ilość przyznanych punktów:"; 
            }
            else
            {
                GroupManagementPanel.Visibility = TasksDataGrid.Visibility = ApplyToAllCheckBox.Visibility = Visibility.Visible;
                MaxPointsLabel.Text = "Max:"; 
            }
        }

        private void TaskCountTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(TaskCountTextBox.Text.Trim(), out int count) && count > 0)
            {
                while (CurrentTasks.Count < count) CurrentTasks.Add(new TaskRow { TaskNumber = CurrentTasks.Count + 1 });
                while (CurrentTasks.Count > count) CurrentTasks.RemoveAt(CurrentTasks.Count - 1);
            }
        }

        private void ApplyToAllCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            if (ApplyToAllCheckBox.IsChecked == true)
            {
                _divergedData.Clear();
            }
            else
            {
                foreach (var g in _groups) _divergedData[g] = new ObservableCollection<TaskRow>(CurrentTasks.Select(t => t.Clone()));
                CurrentTasks = _divergedData[GroupsComboBox.SelectedItem as string ?? _groups[0]];
                TasksDataGrid.ItemsSource = CurrentTasks;
            }
        }

        private void GroupsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || GroupsComboBox.SelectedItem == null) return;
            string newGroup = GroupsComboBox.SelectedItem.ToString()!;
            
            if (ApplyToAllCheckBox.IsChecked == false && _divergedData.ContainsKey(newGroup))
            {
                CurrentTasks = _divergedData[newGroup];
                TasksDataGrid.ItemsSource = CurrentTasks;
                TaskCountTextBox.Text = CurrentTasks.Count.ToString();
            }
        }

        private void AddGroup_Click(object sender, RoutedEventArgs e)
        {
            string name = NewGroupNameTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(name) && !_groups.Contains(name))
            {
                _groups.Add(name);
                if (ApplyToAllCheckBox.IsChecked == false) _divergedData[name] = new ObservableCollection<TaskRow>(CurrentTasks.Select(t => t.Clone()));
                GroupsComboBox.SelectedItem = name;
                NewGroupNameTextBox.Clear();
            }
        }

        private void RemoveGroup_Click(object sender, RoutedEventArgs e)
        {
            if (_groups.Count <= 1) { MessageBox.Show("Musi pozostać co najmniej jedna grupa.", "Zablokowane", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            string current = GroupsComboBox.SelectedItem as string ?? "";
            _groups.Remove(current);
            if (_divergedData.ContainsKey(current)) _divergedData.Remove(current);
            GroupsComboBox.SelectedIndex = 0;
        }

        private double ParseScore(string? input) => string.IsNullOrWhiteSpace(input) ? 0 : (double.TryParse(input, out double val) ? val : -1);

        private void TasksDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Tab)
            {
                e.Handled = true;
                var direction = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next;
                var request = new TraversalRequest(direction);
                
                if (Keyboard.FocusedElement is UIElement element)
                {
                    element.MoveFocus(request);
                    
                    // Wykorzystujemy zmienną 'cell', która już jest zrzutowana na typ UIElement (DataGridCell)
                    if (Keyboard.FocusedElement is DataGridCell cell && (!cell.IsEditing && cell.Column.Header?.ToString() == "Zad."))
                    {
                        cell.MoveFocus(request);
                    }
                }
            }
            else if (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down)
            {
                if (e.OriginalSource is TextBox textBox)
                {
                    // Jeśli kursor tekstowy nie jest na skraju wpisanego tekstu, pozwalamy na normalne poruszanie się wewnątrz kratki
                    if ((e.Key == Key.Left && textBox.CaretIndex > 0) || 
                        (e.Key == Key.Right && textBox.CaretIndex < textBox.Text.Length))
                    {
                        return;
                    }

                    e.Handled = true;
                    var direction = e.Key == Key.Left ? FocusNavigationDirection.Left : 
                                    e.Key == Key.Right ? FocusNavigationDirection.Right : 
                                    e.Key == Key.Up ? FocusNavigationDirection.Up : FocusNavigationDirection.Down;

                    textBox.MoveFocus(new TraversalRequest(direction));
                }
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            TasksDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
            WorkTitle = TitleTextBox.Text.Trim();
            WorkType = (WorkTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "inne";

            if (string.IsNullOrEmpty(WorkTitle) || !double.TryParse(MaxPointsTextBox.Text.Trim(), out double m) || m <= 0)
            {
                MessageBox.Show("Podaj tytuł oceny i prawidłową wartość Max (większą od zera).", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Tasks.Clear();
            if (WorkType == "aktywność") Tasks.Add(new WrittenWorkTask { TaskNumber = 1, MaxPointsLevel3 = m });
            else
            {
                foreach (var g in _groups)
                {
                    var sourceList = ApplyToAllCheckBox.IsChecked == true ? CurrentTasks : _divergedData[g];
                    foreach (var row in sourceList)
                    {
                        double p1 = ParseScore(row.L1); double p2 = ParseScore(row.L2); double p3 = ParseScore(row.L3);
                        if (p1 < 0 || p2 < 0 || p3 < 0) { MessageBox.Show($"Błędny format liczb w grupie {g}, zadanie {row.TaskNumber}.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                        Tasks.Add(new WrittenWorkTask { TaskNumber = row.TaskNumber, GroupName = g, MaxPointsLevel1 = p1, MaxPointsLevel2 = p2, MaxPointsLevel3 = p3 });
                    }
                }
            }

            MaxFinalPoints = m; DateWritten = DateWrittenPicker.SelectedDate; DateEntered = DateEnteredPicker.SelectedDate;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}