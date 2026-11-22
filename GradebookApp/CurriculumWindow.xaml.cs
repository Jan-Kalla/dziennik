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
            _dataService.ApplyMigrations();
            PdfFileResolver.EnsurePdfsConsolidated();
            CurriculumTreeView.ItemsSource = CurriculumItems;
            
            LoadFromDatabase();
        }

        private void LoadFromDatabase(int? selectedIdToRestore = null, bool scrollToBottom = false)
        {
            int? idToRestore = selectedIdToRestore ?? _selectedItem?.Id;
            CurriculumItems.Clear();
            var allNodes = _dataService.GetAllCurriculumNodes();

            var roots = allNodes.Where(n => n.ParentId == null).OrderBy(n => n.OrderIndex).ThenBy(n => n.Id).ToList();
            foreach (var root in roots)
            {
                CurriculumItems.Add(BuildTree(root, allNodes));
            }

            if (idToRestore.HasValue)
            {
                var found = FindItemById(CurriculumItems, idToRestore.Value);
                if (found != null)
                {
                    found.IsSelected = true;
                    _selectedItem = found;
                    UpdateEditPanelForSelection(found);
                }
            }

            if (scrollToBottom)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var sv = FindVisualChild<ScrollViewer>(CurriculumTreeView);
                    sv?.ScrollToBottom();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        private CurriculumItem? FindItemById(IEnumerable<CurriculumItem> items, int id)
        {
            foreach (var item in items)
            {
                if (item.Id == id) return item;
                var found = FindItemById(item.Children, id);
                if (found != null) return found;
            }
            return null;
        }

        private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
        {
            if (parent == null) return null;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null) return childOfChild;
            }
            return null;
        }

        private void CurriculumTreeView_Expanded(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is TreeViewItem tvi && tvi.DataContext is CurriculumItem item)
            {
                item.IsExpanded = true;
                _dataService.UpdateCurriculumNodeExpanded(item.Id, true);
            }
        }

        private void CurriculumTreeView_Collapsed(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is TreeViewItem tvi && tvi.DataContext is CurriculumItem item)
            {
                item.IsExpanded = false;
                _dataService.UpdateCurriculumNodeExpanded(item.Id, false);
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
                IsEditMode = _isEditMode,
                IsExpanded = node.IsExpanded
            };

            if (node.Files != null)
            {
                foreach (var f in node.Files.Where(x => x.FileType == "Task"))
                {
                    string resolved = PdfFileResolver.ResolvePdfPath(f.FilePath, f.FileName) ?? f.FilePath;
                    if (resolved != f.FilePath)
                    {
                        _dataService.UpdateTopicFilePath(f.Id, resolved);
                    }
                    item.TaskFiles.Add(new TopicFileDto { FileId = f.Id, FileName = f.FileName, FilePath = resolved });
                }

                foreach (var f in node.Files.Where(x => x.FileType == "Answer"))
                {
                    string resolved = PdfFileResolver.ResolvePdfPath(f.FilePath, f.FileName) ?? f.FilePath;
                    if (resolved != f.FilePath)
                    {
                        _dataService.UpdateTopicFilePath(f.Id, resolved);
                    }
                    item.AnswerFiles.Add(new TopicFileDto { FileId = f.Id, FileName = f.FileName, FilePath = resolved });
                }
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
            UpdateEditPanelForSelection(_selectedItem);
        }

        private void UpdateEditPanelForSelection(CurriculumItem? item)
        {
            if (item != null)
            {
                EditNameTextBox.Text = item.Name;
                
                // Odblokowujemy edycję godzin dla KAŻDEGO węzła, który nie ma jeszcze podtematów
                bool hasChildren = item.Children.Any();
                if (hasChildren)
                {
                    EditHoursTextBox.Text = "";
                    EditHoursTextBox.IsEnabled = false;
                    EditHoursTextBox.ToolTip = "Godziny są sumowane automatycznie z elementów podrzędnych";
                }
                else
                {
                    EditHoursTextBox.Text = item.Hours?.ToString() ?? "";
                    EditHoursTextBox.IsEnabled = true;
                    EditHoursTextBox.ToolTip = "Wpisz godziny, aby zamienić węzeł w temat/sprawdzian (liść) i dodać pliki";
                }
                
                AddChildBtn.IsEnabled = item.Level < 2; 
                AddSiblingBtn.IsEnabled = true;
                DeleteNodeBtn.IsEnabled = true;
                MoveUpBtn.IsEnabled = true;
                MoveDownBtn.IsEnabled = true;
            }
            else
            {
                EditNameTextBox.Text = "";
                EditHoursTextBox.Text = "";
                EditHoursTextBox.IsEnabled = true;
                AddChildBtn.IsEnabled = false;
                AddSiblingBtn.IsEnabled = false;
                DeleteNodeBtn.IsEnabled = false;
                MoveUpBtn.IsEnabled = false;
                MoveDownBtn.IsEnabled = false;
            }
        }

        private void SaveNodeEdit_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null) return;
            int id = _selectedItem.Id;
            string newName = string.IsNullOrWhiteSpace(EditNameTextBox.Text) ? "Nowy element" : EditNameTextBox.Text;
            
            // Zapisujemy ręczne godziny tylko, jeśli pole jest aktywne (węzeł nie ma dzieci)
            bool hasChildren = _selectedItem.Children.Any();
            int? newHours = !hasChildren ? (int.TryParse(EditHoursTextBox.Text, out int h) ? h : null) : _selectedItem.Hours;

            _dataService.UpdateCurriculumNode(id, newName, newHours);
            LoadFromDatabase(id); 
        }

        private void AddRoot_Click(object sender, RoutedEventArgs e) 
        { 
            var node = _dataService.AddCurriculumNode("Nowy Dział", null, 0, null); 
            LoadFromDatabase(node.Id, scrollToBottom: true); 
        }

        private void AddChild_Click(object sender, RoutedEventArgs e) 
        { 
            if (_selectedItem == null || _selectedItem.Level >= 2) return; 
            _selectedItem.IsExpanded = true;
            _dataService.UpdateCurriculumNodeExpanded(_selectedItem.Id, true);
            string prefix = _selectedItem.Level == 0 ? "Rozdział/Sprawdzian" : "Temat"; 
            var node = _dataService.AddCurriculumNode($"Nowy {prefix}", null, _selectedItem.Level + 1, _selectedItem.Id); 
            LoadFromDatabase(node.Id, scrollToBottom: true); 
        }

        private void AddSibling_Click(object sender, RoutedEventArgs e) 
        { 
            if (_selectedItem == null) return; 
            string prefix = _selectedItem.Level == 0 ? "Dział" : (_selectedItem.Level == 1 ? "Rozdział" : "Temat"); 
            var node = _dataService.AddCurriculumNode($"Nowy {prefix}", null, _selectedItem.Level, _selectedItem.ParentId); 
            LoadFromDatabase(node.Id, scrollToBottom: true); 
        }

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
            int id = _selectedItem.Id;
            _dataService.MoveCurriculumNode(id, true);
            LoadFromDatabase(id);
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null) return;
            int id = _selectedItem.Id;
            _dataService.MoveCurriculumNode(id, false);
            LoadFromDatabase(id);
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
                        string? resolvedPath = PdfFileResolver.ResolvePdfPath(fileToOpen.FilePath, fileToOpen.FileName);
                        if (!string.IsNullOrEmpty(resolvedPath) && System.IO.File.Exists(resolvedPath))
                        {
                            if (resolvedPath != fileToOpen.FilePath)
                            {
                                fileToOpen.FilePath = resolvedPath;
                                _dataService.UpdateTopicFilePath(fileToOpen.FileId, resolvedPath);
                            }
                            var viewer = new PdfViewerWindow(fileToOpen.FilePath, fileToOpen.FileName) { Owner = this };
                            viewer.Show();
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
                        string targetFolder = PdfFileResolver.GetDefaultPdfsDirectory();
                        string originalFileName = System.IO.Path.GetFileName(openFileDialog.FileName);
                        string fileNameToSave = PdfFileResolver.GenerateUniquePdfFileName(targetFolder, originalFileName);
                        string destPath = System.IO.Path.Combine(targetFolder, fileNameToSave);
                        
                        System.IO.File.Copy(openFileDialog.FileName, destPath, overwrite: true);
                        
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
                    string? resolvedPath = PdfFileResolver.ResolvePdfPath(fileDto.FilePath, fileDto.FileName);
                    if (!string.IsNullOrEmpty(resolvedPath) && System.IO.File.Exists(resolvedPath))
                    {
                        if (resolvedPath != fileDto.FilePath)
                        {
                            fileDto.FilePath = resolvedPath;
                            _dataService.UpdateTopicFilePath(fileDto.FileId, resolvedPath);
                        }
                        var viewer = new PdfViewerWindow(fileDto.FilePath, fileDto.FileName) { Owner = this };
                        viewer.Show();
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

        private bool _isExpanded = true;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

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