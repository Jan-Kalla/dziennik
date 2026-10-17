using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace GradebookApp
{
    public class ConflictViewModel : INotifyPropertyChanged
    {
        public int ScoreId { get; set; }
        public string StudentName { get; set; } = "";
        public string TaskInfo { get; set; } = "";
        public double OldMax { get; set; }
        public double NewMax { get; set; }
        public double? CurrentPoints { get; set; }
        
        private double? _newPoints;
        public double? NewPoints 
        { 
            get => _newPoints; 
            set { _newPoints = value; OnPropertyChanged(); } 
        }

        public bool IsRetake { get; set; }
        public int Level { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class WorkEditConflictsWindow : Window
    {
        public ObservableCollection<ConflictViewModel> Conflicts { get; set; }

        public WorkEditConflictsWindow(List<ConflictViewModel> conflicts)
        {
            InitializeComponent();
            Conflicts = new ObservableCollection<ConflictViewModel>(conflicts);
            ConflictsDataGrid.ItemsSource = Conflicts;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ConflictsDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
            
            // Szybka walidacja przed przepuszczeniem zapisu
            foreach(var c in Conflicts)
            {
                if (c.NewPoints.HasValue && (c.NewPoints < 0 || c.NewPoints > c.NewMax))
                {
                    MessageBox.Show($"Uczeń {c.StudentName} ({c.TaskInfo}) ma nieprawidłową liczbę punktów ({c.NewPoints}). Upewnij się, że nie przekracza Nowego Max ({c.NewMax}).", "Błąd walidacji", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}