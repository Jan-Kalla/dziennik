using System.Collections.Generic;
using System.Windows;
using GradebookApp.ViewModels;
using GradebookApp.Services;

namespace GradebookApp
{
    public partial class RetakeDetailsDialog : Window
    {
        private readonly GradebookDataService _dataService;

        public RetakeDetailsDialog(int workId, string title, List<int> attendeeIds)
        {
            InitializeComponent();
            _dataService = new GradebookDataService();
            TitleTextBlock.Text = $"Lista przypisanych do: {title}";

            DetailsDataGrid.ItemsSource = _dataService.GetRetakeStudentDetails(workId, attendeeIds);
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        
        protected override void OnClosed(System.EventArgs e)
        {
            _dataService.Dispose();
            base.OnClosed(e);
        }
    }
}