using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GradebookApp.Services;

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

    public partial class WorkEditorDialog : Window
    {
        private readonly GradebookDataService _dataService;
        private WrittenWork? _editingWork;
        
        private int? _targetClassId;
        private int? _targetStudentId;
        private bool _isTemplateMode;
        
        public WrittenWork SavedWork { get; private set; } = null!;

        private ObservableCollection<string> _groups = new ObservableCollection<string>();
        private Dictionary<string, ObservableCollection<TaskRow>> _divergedData = new Dictionary<string, ObservableCollection<TaskRow>>();
        public ObservableCollection<TaskRow> CurrentTasks { get; set; } = new ObservableCollection<TaskRow>();
        
        private bool _isInitializing = true;

        public WorkEditorDialog(int? editWorkId = null, int? targetClassId = null, int? targetStudentId = null, bool isTemplateMode = false)
        {
            InitializeComponent();
            _dataService = new GradebookDataService();
            _targetClassId = targetClassId;
            _targetStudentId = targetStudentId;
            _isTemplateMode = isTemplateMode;

            if (editWorkId.HasValue)
            {
                this.Title = "Edycja oceny"; SaveButton.Content = "Zapisz zmiany"; EditWarningText.Visibility = Visibility.Visible; UseTemplateButton.Visibility = Visibility.Collapsed;
                _editingWork = _dataService.GetWorkByIdWithTasks(editWorkId.Value);

                if (_editingWork != null)
                {
                    TitleTextBox.Text = _editingWork.Title; DateWrittenPicker.SelectedDate = _editingWork.DateWritten; DateEnteredPicker.SelectedDate = _editingWork.DateEntered; MaxPointsTextBox.Text = _editingWork.MaxFinalPoints.ToString();
                    foreach (ComboBoxItem item in WorkTypeComboBox.Items) if (item.Content?.ToString() == _editingWork.WorkType) { WorkTypeComboBox.SelectedItem = item; break; }

                    var grouped = _editingWork.Tasks.GroupBy(t => t.GroupName ?? "A").ToList();
                    foreach (var g in grouped) _groups.Add(g.Key);
                    if (_groups.Count == 0) { _groups.Add("A"); _groups.Add("B"); } 
                    
                    foreach (var g in grouped)
                    {
                        var list = new ObservableCollection<TaskRow>();
                        foreach (var t in g) list.Add(new TaskRow { TaskNumber = t.TaskNumber, L1 = t.MaxPointsLevel1 == 0 ? "" : t.MaxPointsLevel1?.ToString() ?? "", L2 = t.MaxPointsLevel2 == 0 ? "" : t.MaxPointsLevel2?.ToString() ?? "", L3 = t.MaxPointsLevel3 == 0 ? "" : t.MaxPointsLevel3?.ToString() ?? "" });
                        _divergedData[g.Key] = list;
                    }
                    if (grouped.Count == 0) _divergedData["A"] = new ObservableCollection<TaskRow> { new TaskRow { TaskNumber = 1 } };
                    bool identical = CheckIfIdentical(); ApplyToAllCheckBox.IsChecked = identical;
                    CurrentTasks = identical ? new ObservableCollection<TaskRow>(_divergedData[_groups[0]].Select(t => t.Clone())) : _divergedData[_groups[0]];
                }
            }
            else
            {
                this.Title = _targetStudentId.HasValue ? "Dodaj ocenę indywidualną" : "Dodaj ocenę";
                _groups.Add("A"); _groups.Add("B");
                _divergedData["A"] = new ObservableCollection<TaskRow>(); _divergedData["B"] = new ObservableCollection<TaskRow>();
                CurrentTasks = new ObservableCollection<TaskRow> { new TaskRow { TaskNumber = 1 }, new TaskRow { TaskNumber = 2 } };
                ApplyToAllCheckBox.IsChecked = true;
            }

            if (_isTemplateMode)
            {
                this.Title = "Kreator szablonu"; SaveButton.Content = "Zapisz szablon";
                DatesPanel.Visibility = Visibility.Collapsed; UseTemplateButton.Visibility = Visibility.Collapsed;
            }

            GroupsComboBox.ItemsSource = _groups; GroupsComboBox.SelectedIndex = 0;
            TasksDataGrid.ItemsSource = CurrentTasks; TaskCountTextBox.Text = CurrentTasks.Count > 0 ? CurrentTasks.Count.ToString() : "2";
            _isInitializing = false; TitleTextBox.Focus();
        }

        private void UseTemplate_Click(object sender, RoutedEventArgs e)
        {
            var templates = _dataService.GetAllTemplates();
            if (!templates.Any()) { MessageBox.Show("W systemie nie zapisano jeszcze żadnych szablonów.", "Brak danych", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            
            var contextMenu = new ContextMenu();
            foreach(var t in templates)
            {
                var menuItem = new MenuItem { Header = $"{t.WorkType.ToUpper()}: {t.Title}" };
                menuItem.Click += (s, ev) => ApplyTemplate(t);
                contextMenu.Items.Add(menuItem);
            }
            contextMenu.PlacementTarget = UseTemplateButton;
            contextMenu.IsOpen = true;
        }

        private void ApplyTemplate(WorkTemplate t)
        {
            _isInitializing = true;
            TitleTextBox.Text = t.Title; MaxPointsTextBox.Text = t.MaxFinalPoints.ToString();
            foreach (ComboBoxItem item in WorkTypeComboBox.Items) if (item.Content?.ToString() == t.WorkType) { WorkTypeComboBox.SelectedItem = item; break; }

            _groups.Clear(); _divergedData.Clear(); CurrentTasks.Clear();

            if (t.HasGroups && t.Tasks.Any(x => x.GroupName != null))
            {
                var grouped = t.Tasks.GroupBy(x => x.GroupName ?? "A").ToList();
                foreach (var g in grouped) _groups.Add(g.Key);
                foreach (var g in grouped)
                {
                    var list = new ObservableCollection<TaskRow>();
                    foreach (var task in g) list.Add(new TaskRow { TaskNumber = task.TaskNumber, L1 = task.MaxPointsLevel1 == 0 ? "" : task.MaxPointsLevel1?.ToString() ?? "", L2 = task.MaxPointsLevel2 == 0 ? "" : task.MaxPointsLevel2?.ToString() ?? "", L3 = task.MaxPointsLevel3 == 0 ? "" : task.MaxPointsLevel3?.ToString() ?? "" });
                    _divergedData[g.Key] = list;
                }
            }
            else
            {
                _groups.Add("A"); var list = new ObservableCollection<TaskRow>();
                foreach (var task in t.Tasks) list.Add(new TaskRow { TaskNumber = task.TaskNumber, L1 = task.MaxPointsLevel1 == 0 ? "" : task.MaxPointsLevel1?.ToString() ?? "", L2 = task.MaxPointsLevel2 == 0 ? "" : task.MaxPointsLevel2?.ToString() ?? "", L3 = task.MaxPointsLevel3 == 0 ? "" : task.MaxPointsLevel3?.ToString() ?? "" });
                _divergedData["A"] = list;
            }

            bool identical = CheckIfIdentical(); ApplyToAllCheckBox.IsChecked = identical;
            CurrentTasks = identical ? new ObservableCollection<TaskRow>(_divergedData[_groups[0]].Select(x => x.Clone())) : _divergedData[_groups[0]];

            GroupsComboBox.ItemsSource = _groups; GroupsComboBox.SelectedIndex = 0; TasksDataGrid.ItemsSource = CurrentTasks;
            TaskCountTextBox.Text = CurrentTasks.Count.ToString();
            _isInitializing = false;
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
                    if (Keyboard.FocusedElement is DataGridCell cell && (!cell.IsEditing && cell.Column.Header?.ToString() == "Zad."))
                        cell.MoveFocus(request);
                }
            }
            else if (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down)
            {
                if (e.OriginalSource is TextBox textBox)
                {
                    if ((e.Key == Key.Left && textBox.CaretIndex > 0) || (e.Key == Key.Right && textBox.CaretIndex < textBox.Text.Length))
                        return;

                    e.Handled = true;
                    var direction = e.Key == Key.Left ? FocusNavigationDirection.Left : e.Key == Key.Right ? FocusNavigationDirection.Right : e.Key == Key.Up ? FocusNavigationDirection.Up : FocusNavigationDirection.Down;
                    textBox.MoveFocus(new TraversalRequest(direction));
                }
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            TasksDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
            string title = TitleTextBox.Text.Trim(); string type = (WorkTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "inne";
            if (string.IsNullOrEmpty(title) || !double.TryParse(MaxPointsTextBox.Text.Trim(), out double m) || m <= 0) { MessageBox.Show("Sprawdź tytuł i poprawność punktów Max.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            // Dla szablonów nie będziemy generować listy Tasks, tylko WorkTemplateTasks
            if (_isTemplateMode)
            {
                var templateTasks = new List<WorkTemplateTask>();
                if (type == "aktywność") templateTasks.Add(new WorkTemplateTask { TaskNumber = 1, MaxPointsLevel3 = m });
                else
                {
                    foreach (var g in _groups)
                    {
                        var sourceList = ApplyToAllCheckBox.IsChecked == true ? CurrentTasks : _divergedData[g];
                        foreach (var row in sourceList)
                        {
                            double p1 = ParseScore(row.L1); double p2 = ParseScore(row.L2); double p3 = ParseScore(row.L3);
                            if (p1 < 0 || p2 < 0 || p3 < 0) { MessageBox.Show($"Błędny format liczb w grupie {g}, zadanie {row.TaskNumber}.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                            templateTasks.Add(new WorkTemplateTask { TaskNumber = row.TaskNumber, GroupName = g, MaxPointsLevel1 = p1, MaxPointsLevel2 = p2, MaxPointsLevel3 = p3 });
                        }
                    }
                }
                
                var newTemplate = new WorkTemplate { Title = title, WorkType = type, MaxFinalPoints = m, HasGroups = _groups.Count > 1, Tasks = templateTasks };
                _dataService.AddTemplate(newTemplate);
            }
            else
            {
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

                if (_editingWork != null)
                {
                    _dataService.UpdateWorkAndTasks(_editingWork, generatedTasks, title, m, type, _groups.Count > 1, DateWrittenPicker.SelectedDate, DateEnteredPicker.SelectedDate);
                    SavedWork = _editingWork;
                }
                else if (_targetClassId.HasValue)
                {
                    if (_targetStudentId.HasValue) SavedWork = _dataService.AddIndividualWork(_targetStudentId.Value, _targetClassId.Value, title, type, m, DateWrittenPicker.SelectedDate, DateEnteredPicker.SelectedDate, generatedTasks);
                    else
                    {
                        var newWork = new WrittenWork { Title = title, WorkType = type, MaxFinalPoints = m, DateWritten = DateWrittenPicker.SelectedDate, DateEntered = DateEnteredPicker.SelectedDate, HasGroups = _groups.Count > 1, SchoolClassId = _targetClassId.Value, Tasks = generatedTasks };
                        SavedWork = _dataService.AddWork(newWork);
                    }
                }
            }

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
        protected override void OnClosed(EventArgs e) { _dataService.Dispose(); base.OnClosed(e); }
    }
}