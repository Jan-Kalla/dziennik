using System.Collections.ObjectModel;
using System.Windows;

namespace GradebookApp
{
    public partial class CurriculumWindow : Window
    {
        public ObservableCollection<CurriculumItem> CurriculumItems { get; set; }

        public CurriculumWindow()
        {
            InitializeComponent();
            CurriculumItems = new ObservableCollection<CurriculumItem>();
            
            LoadMockData();

            CurriculumTreeView.ItemsSource = CurriculumItems;
        }

        private void LoadMockData()
        {
            // Odwzorowanie struktury z Twoich notatek (Zdjęcie 2)
            var dzial1 = new CurriculumItem { Name = "Dział 1", Hours = 6, Level = 0 };
            
            var rozdzial1 = new CurriculumItem { Name = "Rozdział 1", Hours = 5, Level = 1 };
            rozdzial1.Children.Add(new CurriculumItem { Name = "Temat 1", IsTopic = true, Level = 2 });
            rozdzial1.Children.Add(new CurriculumItem { Name = "Temat 2", IsTopic = true, Level = 2 });
            
            dzial1.Children.Add(rozdzial1);

            var dzial2 = new CurriculumItem { Name = "Dział 2", Level = 0 };
            dzial2.Children.Add(new CurriculumItem { Name = "Rozdział 2", Hours = 4, Level = 1 });
            dzial2.Children.Add(new CurriculumItem { Name = "Rozdział 3", Level = 1 });

            var dzial3 = new CurriculumItem { Name = "Dział 3", Level = 0 };

            CurriculumItems.Add(dzial1);
            CurriculumItems.Add(dzial2);
            CurriculumItems.Add(dzial3);
        }

        private void PdfButton_Click(object sender, RoutedEventArgs e)
        {
            // Tu w przyszłości: Przejście do pliku .pdf z zadaniami do danego tematu lub kartkówki.
            // w przypadku kilku plików: nowe okno z możliwością wyboru danego pliku.
            MessageBox.Show("Otwieranie przypisanego pliku PDF lub okna wyboru (jeśli jest ich więcej).", "Podgląd PDF", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void EditCurriculum_Click(object sender, RoutedEventArgs e)
        {
            // Tu w przyszłości: Tworzenie / Edycja rozkładu (np. otwieranie nowego okna edytora)
            MessageBox.Show("Tu pojawi się kreator dodawania/edycji struktury rozkładu materiału.", "Tworzenie rozkładu", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // Model danych dla struktury drzewiastej
    public class CurriculumItem
    {
        public string Name { get; set; } = string.Empty;
        public int? Hours { get; set; }
        public bool IsTopic { get; set; }
        public int Level { get; set; } // 0 = Dział, 1 = Rozdział, 2 = Temat

        public ObservableCollection<CurriculumItem> Children { get; set; } = new ObservableCollection<CurriculumItem>();

        // Właściwość automatycznie doklejająca liczbę godzin w nawiasie " (x)", jeśli zostały podane
        public string DisplayText => Hours.HasValue ? $"{Name} ({Hours})" : Name;
    }
}