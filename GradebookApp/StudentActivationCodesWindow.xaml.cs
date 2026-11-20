using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using GradebookApp.Services;
using Microsoft.Win32;

namespace GradebookApp
{
    public class StudentActivationViewModel
    {
        public int JournalNumber { get; set; }
        public string StudentName { get; set; } = string.Empty;
        public string ActivationCode { get; set; } = string.Empty;
        public string Status { get; set; } = "active";
        public DateTime? ActivatedAt { get; set; }
        public string DeviceId { get; set; } = string.Empty;

        public string StatusText => Status switch
        {
            "used" => "Aktywowany na telefonie",
            "revoked" => "Unieważniony",
            "active" when !string.IsNullOrWhiteSpace(ActivationCode) => "Gotowy (oczekuje na ucznia)",
            _ => "Brak kodu"
        };

        public string StatusColor => Status switch
        {
            "used" => "#4CAF50",
            "revoked" => "#F44336",
            "active" when !string.IsNullOrWhiteSpace(ActivationCode) => "#00E5FF",
            _ => "#888888"
        };

        public string ActivatedAtText => ActivatedAt.HasValue
            ? ActivatedAt.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
            : "-";
    }

    public partial class StudentActivationCodesWindow : Window
    {
        private readonly CloudSyncService _syncService;
        public ObservableCollection<StudentActivationViewModel> StudentsList { get; set; } = new ObservableCollection<StudentActivationViewModel>();

        public StudentActivationCodesWindow()
        {
            InitializeComponent();
            _syncService = new CloudSyncService();
            CodesDataGrid.ItemsSource = StudentsList;
            LoadClasses();
        }

        private void LoadClasses()
        {
            using var db = new AppDbContext();
            var classes = db.Classes.OrderBy(c => c.Name).ToList();
            ClassComboBox.ItemsSource = classes;
            if (classes.Any())
            {
                ClassComboBox.SelectedIndex = 0;
            }
        }

        private async void ClassComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            await RefreshCodesAsync();
        }

        private async void RefreshCodes_Click(object sender, RoutedEventArgs e)
        {
            await RefreshCodesAsync();
        }

