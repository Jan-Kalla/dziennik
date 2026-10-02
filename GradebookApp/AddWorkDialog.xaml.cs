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
        
        private Dictionary<string, int> _activeGroups = new Dictionary<string, int>();
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
            if (GroupManagementPanel == null) return;

            if (HasGroupsCheckBox.IsChecked == true)
            {
                GroupManagementPanel.Visibility = Visibility.Visible;
                BaseTaskCountPanel.Visibility = Visibility.Collapsed;
                
                this.ClearValue(Window.WidthProperty); 
                this.SizeToContent = SizeToContent.WidthAndHeight;

                if (_activeGroups.Count == 0)
                {
                    int.TryParse(TaskCountTextBox.Text.Trim(), out int count);
                    if (count <= 0) count = 1;
                    
                    _activeGroups.Add("A", count);
                    _activeGroups.Add("B", count);
                }
            }
            else
            {
                GroupManagementPanel.Visibility = Visibility.Collapsed;
                BaseTaskCountPanel.Visibility = Visibility.Visible;
                
                this.SizeToContent = SizeToContent.Height;
                this.Width = 470; 
            }
            RefreshGroupsComboBox();
            if (this.IsLoaded) RebuildDataTable();
        }

        private void GroupsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GroupsComboBox.SelectedItem is string name && _activeGroups.ContainsKey(name))
            {
                EditGroupTaskCountTextBox.Text = _activeGroups[name].ToString();
            }
            else
            {
                EditGroupTaskCountTextBox.Text = string.Empty;
            }
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

            int count = _activeGroups.Count > 0 ? _activeGroups.Values.Max() : 1;

            _activeGroups.Add(name, count);

            NewGroupNameTextBox.Text = string.Empty;
            RefreshGroupsComboBox();
            GroupsComboBox.SelectedItem = name;
            RebuildDataTable();
        }

        private void UpdateGroupTaskCount_Click(object sender, RoutedEventArgs e)
        {
            if (GroupsComboBox.SelectedItem is string name && _activeGroups.ContainsKey(name))
            {
                if (int.TryParse(EditGroupTaskCountTextBox.Text.Trim(), out int count) && count > 0 && count <= 100)
                {
                    _activeGroups[name] = count;
                    RebuildDataTable();
                }
                else
                {
                    MessageBox.Show("Wpisz poprawną liczbę zadań (1-100).", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
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
            if (HasGroupsCheckBox != null && HasGroupsCheckBox.IsChecked == false && this.IsLoaded)
            {
                RebuildDataTable();
            }
        }

        private void SaveBackup()
        {
            if (_dataTable == null || _dataTable.Columns.Count == 0) return;
            try { TasksDataGrid.CommitEdit(DataGridEditingUnit.Row, true); } catch { }

            foreach (DataRow row in _dataTable.Rows)
            {
                int tNum = (int)row["TaskNumber"];
                
                foreach (var group in _activeGroups)
                {
                    string l1Col = $"{group.Key}_L1";
                    if (_dataTable.Columns.Contains(l1Col))
                    {
                        if (!_backup.ContainsKey(group.Key)) _backup[group.Key] = new Dictionary<int, Tuple<string, string, string>>();
                        
                        string l1 = row[l1Col]?.ToString() ?? "";
                        string l2 = row[$"{group.Key}_L2"]?.ToString() ?? "";
                        string l3 = row[$"{group.Key}_L3"]?.ToString() ?? "";
                        
                        if (l1 == "-") l1 = "";
                        if (l2 == "-") l2 = "";
                        if (l3 == "-") l3 = "";

                        _backup[group.Key][tNum] = new Tuple<string, string, string>(l1, l2, l3);
                    }
                }

                if (_dataTable.Columns.Contains("BASE_L1"))
                {
                    if (!_backup.ContainsKey("BASE")) _backup["BASE"] = new Dictionary<int, Tuple<string, string, string>>();
                    
                    string l1 = row["BASE_L1"]?.ToString() ?? "";
                    string l2 = row["BASE_L2"]?.ToString() ?? "";
                    string l3 = row["BASE_L3"]?.ToString() ?? "";
                    
                    if (l1 == "-") l1 = "";
                    if (l2 == "-") l2 = "";
                    if (l3 == "-") l3 = "";

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

            // Zmieniono na TasksDataGrid.FindResource, aby poprawnie odnaleźć styl przypisany do siatki danych
            Style readOnlyStyle = (Style)TasksDataGrid.FindResource("ReadOnlyCellStyle");

            TasksDataGrid.Columns.Add(new DataGridTextColumn { 
                Header = "Zad.", 
                Binding = new Binding("TaskNumber"), 
                IsReadOnly = true,
                CellStyle = readOnlyStyle,
                Width = new DataGridLength(45)
            });

            int maxTasks = 0;

            if (HasGroupsCheckBox.IsChecked == true)
            {
                maxTasks = _activeGroups.Count > 0 ? _activeGroups.Values.Max() : 0;
                
                foreach (var group in _activeGroups.Keys.OrderBy(k => k))
                {
                    _dataTable.Columns.Add($"{group}_L1", typeof(string));
                    _dataTable.Columns.Add($"{group}_L2", typeof(string));
                    _dataTable.Columns.Add($"{group}_L3", typeof(string));

                    // Szerokość zmieniona na Star z Minimum, żeby zawsze wypełniało przestrzeń i nigdy nie ścinało tekstu
                    TasksDataGrid.Columns.Add(new DataGridTextColumn { Header = $"{group} Poz I", Binding = new Binding($"{group}_L1"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 55 });
                    TasksDataGrid.Columns.Add(new DataGridTextColumn { Header = $"{group} Poz II", Binding = new Binding($"{group}_L2"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 55 });
                    TasksDataGrid.Columns.Add(new DataGridTextColumn { Header = $"{group} Poz III", Binding = new Binding($"{group}_L3"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 55 });
                }
            }
            else
            {
                int.TryParse(TaskCountTextBox.Text.Trim(), out maxTasks);
                if (maxTasks <= 0) return;

                _dataTable.Columns.Add("BASE_L1", typeof(string));
                _dataTable.Columns.Add("BASE_L2", typeof(string));
                _dataTable.Columns.Add("BASE_L3", typeof(string));

                TasksDataGrid.Columns.Add(new DataGridTextColumn { Header = "Poz I", Binding = new Binding("BASE_L1"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 55 });
                TasksDataGrid.Columns.Add(new DataGridTextColumn { Header = "Poz II", Binding = new Binding("BASE_L2"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 55 });
                TasksDataGrid.Columns.Add(new DataGridTextColumn { Header = "Poz III", Binding = new Binding("BASE_L3"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 55 });
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
                            else if (_backup.ContainsKey("BASE") && _backup["BASE"].ContainsKey(i))
                            {
                                row[$"{group.Key}_L1"] = _backup["BASE"][i].Item1;
                                row[$"{group.Key}_L2"] = _backup["BASE"][i].Item2;
                                row[$"{group.Key}_L3"] = _backup["BASE"][i].Item3;
                            }
                        }
                        else
                        {
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
                    else if (_activeGroups.Count > 0 && _backup.ContainsKey(_activeGroups.Keys.First()) && _backup[_activeGroups.Keys.First()].ContainsKey(i))
                    {
                        string firstGroup = _activeGroups.Keys.First();
                        row["BASE_L1"] = _backup[firstGroup][i].Item1;
                        row["BASE_L2"] = _backup[firstGroup][i].Item2;
                        row["BASE_L3"] = _backup[firstGroup][i].Item3;
                    }
                }

                _dataTable.Rows.Add(row);
            }

            TasksDataGrid.ItemsSource = _dataTable.DefaultView;
            TasksDataGrid.UpdateLayout(); 
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
                    if (_activeGroups.ContainsKey(groupName) && taskNum > _activeGroups[groupName])
                    {
                        e.Cancel = true;
                    }
                }
            }
        }

        private void TasksDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Tab)
            {
                var dir = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next;
                if (Keyboard.FocusedElement is UIElement element)
                {
                    element.MoveFocus(new TraversalRequest(dir));
                    e.Handled = true;
                }
            }
            
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
            TasksDataGrid.CommitEdit(DataGridEditingUnit.Row, true);

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
                MessageBox.Show("Wartość Max musi być prawidłową liczbą większą od zera.", "Ajajajaj! Błąd...", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                            string l1 = _dataTable.Rows[i - 1][$"{group.Key}_L1"]?.ToString() ?? "";
                            string l2 = _dataTable.Rows[i - 1][$"{group.Key}_L2"]?.ToString() ?? "";
                            string l3 = _dataTable.Rows[i - 1][$"{group.Key}_L3"]?.ToString() ?? "";

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
                        string l1 = _dataTable.Rows[i - 1]["BASE_L1"]?.ToString() ?? "";
                        string l2 = _dataTable.Rows[i - 1]["BASE_L2"]?.ToString() ?? "";
                        string l3 = _dataTable.Rows[i - 1]["BASE_L3"]?.ToString() ?? "";

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