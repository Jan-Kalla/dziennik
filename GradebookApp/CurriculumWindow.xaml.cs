using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using GradebookApp.Services;

namespace GradebookApp
{
    public partial class CurriculumWindow : Window
    {
        private readonly GradebookDataService _dataService;
        public ObservableCollection<CurriculumItem> CurriculumItems { get; set; } = new ObservableCollection<CurriculumItem>();
        private bool _isEditMode = false;
        private CurriculumItem? _selectedItem;

        public CurriculumWindow()
        {
            InitializeComponent();
            _dataService = new GradebookDataService();
            CurriculumTreeView.ItemsSource = CurriculumItems;
            
            LoadFromDatabase();
        }

        private void LoadFromDatabase()
        {
            CurriculumItems.Clear();
            var allNodes = _dataService.GetAllCurriculumNodes();

            var roots = allNodes.Where(n => n.ParentId == null).OrderBy(n => n.OrderIndex).ThenBy(n => n.Id).ToList();
            foreach (var root in roots)
            {
                CurriculumItems.Add(BuildTree(root, allNodes));
            }
        }

        private CurriculumItem BuildTree(CurriculumNode node, List<CurriculumNode> allNodes)
        {
            bool hasChildren = allNodes.Any(n => n.ParentId == node.Id);
            
            // LOGIKA LIŚCIA: Węzeł jest liściem, jeśli nie ma dzieci ORAZ (jest Tematem [poz. 2] LUB wpisano mu ręcznie godziny [np. Sprawdzian])
            bool isLeaf = !hasChildren && (node.Level == 2 || node.Hours.HasValue);

            var item = new CurriculumItem
            {
                Id = node.Id,
                Name = node.Name,
                Hours = node.Hours,
                Level = node.Level,
                ParentId = node.ParentId,
                IsTopic = node.Level == 2,
                IsLeaf = isLeaf,
                IsEditMode = _isEditMode
            };

            if (node.Files != null)
            {
                foreach (var f in node.Files.Where(x => x.FileType == "Task")) item.TaskFiles.Add(new TopicFileDto { FileId = f.Id, FileName = f.FileName, FilePath = f.FilePath });
                foreach (var f in node.Files.Where(x => x.FileType == "Answer")) item.AnswerFiles.Add(new TopicFileDto { FileId = f.Id, FileName = f.FileName, FilePath = f.FilePath });
            }

            var children = allNodes.Where(n => n.ParentId == node.Id).OrderBy(n => n.OrderIndex).ThenBy(n => n.Id).ToList();
            foreach (var child in children)
            {
                item.Children.Add(BuildTree(child, allNodes));
            }
            
            // AUTO-SUMOWANIE GODZIN: Jeśli węzeł ma dzieci, ignorujemy jego własne godziny i sumujemy kaskadowo z dołu
            if (item.Children.Any())
            {
                item.Hours = SumHours(item);
                _dataService.UpdateCurriculumNode(item.Id, item.Name, item.Hours);
            }

            item.ForceUpdateVisibilities();
            return item;
        }

        // Rekurencyjna funkcja sumująca
        private int SumHours(CurriculumItem item)
        {
            int sum = 0;
            foreach (var child in item.Children)
            {
                if (child.Children.Any()) sum += SumHours(child);
                else sum += child.Hours ?? 0;
            }
            return sum;
        }