        private async Task RefreshCodesAsync()
        {
            if (ClassComboBox.SelectedItem is not SchoolClass selectedClass) return;

            StatusTextBlock.Text = $"Pobieranie kodów dla klasy {selectedClass.Name}...";
            StudentsList.Clear();

            try
            {
                using var db = new AppDbContext();
                var dbStudents = db.Students
                    .Where(s => s.SchoolClassId == selectedClass.Id)
                    .OrderBy(s => s.JournalNumber)
                    .ToList();

                List<CloudStudentActivation> cloudActivations = new List<CloudStudentActivation>();
                if (_syncService.IsConfigured)
                {
                    cloudActivations = await _syncService.GetStudentActivationsAsync(selectedClass.Name);
                }

                foreach (var student in dbStudents)
                {
                    var activation = cloudActivations.FirstOrDefault(a => a.JournalNumber == student.JournalNumber);
                    StudentsList.Add(new StudentActivationViewModel
                    {
                        JournalNumber = student.JournalNumber,
                        StudentName = student.FullName,
                        ActivationCode = activation?.ActivationCode ?? "-",
                        Status = activation?.Status ?? "none",
                        ActivatedAt = activation?.ActivatedAt,
                        DeviceId = activation?.DeviceId ?? string.Empty
                    });
                }

                int activeCount = StudentsList.Count(s => !string.IsNullOrEmpty(s.ActivationCode) && s.ActivationCode != "-");
                int usedCount = StudentsList.Count(s => s.Status == "used");
                StatusTextBlock.Text = $"Klasa {selectedClass.Name}: {StudentsList.Count} uczniów | Kodów: {activeCount} | Aktywowanych telefonów: {usedCount}";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Błąd: {ex.Message}";
                MessageBox.Show($"Błąd podczas pobierania kodów z chmury: {ex.Message}", "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void GenerateForClass_Click(object sender, RoutedEventArgs e)
        {
            if (ClassComboBox.SelectedItem is not SchoolClass selectedClass) return;

            var result = MessageBox.Show(
                $"Czy chcesz wygenerować kody aktywacyjne dla wszystkich uczniów klasy {selectedClass.Name}?\nIstniejące aktywowane telefony zostaną zachowane, a uczniom bez kodu zostanie przypisany nowy.",
                "Generowanie kodów MatApp",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            StatusTextBlock.Text = "Generowanie i wysyłanie kodów do chmury...";
            try
            {
                await _syncService.GenerateActivationCodesForClassAsync(selectedClass.Id, selectedClass.Name);
                await RefreshCodesAsync();
                MessageBox.Show($"Pomyślnie wygenerowano i zapisano kody dla klasy {selectedClass.Name} w chmurze!", "Gotowe", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Błąd generowania: {ex.Message}";
                MessageBox.Show($"Błąd zapisu w chmurze: {ex.Message}", "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RegenerateSingleCode_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not StudentActivationViewModel item) return;
            if (ClassComboBox.SelectedItem is not SchoolClass selectedClass) return;

            var res = MessageBox.Show(
                $"Czy na pewno chcesz wygenerować NOWY kod dla ucznia:\n#{item.JournalNumber} {item.StudentName}?\n\nUWAGA: Poprzedni kod i przypisanie do urządzenia zostaną natychmiast unieważnione!",
                "Unieważnienie i nowy kod",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res != MessageBoxResult.Yes) return;

            try
            {
                var newRec = await _syncService.RegenerateActivationCodeForStudentAsync(selectedClass.Name, item.JournalNumber, item.StudentName);
                item.ActivationCode = newRec.ActivationCode;
                item.Status = "active";
                item.ActivatedAt = null;
                item.DeviceId = string.Empty;

                await RefreshCodesAsync();
                MessageBox.Show($"Wygenerowano nowy kod dla #{item.JournalNumber} {item.StudentName}:\n{newRec.ActivationCode}\n\nStary kod stracił ważność.", "Sukces", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Błąd regeneracji kodu: {ex.Message}", "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CopyListToClipboard_Click(object sender, RoutedEventArgs e)
        {
            if (ClassComboBox.SelectedItem is not SchoolClass selectedClass || !StudentsList.Any()) return;

            var sb = new StringBuilder();
            sb.AppendLine($"--- KODY AKTYWACYJNE MATAPP - KLASA {selectedClass.Name} ---");
            sb.AppendLine($"Data wygenerowania: {DateTime.Now:dd.MM.yyyy HH:mm}");
            sb.AppendLine();

            foreach (var s in StudentsList)
            {
                sb.AppendLine($"Nr {s.JournalNumber:D2}. {s.StudentName,-25} -> Kod: {s.ActivationCode}");
            }

            Clipboard.SetText(sb.ToString());
            MessageBox.Show($"Skopiowano listę kodów dla {StudentsList.Count} uczniów do schowka!\nMożesz teraz wkleić ją do wiadomości e-mail lub e-dziennika.", "Skopiowano", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ExportToCsv_Click(object sender, RoutedEventArgs e)
        {
            if (ClassComboBox.SelectedItem is not SchoolClass selectedClass || !StudentsList.Any()) return;

            var sfd = new SaveFileDialog
            {
                FileName = $"kody_matapp_{selectedClass.Name.Replace(" ", "_")}.csv",
                Filter = "Pliki CSV (*.csv)|*.csv"
            };

            if (sfd.ShowDialog() == true)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Klasa;Nr_dziennika;Imie_Nazwisko;Kod_aktywacyjny;Status;Data_aktywacji");
                foreach (var s in StudentsList)
                {
                    sb.AppendLine($"{selectedClass.Name};{s.JournalNumber};{s.StudentName};{s.ActivationCode};{s.Status};{s.ActivatedAtText}");
                }
                File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show("Wyeksportowano pomyślnie do pliku CSV!", "Zapisano", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
