using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace GradebookApp
{
    public partial class AddWorkDialog : Window
    {
        public string WorkTitle { get; private set; } = string.Empty;
        public string WorkType { get; private set; } = string.Empty;
        public double MaxFinalPoints { get; private set; } 
        public DateTime? DateWritten { get; private set; }
        public DateTime? DateEntered { get; private set; }
        public bool HasGroups { get; private set; } 
        
        public List<WrittenWorkTask> Tasks { get; private set; } = new List<WrittenWorkTask>();
        
        // Pamięć podręczna grup: Nazwa Grupy -> Liczba Zadań
        private Dictionary<string, int> _activeGroups = new Dictionary<string, int>();
        
        // Pamięć podręczna tabeli, zapobiegająca utracie wpisanych punktów przy odświeżaniu UI
        private Dictionary<string, Dictionary<int, Tuple<string, string, string>>> _backup = new Dictionary<string, Dictionary<int, Tuple<string, string, string>>>();
        
        private DataTable _dataTable = new DataTable();

        public AddWorkDialog()
        {
            InitializeComponent();
            TitleTextBox.Focus();
        }

        private void WorkTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BaseTaskCountPanel == null || MaxPointsLabel == null) return; 

            string selectedType = (WorkTypeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "";
            if (selectedType == "aktywność")
            {
                BaseTaskCountPanel.Visibility = Visibility.Collapsed;
                TasksDataGrid.Visibility = Visibility.Collapsed;
                HasGroupsCheckBox.Visibility = Visibility.Collapsed;
                HasGroupsCheckBox.IsChecked = false;
                MaxPointsLabel.Text = "Ilość przyznanych punktów:"; 
            }
            else
            {
                BaseTaskCountPanel.Visibility = HasGroupsCheckBox.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
                TasksDataGrid.Visibility = Visibility.Visible;
                HasGroupsCheckBox.Visibility = Visibility.Visible;
                MaxPointsLabel.Text = "Max:"; 
            }
        }

        private void HasGroupsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (HasGroupsCheckBox.IsChecked == true)
            {
                GroupManagementPanel.Visibility = Visibility.Visible;
                BaseTaskCountPanel.Visibility = Visibility.Collapsed;
                if (_activeGroups.Count == 0)
                {
                    _activeGroups.Add("A", 3);
                    _activeGroups.Add("B", 3);
                }
            }
            else
            {
                GroupManagementPanel.Visibility = Visibility.Collapsed;
                BaseTaskCountPanel.Visibility = Visibility.Visible;
            }
            RefreshGroupsComboBox();
            RebuildDataTable();
        }

        private void AddGroup_Click(object sender, RoutedEventArgs e)
        {
            string name = NewGroupNameTextBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) return;
            if (_activeGroups.ContainsKey(name)) 
            { 
                MessageBox.Show("Grupa o takiej nazwie już znajduje się w tabeli.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning); 
                return; 
            }

            if (!int.TryParse(NewGroupTaskCountTextBox.Text.Trim(), out int count) || count <= 0 || count > 100)
            {
                MessageBox.Show("Wpisz poprawną liczbę zadań dla tej grupy (1-100).", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning); 
                return;
            }

            _activeGroups.Add(name, count);
            NewGroupNameTextBox.Text = string.Empty;
            NewGroupTaskCountTextBox.Text = string.Empty;
            RefreshGroupsComboBox();
            RebuildDataTable();
        }

        private void RemoveGroup_Click(object sender, RoutedEventArgs e)
        {
            if (GroupsComboBox.SelectedItem is string name && _activeGroups.ContainsKey(name))
            {
                _activeGroups.Remove(name);
                RefreshGroupsComboBox();
                RebuildDataTable();
            }
        }

        private void RefreshGroupsComboBox()
        {
            GroupsComboBox.ItemsSource = null;
            GroupsComboBox.ItemsSource = _activeGroups.Keys.OrderBy(k => k).ToList();
            if (_activeGroups.Count > 0) GroupsComboBox.SelectedIndex = 0;
        }

        private void TaskCountTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (HasGroupsCheckBox.IsChecked == false)
            {
                RebuildDataTable();
            }
        }

        private void SaveBackup()
        {
            if (_dataTable == null) return;
            try { TasksDataGrid.CommitEdit(DataGridEditingUnit.Row, true); } catch { }

            foreach (DataRow row in _dataTable.Rows)
            {
                int tNum = (int)row["TaskNumber"];
                
                if (HasGroupsCheckBox.IsChecked == true)
                {
                    foreach (var group in _activeGroups)
                    {
                        if (tNum > group.Value) continue;

                        if (!_backup.ContainsKey(group.Key)) _backup[group.Key] = new Dictionary<int, Tuple<string, string, string>>();
                        
                        string l1 = row[$"{group.Key}_L1"].ToString() ?? "";
                        string l2 = row[$"{group.Key}_L2"].ToString() ?? "";
                        string l3 = row[$"{group.Key}_L3"].ToString() ?? "";
                        
                        _backup[group.Key][tNum] = new Tuple<string, string, string>(l1, l2, l3);
                    }
                }
                else
                {
                    if (!_backup.ContainsKey("BASE")) _backup["BASE"] = new Dictionary<int, Tuple<string, string, string>>();
                    
                    string l1 = row["BASE_L1"].ToString() ?? "";
                    string l2 = row["BASE_L2"].ToString() ?? "";
                    string l3 = row["BASE_L3"].ToString() ?? "";
                    
                    _backup["BASE"][tNum] = new Tuple<string, string, string>(l1, l2, l3);
                }
            }
        }

        private void RebuildDataTable()
        {
            SaveBackup();

            _dataTable = new DataTable();
            TasksDataGrid.Columns.Clear();

            _dataTable.Columns.Add("TaskNumber", typeof(int));
            TasksDataGrid.Columns.Add(new DataGridTextColumn { Header = "Zad.", Binding = new Binding("TaskNumber"), IsReadOnly = true });

            int maxTasks = 0;

            if (HasGroupsCheckBox.IsChecked == true)
            {
                maxTasks = _activeGroups.Count > 0 ? _activeGroups.Values.Max() : 0;
                
                foreach (var group in _activeGroups.Keys.OrderBy(k => k))
                {
                    _dataTable.Columns.Add($"{group}_L1", typeof(string));
                    _dataTable.Columns.Add($"{group}_L2", typeof(string));
                    _dataTable.Columns.Add($"{group}_L3", typeof(string));

                    TasksDataGrid.Columns.Add(new DataGridTextColumn { Header = $"{group} Poz I", Binding = new Binding($"{group}_L1") });
                    TasksDataGrid.Columns.Add(new DataGridTextColumn { Header = $"{group} Poz II", Binding = new Binding($"{group}_L2") });
                    TasksDataGrid.Columns.Add(new DataGridTextColumn { Header = $"{group} Poz III", Binding = new Binding($"{group}_L3") });
                }
            }
            else
            {
                int.TryParse(TaskCountTextBox.Text.Trim(), out maxTasks);
                if (maxTasks <= 0) return;

                _dataTable.Columns.Add("BASE_L1", typeof(string));
                _dataTable.Columns.Add("BASE_L2", typeof(string));
                _dataTable.Columns.Add("BASE_L3", typeof(string));

                TasksDataGrid.Columns.Add(new DataGridTextColumn { Header = "Poz I", Binding = new Binding("BASE_L1") });
                TasksDataGrid.Columns.Add(new DataGridTextColumn { Header = "Poz II", Binding = new Binding("BASE_L2") });
                TasksDataGrid.Columns.Add(new DataGridTextColumn { Header = "Poz III", Binding = new Binding("BASE_L3") });
            }

            for (int i = 1; i <= maxTasks; i++)
            {
                DataRow row = _dataTable.NewRow();
                row["TaskNumber"] = i;

                if (HasGroupsCheckBox.IsChecked == true)
                {
                    foreach (var group in _activeGroups)
                    {
                        if (i <= group.Value)
                        {
                            if (_backup.ContainsKey(group.Key) && _backup[group.Key].ContainsKey(i))
                            {
                                row[$"{group.Key}_L1"] = _backup[group.Key][i].Item1;
                                row[$"{group.Key}_L2"] = _backup[group.Key][i].Item2;
                                row[$"{group.Key}_L3"] = _backup[group.Key][i].Item3;
                            }
                        }
                        else
                        {
                            // Blokujemy puste wiersze u grup, które mają mniej zadań niż inne obok
                            row[$"{group.Key}_L1"] = "-";
                            row[$"{group.Key}_L2"] = "-";
                            row[$"{group.Key}_L3"] = "-";
                        }
                    }
                }
                else
                {
                    if (_backup.ContainsKey("BASE") && _backup["BASE"].ContainsKey(i))
                    {
                        row["BASE_L1"] = _backup["BASE"][i].Item1;
                        row["BASE_L2"] = _backup["BASE"][i].Item2;
                        row["BASE_L3"] = _backup["BASE"][i].Item3;
                    }
                }

                _dataTable.Rows.Add(row);
            }

            TasksDataGrid.ItemsSource = _dataTable.DefaultView;
        }

        private void TasksDataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            var colHeader = e.Column.Header?.ToString() ?? "";
            if (colHeader == "Zad.") { e.Cancel = true; return; }

            if (HasGroupsCheckBox.IsChecked == true)
            {
                string groupName = colHeader.Split(' ')[0];
                var rowView = e.Row.Item as DataRowView;
                if (rowView != null)
                {
                    int taskNum = (int)rowView["TaskNumber"];
                    // Blokada edycji komórki, jeśli wykracza ona poza liczbę zadań zadeklarowaną dla danej grupy!
                    if (_activeGroups.ContainsKey(groupName) && taskNum > _activeGroups[groupName])
                    {
                        e.Cancel = true;
                    }
                }
            }
        }

        private void TasksDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Left || e.Key == Key.Right)
            {
                var dir = e.Key == Key.Left ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next;
                if (Keyboard.FocusedElement is UIElement element)
                {
                    element.MoveFocus(new TraversalRequest(dir));
                    e.Handled = true;
                }
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            WorkTitle = TitleTextBox.Text.Trim();
            WorkType = (WorkTypeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "inne";
            HasGroups = HasGroupsCheckBox.IsChecked == true;

            if (string.IsNullOrEmpty(WorkTitle))
            {
                MessageBox.Show("Podaj tytuł oceny.", "Ajajajaj! Błąd...", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!double.TryParse(MaxPointsTextBox.Text.Trim(), out double m) || m <= 0)
            {
                MessageBox.Show("Podaj poprawną wartość dla M (Max musi być większe od 0).", "Ajajajaj! Błąd...", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SaveBackup();
            Tasks.Clear();

            if (WorkType == "aktywność")
            {
                Tasks.Add(new WrittenWorkTask { TaskNumber = 1, MaxPointsLevel3 = m });
            }
            else
            {
                if (HasGroups)
                {
                    if (_activeGroups.Count == 0)
                    {
                        MessageBox.Show("Zaznaczono tryb zmiennych grup. Musisz dodać przynajmniej jedną do tabeli.", "Ajajajaj! Błąd...", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    foreach (var group in _activeGroups)
                    {
                        for (int i = 1; i <= group.Value; i++)
                        {
                            string l1 = _backup.ContainsKey(group.Key) && _backup[group.Key].ContainsKey(i) ? _backup[group.Key][i].Item1 : "";
                            string l2 = _backup.ContainsKey(group.Key) && _backup[group.Key].ContainsKey(i) ? _backup[group.Key][i].Item2 : "";
                            string l3 = _backup.ContainsKey(group.Key) && _backup[group.Key].ContainsKey(i) ? _backup[group.Key][i].Item3 : "";

                            if (string.IsNullOrWhiteSpace(l1) || string.IsNullOrWhiteSpace(l2) || string.IsNullOrWhiteSpace(l3))
                            {
                                MessageBox.Show($"Musisz wypełnić wszystkie pola dla grupy {group.Key}, zadanie {i}. Wpisz 0 dla poziomów niepunktowanych.", "Brak danych", MessageBoxButton.OK, MessageBoxImage.Warning);
                                return;
                            }

                            if (!double.TryParse(l1, out double p1) || !double.TryParse(l2, out double p2) || !double.TryParse(l3, out double p3))
                            {
                                MessageBox.Show($"Wykryto błędny format liczby w grupie {group.Key}, zadanie {i}.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning);
                                return;
                            }

                            if (p1 < 0 || p2 < 0 || p3 < 0)
                            {
                                MessageBox.Show($"Punkty nie mogą być ujemne (Grupa {group.Key}, zad. {i}).", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning);
                                return;
                            }

                            Tasks.Add(new WrittenWorkTask
                            {
                                TaskNumber = i,
                                GroupName = group.Key,
                                MaxPointsLevel1 = p1,
                                MaxPointsLevel2 = p2,
                                MaxPointsLevel3 = p3
                            });
                        }
                    }
                }
                else
                {
                    int.TryParse(TaskCountTextBox.Text.Trim(), out int count);
                    if (count <= 0)
                    {
                        MessageBox.Show("Podaj poprawną liczbę zadań.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    for (int i = 1; i <= count; i++)
                    {
                        string l1 = _backup.ContainsKey("BASE") && _backup["BASE"].ContainsKey(i) ? _backup["BASE"][i].Item1 : "";
                        string l2 = _backup.ContainsKey("BASE") && _backup["BASE"].ContainsKey(i) ? _backup["BASE"][i].Item2 : "";
                        string l3 = _backup.ContainsKey("BASE") && _backup["BASE"].ContainsKey(i) ? _backup["BASE"][i].Item3 : "";

                        if (string.IsNullOrWhiteSpace(l1) || string.IsNullOrWhiteSpace(l2) || string.IsNullOrWhiteSpace(l3))
                        {
                            MessageBox.Show($"Wypełnij wszystkie poziomy dla zadania {i}. Wpisz 0 tam, gdzie brak punktów.", "Brak danych", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        if (!double.TryParse(l1, out double p1) || !double.TryParse(l2, out double p2) || !double.TryParse(l3, out double p3) || p1 < 0 || p2 < 0 || p3 < 0)
                        {
                            MessageBox.Show($"Błędne wartości punktowe w zadaniu {i}.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        Tasks.Add(new WrittenWorkTask
                        {
                            TaskNumber = i,
                            MaxPointsLevel1 = p1,
                            MaxPointsLevel2 = p2,
                            MaxPointsLevel3 = p3
                        });
                    }
                }
            }

            MaxFinalPoints = m;
            DateWritten = DateWrittenPicker.SelectedDate;
            DateEntered = DateEnteredPicker.SelectedDate;
            
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}