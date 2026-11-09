using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
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

            var roots = allNodes.Where(n => n.ParentId == null).OrderBy(n => n.Id).ToList();
            foreach (var root in roots)
            {
                CurriculumItems.Add(BuildTree(root, allNodes));
            }
        }

        private CurriculumItem BuildTree(CurriculumNode node, System.Collections.Generic.List<CurriculumNode> allNodes)
        {
            var item = new CurriculumItem
            {
                Id = node.Id,
                Name = node.Name,
                Hours = node.Hours,
                Level = node.Level,
                ParentId = node.ParentId,
                IsTopic = node.Level == 2,
                TasksPdfPath = node.TasksPdfPath,
                AnswersPdfPath = node.AnswersPdfPath,
                QuizzesPdfPath = node.QuizzesPdfPath
            };

            var children = allNodes.Where(n => n.ParentId == node.Id).OrderBy(n => n.Id).ToList();
            foreach (var child in children)
            {
                item.Children.Add(BuildTree(child, allNodes));
            }
            return item;
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
                CurriculumTreeView.SelectedItemChanged -= CurriculumTreeView_SelectedItemChanged; 
                LoadFromDatabase(); 
                CurriculumTreeView.SelectedItemChanged += CurriculumTreeView_SelectedItemChanged;
            }
        }

        private void CurriculumTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            _selectedItem = e.NewValue as CurriculumItem;
            
            if (_selectedItem != null)
            {
                EditNameTextBox.Text = _selectedItem.Name;
                EditHoursTextBox.Text = _selectedItem.Hours?.ToString() ?? "";
                
                AddChildBtn.IsEnabled = _selectedItem.Level < 2; 
                AddSiblingBtn.IsEnabled = true;
                DeleteNodeBtn.IsEnabled = true;
            }
            else
            {
                EditNameTextBox.Text = "";
                EditHoursTextBox.Text = "";
                AddChildBtn.IsEnabled = false;
                AddSiblingBtn.IsEnabled = false;
                DeleteNodeBtn.IsEnabled = false;
            }
        }

        private void SaveNodeEdit_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null) return;
            string newName = string.IsNullOrWhiteSpace(EditNameTextBox.Text) ? "Nowy element" : EditNameTextBox.Text;
            int? newHours = int.TryParse(EditHoursTextBox.Text, out int h) ? h : null;

            _dataService.UpdateCurriculumNode(_selectedItem.Id, newName, newHours);
            LoadFromDatabase(); 
        }

        private void AddRoot_Click(object sender, RoutedEventArgs e)
        {
            _dataService.AddCurriculumNode("Nowy Dział", null, 0, null);
            LoadFromDatabase();
        }

        private void AddChild_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null || _selectedItem.Level >= 2) return;
            string prefix = _selectedItem.Level == 0 ? "Rozdział" : "Temat";
            _dataService.AddCurriculumNode($"Nowy {prefix}", null, _selectedItem.Level + 1, _selectedItem.Id);
            LoadFromDatabase();
        }

        private void AddSibling_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null) return;
            string prefix = _selectedItem.Level == 0 ? "Dział" : (_selectedItem.Level == 1 ? "Rozdział" : "Temat");
            _dataService.AddCurriculumNode($"Nowy {prefix}", null, _selectedItem.Level, _selectedItem.ParentId);
            LoadFromDatabase();
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

        private void PdfButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.DataContext is CurriculumItem item)
            {
                string pdfType = btn.CommandParameter?.ToString() ?? "";
                string? currentPath = pdfType == "Task" ? item.TasksPdfPath : (pdfType == "Answer" ? item.AnswersPdfPath : item.QuizzesPdfPath);

                if (_isEditMode)
                {
                    // 1. TRYB EDYCJI: Wgrywanie pliku
                    var openFileDialog = new Microsoft.Win32.OpenFileDialog
                    {
                        Filter = "Pliki PDF (*.pdf)|*.pdf",
                        Title = "Wybierz plik PDF z komputera"
                    };

                    if (openFileDialog.ShowDialog() == true)
                    {
                        try
                        {
                            string targetFolder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Pdfs");
                            System.IO.Directory.CreateDirectory(targetFolder);
                            string fileName = $"{Guid.NewGuid()}_{System.IO.Path.GetFileName(openFileDialog.FileName)}";
                            string destPath = System.IO.Path.Combine(targetFolder, fileName);
                            
                            System.IO.File.Copy(openFileDialog.FileName, destPath);
                            _dataService.UpdateCurriculumNodePdf(item.Id, pdfType, destPath);
                            LoadFromDatabase(); // Odświeża kolory
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Błąd podczas kopiowania pliku: {ex.Message}", "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
                else
                {
                    // 2. TRYB PODGLĄDU: Otwieranie pliku
                    if (!string.IsNullOrEmpty(currentPath) && System.IO.File.Exists(currentPath))
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(currentPath) { UseShellExecute = true });
                        }
                        catch { MessageBox.Show("Nie można otworzyć pliku. Brak domyślnego programu PDF w systemie.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning); }
                    }
                    else
                    {
                        MessageBox.Show("Do tego pola nie przypisano jeszcze żadnego pliku (lub został usunięty z dysku). Wejdź w tryb Edycji, by go dodać.", "Brak pliku", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _dataService.Dispose();
            base.OnClosed(e);
        }
    }

    public class CurriculumItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int? Hours { get; set; }
        public bool IsTopic { get; set; }
        public int Level { get; set; } 
        public int? ParentId { get; set; }
        
        public string? TasksPdfPath { get; set; }
        public string? AnswersPdfPath { get; set; }
        public string? QuizzesPdfPath { get; set; }

        public ObservableCollection<CurriculumItem> Children { get; set; } = new ObservableCollection<CurriculumItem>();

        public string DisplayText => Hours.HasValue ? $"{Name} ({Hours})" : Name;
        
        // Dynamiczne kolorowanie przycisków
        public string TasksColor => !string.IsNullOrEmpty(TasksPdfPath) ? "#2E4A2E" : "#3E3E42";
        public string AnswersColor => !string.IsNullOrEmpty(AnswersPdfPath) ? "#2E4A2E" : "#3E3E42";
        public string QuizzesColor => !string.IsNullOrEmpty(QuizzesPdfPath) ? "#2E4A2E" : "#4A2E2E";
    }
}