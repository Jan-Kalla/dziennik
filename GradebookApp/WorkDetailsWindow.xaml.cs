using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GradebookApp.Services;
using System.Windows.Input;

namespace GradebookApp
{
    public partial class WorkDetailsWindow : Window
    {
        private readonly GradebookDataService _dataService;
        private WrittenWork _work = null!;
        
        private ObservableCollection<string> _groups = new ObservableCollection<string>();
        private Dictionary<string, ObservableCollection<TaskRow>> _divergedData = new Dictionary<string, ObservableCollection<TaskRow>>();
        public ObservableCollection<TaskRow> CurrentTasks { get; set; } = new ObservableCollection<TaskRow>();
        
        private bool _isInitializing = true;

        public WorkDetailsWindow(int workId)
        {
            InitializeComponent();
            _dataService = new GradebookDataService();
            _work = _dataService.GetWorkByIdWithTasks(workId)!;

            if (_work != null)
            {
                TitleTextBox.Text = _work.Title; DateWrittenPicker.SelectedDate = _work.DateWritten; DateEnteredPicker.SelectedDate = _work.DateEntered; MaxPointsTextBox.Text = _work.MaxFinalPoints.ToString();
                foreach (ComboBoxItem item in WorkTypeComboBox.Items) if (item.Content?.ToString() == _work.WorkType) { WorkTypeComboBox.SelectedItem = item; break; }

                var grouped = _work.Tasks.GroupBy(t => t.GroupName ?? "A").ToList();
                foreach (var g in grouped) _groups.Add(g.Key);
                if (_groups.Count == 0) { _groups.Add("A"); _groups.Add("B"); } 
                
                foreach (var g in grouped)
                {
                    var list = new ObservableCollection<TaskRow>();
                    foreach (var t in g) list.Add(new TaskRow { TaskNumber = t.TaskNumber, L1 = t.MaxPointsLevel1 == 0 ? "" : t.MaxPointsLevel1?.ToString() ?? "", L2 = t.MaxPointsLevel2 == 0 ? "" : t.MaxPointsLevel2?.ToString() ?? "", L3 = t.MaxPointsLevel3 == 0 ? "" : t.MaxPointsLevel3?.ToString() ?? "" });
                    _divergedData[g.Key] = list;
                }
                
                if (grouped.Count == 0) _divergedData["A"] = new ObservableCollection<TaskRow> { new TaskRow { TaskNumber = 1 } };

                bool identical = CheckIfIdentical();
                ApplyToAllCheckBox.IsChecked = identical;
                CurrentTasks = identical ? new ObservableCollection<TaskRow>(_divergedData[_groups[0]].Select(t => t.Clone())) : _divergedData[_groups[0]];

                GroupsComboBox.ItemsSource = _groups; GroupsComboBox.SelectedIndex = 0;
                TasksDataGrid.ItemsSource = CurrentTasks;
                TaskCountTextBox.Text = CurrentTasks.Count.ToString();
                _isInitializing = false; TitleTextBox.Focus();
            }
        }

        private bool CheckIfIdentical()
        {
            if (_groups.Count <= 1) return true;
            var first = _divergedData[_groups[0]];
            for (int i = 1; i < _groups.Count; i++)
            {
                var compare = _divergedData[_groups[i]];
                if (first.Count != compare.Count) return false;
                for (int j = 0; j < first.Count; j++) if (first[j].L1 != compare[j].L1 || first[j].L2 != compare[j].L2 || first[j].L3 != compare[j].L3) return false;
            }
            return true;
        }

        private void WorkTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MaxPointsLabel == null) return; 
            if ((WorkTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() == "aktywność")
            { GroupManagementPanel.Visibility = TasksDataGrid.Visibility = ApplyToAllCheckBox.Visibility = Visibility.Collapsed; MaxPointsLabel.Text = "Ilość przyznanych punktów:"; }
            else { GroupManagementPanel.Visibility = TasksDataGrid.Visibility = ApplyToAllCheckBox.Visibility = Visibility.Visible; MaxPointsLabel.Text = "Max:"; }
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
            if (ApplyToAllCheckBox.IsChecked == true) _divergedData.Clear();
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
                CurrentTasks = _divergedData[newGroup]; TasksDataGrid.ItemsSource = CurrentTasks; TaskCountTextBox.Text = CurrentTasks.Count.ToString();
            }
        }

        private void AddGroup_Click(object sender, RoutedEventArgs e)
        {
            string name = NewGroupNameTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(name) && !_groups.Contains(name))
            {
                _groups.Add(name); if (ApplyToAllCheckBox.IsChecked == false) _divergedData[name] = new ObservableCollection<TaskRow>(CurrentTasks.Select(t => t.Clone()));
                GroupsComboBox.SelectedItem = name; NewGroupNameTextBox.Clear();
            }
        }

        private void RemoveGroup_Click(object sender, RoutedEventArgs e)
        {
            if (_groups.Count <= 1) { MessageBox.Show("Musi pozostać co najmniej jedna grupa.", "Zablokowane", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            string current = GroupsComboBox.SelectedItem as string ?? "";
            _groups.Remove(current); if (_divergedData.ContainsKey(current)) _divergedData.Remove(current);
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
            string title = TitleTextBox.Text.Trim(); string type = (WorkTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "inne";
            if (string.IsNullOrEmpty(title) || !double.TryParse(MaxPointsTextBox.Text.Trim(), out double m) || m <= 0) { MessageBox.Show("Sprawdź tytuł i poprawność punktów Max.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            var generatedTasks = new List<WrittenWorkTask>();
            if (type == "aktywność") generatedTasks.Add(new WrittenWorkTask { TaskNumber = 1, MaxPointsLevel3 = m });
            else
            {
                foreach (var g in _groups)
                {
                    var sourceList = ApplyToAllCheckBox.IsChecked == true ? CurrentTasks : _divergedData[g];
                    foreach (var row in sourceList)
                    {
                        double p1 = ParseScore(row.L1); double p2 = ParseScore(row.L2); double p3 = ParseScore(row.L3);
                        if (p1 < 0 || p2 < 0 || p3 < 0) { MessageBox.Show($"Błędny format liczb w grupie {g}, zadanie {row.TaskNumber}.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                        generatedTasks.Add(new WrittenWorkTask { TaskNumber = row.TaskNumber, GroupName = g, MaxPointsLevel1 = p1, MaxPointsLevel2 = p2, MaxPointsLevel3 = p3 });
                    }
                }
            }

            _dataService.UpdateWorkAndTasks(_work, generatedTasks, title, m, type, _groups.Count > 1, DateWrittenPicker.SelectedDate, DateEnteredPicker.SelectedDate);
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
        protected override void OnClosed(EventArgs e) { _dataService.Dispose(); base.OnClosed(e); }
    }
}