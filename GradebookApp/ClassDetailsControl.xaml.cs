using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using GradebookApp.ViewModels;
using GradebookApp.Services;

namespace GradebookApp
{
    public partial class ClassDetailsControl : UserControl
    {
        private readonly GradebookDataService _dataService;
        private List<Student> _allStudents = new List<Student>();
        private List<WrittenWork> _allWorks = new List<WrittenWork>();
        private SchoolClass _currentClass = null!;

        public event EventHandler<Student>? StudentDetailsRequested;

        public ObservableCollection<Student> DisplayedStudents { get; set; } = new ObservableCollection<Student>();
        public ObservableCollection<ClassWorkViewModel> DisplayedWorks { get; set; } = new ObservableCollection<ClassWorkViewModel>();

        public ClassDetailsControl()
        {
            InitializeComponent();
            _dataService = new GradebookDataService();
            StudentsDataGrid.ItemsSource = DisplayedStudents;
            WorksDataGrid.ItemsSource = DisplayedWorks; 
        }

        public void LoadClassData(SchoolClass schoolClass)
        {
            _currentClass = schoolClass;
            ClassNameText.Text = $"Klasa: {schoolClass.Name}";
            
            _allStudents = _dataService.GetStudentsByClass(schoolClass.Id);
            _allWorks = _dataService.GetWorksByClass(schoolClass.Id);

            _dataService.RecalculateClassAverages(schoolClass.Id);

            SearchTextBox.Text = string.Empty;
            RefreshDisplayedStudents(_allStudents);
            RefreshDisplayedWorks();
            RefreshCalendar();
        }

        private void RefreshDisplayedWorks()
        {
            DisplayedWorks.Clear();
            var globalWorks = _allWorks.Where(w => !w.IsIndividual).ToList();

            foreach (var work in globalWorks)
            {
                int gradedCount = _dataService.GetGradedStudentsCountForWork(_currentClass.Id, work);
                DisplayedWorks.Add(new ClassWorkViewModel
                {
                    WorkId = work.Id, WorkType = work.WorkType, Title = work.Title, MaxFinalPoints = work.MaxFinalPoints,
                    DateWrittenDisplay = work.DateWritten?.ToString("dd.MM.yyyy") ?? "Brak",
                    DateEnteredDisplay = work.DateEntered?.ToString("dd.MM.yyyy") ?? "Brak",
                    AttendanceRatio = $"{gradedCount} / {_allStudents.Count}", OriginalWork = work
                });
            }
        }

        private void RefreshCalendar()
        {
            if (_currentClass == null) return;
            var events = new List<CalendarEventViewModel>();
            var today = DateTime.Today;

            var retakes = _dataService.GetUpcomingRetakes(_currentClass.Id, today);
            foreach (var r in retakes)
            {
                events.Add(new CalendarEventViewModel 
                { 
                    EventId = r.Id, 
                    WorkId = r.WrittenWorkId, // Przypisanie WorkId!
                    IsRetake = true, 
                    SortDate = r.Date, 
                    DateDisplay = r.Date.ToString("dd.MM.yyyy"), 
                    TimeDisplay = string.IsNullOrEmpty(r.Time) ? "-" : r.Time, 
                    WorkType = r.WrittenWork.WorkType, 
                    Title = r.WrittenWork.Title, 
                    AttendeeIds = r.Attendees.Select(a => a.StudentId).ToList() 
                });
            }

            if (ShowAllWorksCheckBox.IsChecked == true)
            {
                foreach (var w in _allWorks.Where(w => w.DateWritten >= today && !w.IsIndividual))
                {
                    events.Add(new CalendarEventViewModel 
                    { 
                        EventId = w.Id, 
                        WorkId = w.Id, // Przypisanie WorkId!
                        IsRetake = false, 
                        SortDate = w.DateWritten!.Value, 
                        DateDisplay = w.DateWritten.Value.ToString("dd.MM.yyyy"), 
                        TimeDisplay = "-", 
                        WorkType = w.WorkType, 
                        Title = w.Title 
                    });
                }
            }

            CalendarDataGrid.ItemsSource = events.OrderBy(e => e.SortDate).ToList();
        }

