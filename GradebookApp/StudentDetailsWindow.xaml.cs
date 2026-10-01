using System;
using System.Windows;

namespace GradebookApp
{
    public partial class StudentDetailsWindow : Window
    {
        public event EventHandler? StudentUpdated;

        public StudentDetailsWindow(Student student)
        {
            InitializeComponent();
            Title = $"Dziennik - {student.FullName}";
            
            // Nasłuchiwanie na kontrolkę i przekazanie sygnału dalej do okna głównego
            WindowStudentControl.StudentUpdated += (s, e) => StudentUpdated?.Invoke(this, EventArgs.Empty);
            
            WindowStudentControl.LoadStudentData(student);
        }
    }
}