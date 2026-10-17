using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GradebookApp.Services;

namespace GradebookApp
{
    public partial class TemplateManagerWindow : Window
    {
        private readonly GradebookDataService _dataService;
        private WorkTemplate? _selectedTemplate;
        private List<WorkTemplate> _allTemplates = new List<WorkTemplate>();

        private ObservableCollection<string> _groups = new ObservableCollection<string>();
        private Dictionary<string, ObservableCollection<TaskRow>> _divergedData = new Dictionary<string, ObservableCollection<TaskRow>>();
        public ObservableCollection<TaskRow> CurrentTasks { get; set; } = new ObservableCollection<TaskRow>();
        
        private bool _isInitializing = true;
        private bool _isUpdatingText = false; // Flaga blokująca pętle zdarzeń

        public TemplateManagerWindow()
        {
            InitializeComponent();
            _dataService = new GradebookDataService();
            LoadTemplates();
            ClearForm();
        }

        private void LoadTemplates()
        {
            _allTemplates = _dataService.GetAllTemplates();
            // Usunięto sztuczne wywoływanie SearchTemplateTextBox_TextChanged, 
            // aby lista nie otwierała się bez powodu od razu po włączeniu okna.
        }

        private void SearchTemplateTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SearchTemplateTextBox == null || _isUpdatingText) return;
            string q = SearchTemplateTextBox.Text.Trim().ToLower();

            // Zamykaj popup, gdy formularz wyszukiwania jest pusty
            if (string.IsNullOrEmpty(q))
            {
                SearchResultsPopup.IsOpen = false;
                return;
            }
            
            var filtered = _allTemplates.Where(t => t.Title.ToLower().Contains(q))
                .OrderBy(t => t.WorkType)
                .ThenBy(t => t.Title)
                .ToList();

            if (filtered.Any())
            {
                PopupTemplatesListBox.ItemsSource = filtered;
                SearchResultsPopup.IsOpen = true;
            }
            else
            {
                SearchResultsPopup.IsOpen = false;
            }
        }

        private void PopupTemplatesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || PopupTemplatesListBox.SelectedItem == null) return;
            
            if (PopupTemplatesListBox.SelectedItem is WorkTemplate t)
            {
                _selectedTemplate = t;
                
                // Przepisz tytuł do wyszukiwarki, ale nie odpalaj znowu wyszukiwania
                _isUpdatingText = true;
                SearchTemplateTextBox.Text = t.Title;
                _isUpdatingText = false;

                SearchResultsPopup.IsOpen = false;
                PopupTemplatesListBox.SelectedItem = null; // Zresetuj selekcję dla kolejnych poszukiwań
                
                ApplyTemplateToForm(t);
                
                DeleteButton.Visibility = Visibility.Visible;
                SaveAsCopyButton.Visibility = Visibility.Visible;
                
                FormTitleTextBlock.Text = "Edycja szablonu:";
                FormTitleTextBlock.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFB74D"));
            }
        }

        private void AddNewTemplate_Click(object sender, RoutedEventArgs e)
        {
            _isUpdatingText = true;
            SearchTemplateTextBox.Clear();
            _isUpdatingText = false;
            SearchResultsPopup.IsOpen = false;
            
            ClearForm();
        }

        private void ClearForm()
        {
            _isInitializing = true;
            _selectedTemplate = null;
            
            DeleteButton.Visibility = Visibility.Collapsed;
            if (SaveAsCopyButton != null) SaveAsCopyButton.Visibility = Visibility.Collapsed;
            
            FormTitleTextBlock.Text = "Nowy szablon:";
            FormTitleTextBlock.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4DB6AC"));
            
            TitleTextBox.Clear(); MaxPointsTextBox.Clear(); WorkTypeComboBox.SelectedIndex = 0;

            _groups.Clear(); _groups.Add("A"); _groups.Add("B");
            _divergedData.Clear();
            _divergedData["A"] = new ObservableCollection<TaskRow> { new TaskRow { TaskNumber = 1 }, new TaskRow { TaskNumber = 2 } };
            _divergedData["B"] = new ObservableCollection<TaskRow> { new TaskRow { TaskNumber = 1 }, new TaskRow { TaskNumber = 2 } };
            
            ApplyToAllCheckBox.IsChecked = true;
            CurrentTasks = _divergedData["A"];
            
            GroupsComboBox.ItemsSource = _groups; GroupsComboBox.SelectedIndex = 0;
            TasksDataGrid.ItemsSource = CurrentTasks;
            TaskCountTextBox.Text = "2";
            
            _isInitializing = false;
        }

        private void ApplyTemplateToForm(WorkTemplate t)
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
            
            if (_selectedTemplate == null)
            {
                var newTemplate = new WorkTemplate { Title = title, WorkType = type, MaxFinalPoints = m, HasGroups = _groups.Count > 1, Tasks = templateTasks };
                _dataService.AddTemplate(newTemplate);
            }
            else
            {
                _dataService.UpdateTemplate(_selectedTemplate, templateTasks, title, m, type, _groups.Count > 1);
            }

            MessageBox.Show("Szablon został pomyślnie zapisany.", "Sukces", MessageBoxButton.OK, MessageBoxImage.Information);
            LoadTemplates();
            ClearForm();
        }

        private void SaveAsCopy_Click(object sender, RoutedEventArgs e)
        {
            _selectedTemplate = null; 
            Save_Click(sender, e);    
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTemplate != null)
            {
                if (MessageBox.Show($"Czy na pewno chcesz bezpowrotnie usunąć szablon '{_selectedTemplate.Title}'?", "Potwierdzenie", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    _dataService.DeleteTemplate(_selectedTemplate.Id);
                    LoadTemplates();
                    ClearForm();
                }
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
        protected override void OnClosed(EventArgs e) { _dataService.Dispose(); base.OnClosed(e); }
    }
}