        private void ViewRetakeDetails_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is CalendarEventViewModel vm)
            {
                if (vm.IsRetake)
                {
                    new RetakeDetailsDialog(vm.WorkId, vm.Title, vm.AttendeeIds) { Owner = Window.GetWindow(this) }.ShowDialog();
                }
                else
                {
                    MessageBox.Show("To jest pierwszy termin dla całej klasy. Piszą go domyślnie wszyscy obecni uczniowie.", "Informacja", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void EditRetake_Click(object sender, RoutedEventArgs e)
        {
            // Bezpieczne wydobycie DataContext z ContextMenu
            if ((sender as MenuItem)?.Parent is ContextMenu contextMenu && contextMenu.PlacementTarget is Button button && button.DataContext is CalendarEventViewModel vm)
            {
                if (!vm.IsRetake) { MessageBox.Show("Możesz edytować tylko zaplanowane poprawy.", "Zablokowane", MessageBoxButton.OK, MessageBoxImage.Information); return; }
                if (new PlanRetakeDialog(_currentClass.Id, vm.EventId) { Owner = Window.GetWindow(this) }.ShowDialog() == true) RefreshCalendar();
            }
        }

        private void DeleteRetake_Click(object sender, RoutedEventArgs e)
        {
            // Bezpieczne wydobycie DataContext z ContextMenu
            if ((sender as MenuItem)?.Parent is ContextMenu contextMenu && contextMenu.PlacementTarget is Button button && button.DataContext is CalendarEventViewModel vm)
            {
                if (!vm.IsRetake) { MessageBox.Show("Z tego poziomu można usunąć tylko zaplanowane poprawy.", "Zablokowane", MessageBoxButton.OK, MessageBoxImage.Information); return; }
                if (MessageBox.Show($"Usunąć poprawę '{vm.Title}'?", "Potwierdzenie", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    _dataService.DeletePlannedRetake(vm.EventId); RefreshCalendar();
                }
            }
        }

        private void RefreshCalendar_Click(object sender, RoutedEventArgs e) => RefreshCalendar();

        private void PlanRetake_Click(object sender, RoutedEventArgs e)
        {
            if (new PlanRetakeDialog(_currentClass.Id) { Owner = Window.GetWindow(this) }.ShowDialog() == true) RefreshCalendar();
        }

        private void SwapColumns_Click(object sender, RoutedEventArgs e)
        {
            if (FirstNameCol != null && LastNameCol != null)
            {
                // 1. Zamiana kolumn w głównej tabeli u góry
                int tempIndex = FirstNameCol.DisplayIndex; 
                FirstNameCol.DisplayIndex = LastNameCol.DisplayIndex; 
                LastNameCol.DisplayIndex = tempIndex;

                // 2. Zamiana układu pól szybkiego dodawania na dole
                if (FirstNameStackPanel != null && LastNameStackPanel != null)
                {
                    // Zamiana przypisania do kolumny w siatce (Grid.Column)
                    int tempCol = Grid.GetColumn(FirstNameStackPanel);
                    Grid.SetColumn(FirstNameStackPanel, Grid.GetColumn(LastNameStackPanel));
                    Grid.SetColumn(LastNameStackPanel, tempCol);

                    // Zamiana wyrównania na boki (Right <-> Left)
                    var tempAlign = FirstNameStackPanel.HorizontalAlignment;
                    FirstNameStackPanel.HorizontalAlignment = LastNameStackPanel.HorizontalAlignment;
                    LastNameStackPanel.HorizontalAlignment = tempAlign;

                    // Zamiana marginesów, by zachować stały odstęp na samym środku
                    var tempMargin = FirstNameStackPanel.Margin;
                    FirstNameStackPanel.Margin = LastNameStackPanel.Margin;
                    LastNameStackPanel.Margin = tempMargin;

                    // 3. NAPRAWA NAWIGACJI KLAWISZEM TAB (Logiczna kolejność)
                    int tempTab = QuickAddFirstNameTextBox.TabIndex;
                    QuickAddFirstNameTextBox.TabIndex = QuickAddLastNameTextBox.TabIndex;
                    QuickAddLastNameTextBox.TabIndex = tempTab;
                }
            }
        }

        private void QuickAdd_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { e.Handled = true; QuickAddStudent_Click(null!, null!); }
        }

        private void QuickAddStudent_Click(object sender, RoutedEventArgs e)
        {
            string fName = QuickAddFirstNameTextBox.Text.Trim(), lName = QuickAddLastNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(fName) && string.IsNullOrWhiteSpace(lName)) return;

            int nextNumber = _allStudents.Any() ? _allStudents.Max(s => s.JournalNumber) + 1 : 1;
            var newStudent = _dataService.AddStudent(fName, lName, _currentClass.Id, nextNumber);
            
            _allStudents.Add(newStudent); DisplayedStudents.Add(newStudent);
            RefreshDisplayedWorks();
            StudentsDataGrid.UpdateLayout(); StudentsDataGrid.ScrollIntoView(newStudent);
            QuickAddFirstNameTextBox.Clear(); QuickAddLastNameTextBox.Clear();
            (FirstNameCol.DisplayIndex < LastNameCol.DisplayIndex ? QuickAddFirstNameTextBox : QuickAddLastNameTextBox).Focus();
        }

        private void StudentsDataGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit && e.Row.Item is Student student)
            {
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (string.IsNullOrWhiteSpace(student.FirstName) && string.IsNullOrWhiteSpace(student.LastName)) { MessageBox.Show("Puste dane.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning); LoadClassData(_currentClass); return; }
                    if (student.JournalNumber > 0 && _allStudents.Any(s => s != student && s.JournalNumber == student.JournalNumber)) { MessageBox.Show("Duplikat numerka.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning); LoadClassData(_currentClass); return; }
                    _dataService.UpdateStudent(student);
                }, DispatcherPriority.Background);
            }
        }

        private void AddWork_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WorkEditorDialog(targetClassId: _currentClass.Id) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true && dialog.SavedWork != null)
            {
                _allWorks.Add(dialog.SavedWork); 
                RefreshDisplayedWorks(); 
                RefreshCalendar();
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = SearchTextBox.Text.Trim().ToLower();
            RefreshDisplayedStudents(string.IsNullOrEmpty(query) ? _allStudents : _allStudents.Where(s => s.FullName.ToLower().Contains(query) || s.WrittenWorks.Any(w => w.Title.ToLower().Contains(query))).ToList());
        }

        private void RefreshDisplayedStudents(List<Student> students) { DisplayedStudents.Clear(); foreach (var s in students) DisplayedStudents.Add(s); }

        private void StudentDetailsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.DataContext is Student s && s.Id != 0) StudentDetailsRequested?.Invoke(this, s);
        }

        private void OpenStudentNewWindow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem m && m.DataContext is Student s && s.Id != 0)
            {
                var w = new StudentDetailsWindow(s) { Owner = Window.GetWindow(this) }; w.StudentUpdated += (st, args) => LoadClassData(_currentClass); w.ShowDialog();
            }
        }

        private void DeleteStudent_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem m && m.DataContext is Student s && s.Id != 0 && MessageBox.Show($"Usunąć ucznia {s.FullName}?", "Potwierdzenie", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                _dataService.DeleteStudent(s); _allStudents.Remove(s); RefreshDisplayedStudents(_allStudents); RefreshDisplayedWorks();
            }
        }

        private void WorkDetailsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.DataContext is ClassWorkViewModel vm && new WorkEditorDialog(editWorkId: vm.WorkId) { Owner = Window.GetWindow(this) }.ShowDialog() == true)
            {
                _allWorks = _dataService.GetWorksByClass(_currentClass.Id); RefreshDisplayedWorks(); RefreshCalendar();
            }
        }

        private void WorkResultsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.DataContext is ClassWorkViewModel vm) new WorkResultsWindow(vm.WorkId, _currentClass.Id) { Owner = Window.GetWindow(this) }.ShowDialog();
        }

        private void DeleteWork_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem m && m.DataContext is ClassWorkViewModel vm && MessageBox.Show($"Usunąć ocenę '{vm.Title}'?", "Potwierdzenie", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                _dataService.DeleteWork(vm.OriginalWork); _allWorks.Remove(vm.OriginalWork); RefreshDisplayedWorks(); RefreshCalendar();
            }
        }

        private void RefreshTable_Click(object sender, RoutedEventArgs e) { if (_currentClass != null) LoadClassData(_currentClass); }
    }
}