        private void ToggleEditMode_Click(object sender, RoutedEventArgs e)
        {
            _isEditMode = !_isEditMode;
            
            if (_isEditMode)
            {
                EditToggleBtn.Content = "Wyjdź z trybu edycji";
                EditToggleBtn.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(161, 59, 59));
                EditPanel.Visibility = Visibility.Visible;
            }
            else
            {
                EditToggleBtn.Content = "Edytuj";
                EditToggleBtn.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 122, 204));
                EditPanel.Visibility = Visibility.Collapsed;
            }

            UpdateEditModeGlobally(CurriculumItems, _isEditMode);
        }

        private void UpdateEditModeGlobally(IEnumerable<CurriculumItem> items, bool isEdit)
        {
            foreach(var item in items) {
                item.IsEditMode = isEdit;
                UpdateEditModeGlobally(item.Children, isEdit);
            }
        }

        private void CurriculumTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            _selectedItem = e.NewValue as CurriculumItem;
            
            if (_selectedItem != null)
            {
                EditNameTextBox.Text = _selectedItem.Name;
                
                // Odblokowujemy edycję godzin dla KAŻDEGO węzła, który nie ma jeszcze podtematów
                bool hasChildren = _selectedItem.Children.Any();
                if (hasChildren)
                {
                    EditHoursTextBox.Text = "";
                    EditHoursTextBox.IsEnabled = false;
                    EditHoursTextBox.ToolTip = "Godziny są sumowane automatycznie z elementów podrzędnych";
                }
                else
                {
                    EditHoursTextBox.Text = _selectedItem.Hours?.ToString() ?? "";
                    EditHoursTextBox.IsEnabled = true;
                    EditHoursTextBox.ToolTip = "Wpisz godziny, aby zamienić węzeł w temat/sprawdzian (liść) i dodać pliki";
                }
                
                AddChildBtn.IsEnabled = _selectedItem.Level < 2; 
                AddSiblingBtn.IsEnabled = true;
                DeleteNodeBtn.IsEnabled = true;
                MoveUpBtn.IsEnabled = true;     // <-- NOWE
                MoveDownBtn.IsEnabled = true;   // <-- NOWE
            }
            else
            {
                EditNameTextBox.Text = "";
                EditHoursTextBox.Text = "";
                EditHoursTextBox.IsEnabled = true;
                AddChildBtn.IsEnabled = false;
                AddSiblingBtn.IsEnabled = false;
                DeleteNodeBtn.IsEnabled = false;
                MoveUpBtn.IsEnabled = false;    // <-- NOWE
                MoveDownBtn.IsEnabled = false;  // <-- NOWE
            }
        }

        private void SaveNodeEdit_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null) return;
            string newName = string.IsNullOrWhiteSpace(EditNameTextBox.Text) ? "Nowy element" : EditNameTextBox.Text;
            
            // Zapisujemy ręczne godziny tylko, jeśli pole jest aktywne (węzeł nie ma dzieci)
            bool hasChildren = _selectedItem.Children.Any();
            int? newHours = !hasChildren ? (int.TryParse(EditHoursTextBox.Text, out int h) ? h : null) : _selectedItem.Hours;

            _dataService.UpdateCurriculumNode(_selectedItem.Id, newName, newHours);
            LoadFromDatabase(); 
        }

        private void AddRoot_Click(object sender, RoutedEventArgs e) { _dataService.AddCurriculumNode("Nowy Dział", null, 0, null); LoadFromDatabase(); }
        private void AddChild_Click(object sender, RoutedEventArgs e) { if (_selectedItem == null || _selectedItem.Level >= 2) return; string prefix = _selectedItem.Level == 0 ? "Rozdział/Sprawdzian" : "Temat"; _dataService.AddCurriculumNode($"Nowy {prefix}", null, _selectedItem.Level + 1, _selectedItem.Id); LoadFromDatabase(); }
        private void AddSibling_Click(object sender, RoutedEventArgs e) { if (_selectedItem == null) return; string prefix = _selectedItem.Level == 0 ? "Dział" : (_selectedItem.Level == 1 ? "Rozdział" : "Temat"); _dataService.AddCurriculumNode($"Nowy {prefix}", null, _selectedItem.Level, _selectedItem.ParentId); LoadFromDatabase(); }

        private void DeleteNode_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null) return;
            if (MessageBox.Show($"Czy na pewno usunąć '{_selectedItem.Name}' oraz całą jego zawartość?", "Usuwanie", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                _dataService.DeleteCurriculumNode(_selectedItem.Id);
                LoadFromDatabase();
            }
        }
        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null) return;
            _dataService.MoveCurriculumNode(_selectedItem.Id, true);
            LoadFromDatabase();
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null) return;
            _dataService.MoveCurriculumNode(_selectedItem.Id, false);
            LoadFromDatabase();
        }

        private void TopicTextBlock_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is TextBlock tb && tb.DataContext is CurriculumItem item)
            {
                // Reakcja na kliknięcie zadziała dla każdego liścia (Tematu oraz Sprawdzianu)
                if (item.IsLeaf)
                {
                    var allFiles = item.TaskFiles.Concat(item.AnswerFiles).ToList();
                    
                    if (!_isEditMode && allFiles.Count == 1)
                    {
                        var fileToOpen = allFiles.First();
                        if (System.IO.File.Exists(fileToOpen.FilePath))
                        {
                            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fileToOpen.FilePath) { UseShellExecute = true }); }
                            catch { MessageBox.Show("Nie można otworzyć pliku. Brak domyślnego programu PDF w systemie.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning); }
                        }
                        else MessageBox.Show("Ten plik został fizycznie usunięty z dysku komputera.", "Brak pliku", MessageBoxButton.OK, MessageBoxImage.Information);
                        
                        e.Handled = true; 
                        return;
                    }

                    item.AreFilesExpanded = !item.AreFilesExpanded;
                }
            }
        }

        private void AddPdfButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int nodeId)
            {
                string pdfType = btn.CommandParameter?.ToString() ?? "";
                var openFileDialog = new Microsoft.Win32.OpenFileDialog { Filter = "Pliki PDF (*.pdf)|*.pdf", Title = "Dodaj plik", Multiselect = false };
                
                if (openFileDialog.ShowDialog() == true)
                {
                    try
                    {
                        string targetFolder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Pdfs");
                        System.IO.Directory.CreateDirectory(targetFolder);
                        
                        string originalFileName = System.IO.Path.GetFileName(openFileDialog.FileName);
                        string fileNameToSave = $"{Guid.NewGuid()}_{originalFileName}";
                        string destPath = System.IO.Path.Combine(targetFolder, fileNameToSave);
                        
                        System.IO.File.Copy(openFileDialog.FileName, destPath);
                        
                        _dataService.AddTopicFile(nodeId, pdfType, originalFileName, destPath);
                        LoadFromDatabase(); 
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Błąd podczas kopiowania pliku: {ex.Message}", "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void FileButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is TopicFileDto fileDto)
            {
                if (_isEditMode)
                {
                    if (MessageBox.Show($"Czy na pewno odpiąć plik '{fileDto.FileName}'?", "Usuwanie pliku", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        _dataService.DeleteTopicFile(fileDto.FileId);
                        LoadFromDatabase();
                    }
                }
                else
                {
                    if (System.IO.File.Exists(fileDto.FilePath))
                    {
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fileDto.FilePath) { UseShellExecute = true }); }
                        catch { MessageBox.Show("Nie można otworzyć pliku. Brak domyślnego programu PDF.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning); }
                    }
                    else MessageBox.Show("Ten plik został fizycznie usunięty z dysku komputera.", "Brak pliku", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        protected override void OnClosed(EventArgs e) { _dataService.Dispose(); base.OnClosed(e); }
    }

    public class TopicFileDto
    {
        public int FileId { get; set; }
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
    }

    public class CurriculumItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null!) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int? Hours { get; set; }
        public bool IsTopic { get; set; }
        public bool IsLeaf { get; set; } 
        public int Level { get; set; } 
        public int? ParentId { get; set; }

        private bool _isEditMode;
        public bool IsEditMode 
        {
            get => _isEditMode;
            set { 
                _isEditMode = value; 
                OnPropertyChanged(); 
                ForceUpdateVisibilities();
            }
        }
        
        private bool _areFilesExpanded = false;
        public bool AreFilesExpanded 
        {
            get => _areFilesExpanded;
            set { 
                _areFilesExpanded = value; 
                OnPropertyChanged(); 
                ForceUpdateVisibilities();
            }
        }
        
        public Visibility EditModeVisibility => _isEditMode ? Visibility.Visible : Visibility.Collapsed;
        public Visibility TaskRowVisibility => (_isEditMode || (_areFilesExpanded && TaskFiles.Any())) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility AnswerRowVisibility => (_isEditMode || (_areFilesExpanded && AnswerFiles.Any())) ? Visibility.Visible : Visibility.Collapsed;

        public ObservableCollection<TopicFileDto> TaskFiles { get; set; } = new ObservableCollection<TopicFileDto>();
        public ObservableCollection<TopicFileDto> AnswerFiles { get; set; } = new ObservableCollection<TopicFileDto>();

        public ObservableCollection<CurriculumItem> Children { get; set; } = new ObservableCollection<CurriculumItem>();
        
        public string DisplayText => Hours.HasValue && Hours.Value > 0 ? $"{Name} ({Hours})" : Name;

        public void ForceUpdateVisibilities()
        {
            OnPropertyChanged(nameof(EditModeVisibility));
            OnPropertyChanged(nameof(TaskRowVisibility));
            OnPropertyChanged(nameof(AnswerRowVisibility));
        }
    }
}