using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GradebookApp
{
    public partial class TemplateSelectionWindow : Window
    {
        private List<WorkTemplate> _allTemplates;
        public WorkTemplate? SelectedTemplate { get; private set; }

        public TemplateSelectionWindow(List<WorkTemplate> templates)
        {
            InitializeComponent();
            _allTemplates = templates;
            FilterTemplates(); 
        }

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            FilterTemplates();
        }

        private void FilterTemplates()
        {
            if (_allTemplates == null || TemplatesListBox == null) return;

            string query = SearchTextBox.Text.Trim().ToLower();
            string typeFilter = (TypeFilterComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Wszystkie";

            var filtered = _allTemplates.AsQueryable();

            if (!string.IsNullOrEmpty(query))
            {
                filtered = filtered.Where(t => t.Title.ToLower().Contains(query));
            }

            if (typeFilter != "Wszystkie")
            {
                filtered = filtered.Where(t => t.WorkType == typeFilter);
            }

            TemplatesListBox.ItemsSource = filtered.OrderBy(t => t.WorkType).ThenBy(t => t.Title).ToList();
        }

        // NOWOŚĆ: Logika zasilająca dynamiczny podgląd na dole ekranu
        private void TemplatesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TemplatesListBox.SelectedItem is WorkTemplate t)
            {
                PreviewMaxPointsText.Text = $"Max punktów: {t.MaxFinalPoints}";
                PreviewTypeText.Text = $"Kategoria: {t.WorkType}";
                
                int groupsCount = t.Tasks.Select(task => task.GroupName ?? "A").Distinct().Count();
                PreviewGroupsText.Text = t.HasGroups ? $"Liczba grup: {groupsCount}" : "Liczba grup: Brak (1)";
                
                int tasksCount = t.Tasks.Select(task => task.TaskNumber).Distinct().Count();
                PreviewTasksText.Text = $"Liczba zadań: {tasksCount}";
            }
            else
            {
                PreviewMaxPointsText.Text = "Max punktów: -";
                PreviewTypeText.Text = "Kategoria: -";
                PreviewGroupsText.Text = "Liczba grup: -";
                PreviewTasksText.Text = "Liczba zadań: -";
            }
        }

        private void Select_Click(object sender, RoutedEventArgs e)
        {
            ConfirmSelection();
        }

        private void TemplatesListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (TemplatesListBox.SelectedItem != null)
            {
                ConfirmSelection();
            }
        }

        private void ConfirmSelection()
        {
            if (TemplatesListBox.SelectedItem is WorkTemplate t)
            {
                SelectedTemplate = t;
                DialogResult = true;
            }
            else
            {
                MessageBox.Show("Wybierz szablon z listy.", "Informacja", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}