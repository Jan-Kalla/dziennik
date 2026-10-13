using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using GradebookApp.ViewModels;
using GradebookApp.Services;

namespace GradebookApp
{
    public partial class RetakeDetailsDialog : Window
    {
        private readonly GradebookDataService _dataService;
        private int _retakeId;

        public RetakeDetailsDialog(int retakeId, int workId, string title, List<int> attendeeIds)
        {
            InitializeComponent();
            _dataService = new GradebookDataService();
            _retakeId = retakeId;
            
            TitleTextBlock.Text = title; 

            DetailsDataGrid.ItemsSource = new ObservableCollection<RetakeStudentDetailViewModel>(_dataService.GetRetakeStudentDetails(workId, attendeeIds));
        }

        // Błyskawiczne usunięcie ucznia za pomocą Menu Kontekstowego
        private void RemoveStudentContext_Click(object sender, RoutedEventArgs e)
        {
            if (DetailsDataGrid.SelectedItem is RetakeStudentDetailViewModel vm)
            {
                // Usunięcie z bazy danych
                _dataService.RemoveStudentFromRetake(_retakeId, vm.StudentId);
                
                // Natychmiastowe usunięcie ucznia z widoku tabeli w oknie
                var list = DetailsDataGrid.ItemsSource as ObservableCollection<RetakeStudentDetailViewModel>;
                list?.Remove(vm);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        
        protected override void OnClosed(System.EventArgs e)
        {
            _dataService.Dispose();
            base.OnClosed(e);
        }
    }
}