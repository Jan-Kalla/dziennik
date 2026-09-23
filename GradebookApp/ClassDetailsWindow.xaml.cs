using System.Windows;

namespace GradebookApp
{
    public partial class ClassDetailsWindow : Window
    {
        public ClassDetailsWindow(SchoolClass schoolClass)
        {
            InitializeComponent();
            WindowClassControl.LoadClassData(schoolClass);
            
            // Subskrypcja zdarzenia przejścia do ucznia
            WindowClassControl.StudentDetailsRequested += WindowClassControl_StudentDetailsRequested;
        }

        private void WindowClassControl_StudentDetailsRequested(object? sender, Student student)
        {
            ClassDetailsView.Visibility = Visibility.Collapsed;
            StudentDetailsView.Visibility = Visibility.Visible;
            WindowStudentControl.LoadStudentData(student);
        }

        private void BackToClassFromStudent_Click(object sender, RoutedEventArgs e)
        {
            StudentDetailsView.Visibility = Visibility.Collapsed;
            ClassDetailsView.Visibility = Visibility.Visible;
        }
    }
}