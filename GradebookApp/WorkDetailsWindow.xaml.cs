using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;

namespace GradebookApp
{
    public partial class WorkDetailsWindow : Window
    {
        // To jest nasz "kabel" do bazy danych SQLite. Przez to wysyłamy i pobieramy informacje.
        private AppDbContext _dbContext;
        
        // Zmienna, w której trzymamy aktualnie edytowaną pracę. 
        // "= null!" to sztuczka, która mówi kompilatorowi: "spokojnie, na pewno wrzucę tu dane, nie rzucaj błędami o nullach"[cite: 51].
        private WrittenWork _work = null!; 
        
        // ObservableCollection to specjalna lista stworzona specjalnie dla WPF. 
        // Jej supermocą jest to, że kiedy tylko dołożysz do niej element albo go z niej usuniesz, 
        // tabelka na ekranie natychmiast, magicznie się odświeża i pokazuje zmiany[cite: 51].
        public ObservableCollection<WrittenWorkTask> TasksCollection { get; set; } = new ObservableCollection<WrittenWorkTask>();

        public WorkDetailsWindow(int workId)
        {
            InitializeComponent();
            
            // Tworzymy nowe, świeżutkie połączenie z bazą specjalnie i wyłącznie dla tego okienka[cite: 51].
            _dbContext = new AppDbContext();
            
            // Wyciągamy naszą konkretną pracę z bazy. Używamy funkcji Include(), żeby od razu "zassać"
            // z nią przypisane do niej zadania. Inaczej Entity Framework dałby nam samą pracę, bez zadań w środku[cite: 51].
            _work = _dbContext.WrittenWorks.Include(w => w.Tasks).FirstOrDefault(w => w.Id == workId)!;

            if (_work != null)
            {
                // Krok 1: Wrzucamy dane z bazy prosto do kontrolek na ekranie (teksty, daty)[cite: 51]
                TitleTextBox.Text = _work.Title;
                DateWrittenPicker.SelectedDate = _work.DateWritten;
                DateEnteredPicker.SelectedDate = _work.DateEntered;
                
                // Ładujemy zapisaną w bazie zmienną M, żeby nauczyciel od razu widział stary próg.
                MaxPointsTextBox.Text = _work.MaxFinalPoints.ToString();

                // Krok 2: Szukamy odpowiedniego rodzaju pracy na liście rozwijanej (ComboBox)[cite: 51].
                // Lecimy pętlą po wszystkich opcjach z listy i jak tekst opcji zgadza się z typem z bazy, to ją zaznaczamy.
                foreach (ComboBoxItem item in WorkTypeComboBox.Items)
                {
                    if (item.Content.ToString() == _work.WorkType)
                    {
                        WorkTypeComboBox.SelectedItem = item;
                        break;
                    }
                }

                // Krok 3: Pakujemy zadania z bazy do naszej interaktywnej listy i sortujemy ładnie rosnąco po ich numerze[cite: 51].
                TasksCollection = new ObservableCollection<WrittenWorkTask>(_work.Tasks.OrderBy(t => t.TaskNumber));
                
                // Mówimy tabelce DataGrid na ekranie: "Hej, twoim źródłem danych jest ta kolekcja"[cite: 51].
                TasksDataGrid.ItemsSource = TasksCollection;
                
                // Krok 4: Wpisujemy do okienka obecną liczbę zadań i DOPIERO TERAZ włączamy podsłuch na zmiany w tym polu[cite: 51].
                // Gdybyśmy zrobili to wcześniej, skrypt pomyślałby, że nauczyciel coś wpisał już podczas uruchamiania okna i by zgłupiał.
                TaskCountTextBox.Text = TasksCollection.Count.ToString();
                TaskCountTextBox.TextChanged += TaskCountTextBox_TextChanged;
            }
        }

        // Ta metoda odpala się ZAWSZE, dosłownie przy każdym uderzeniu w klawisz, w polu "Liczba zadań"[cite: 51].
        private void TaskCountTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // int.TryParse sprawdza, czy wpisany tekst to faktycznie poprawna liczba całkowita[cite: 51].
            // Jeśli tak, to ładuje ją do zmiennej 'count'. Od razu blokujemy też liczby ujemne i jakieś abstrakcyjne zera czy liczby > 100.
            if (int.TryParse(TaskCountTextBox.Text.Trim(), out int count) && count > 0 && count <= 100)
            {
                AdjustTasksCollection(count); // Wywołujemy menedżera, żeby ogarnął wiersze.
            }
        }

        // To jest nasz "menedżer zadań". Jego rolą jest dodawanie nowych wierszy do tabeli albo usuwanie nadmiarowych z dołu[cite: 51].
        private void AdjustTasksCollection(int targetCount)
        {
            // Scenariusz 1: Ktoś wpisał większą liczbę. Trzeba na szybko "dorobić" zadań.
            // Pętla leci tak długo, aż liczba zadań nie dobije do tego, co wpisał nauczyciel[cite: 51].
            while (TasksCollection.Count < targetCount)
            {
                var newTask = new WrittenWorkTask { TaskNumber = TasksCollection.Count + 1, WrittenWorkId = _work.Id };
                TasksCollection.Add(newTask); // Dodajemy do tabelki na ekranie...
                _dbContext.WrittenWorkTasks.Add(newTask); // ...i oznaczamy w EF Core, że to całkiem nowy wpis, który zaraz poleci do bazy[cite: 51].
            }
            
            // Scenariusz 2: Ktoś wpisał mniejszą liczbę. Ucinamy zadania od końca[cite: 51].
            while (TasksCollection.Count > targetCount)
            {
                var taskToRemove = TasksCollection.Last(); // Chwytamy za ostatni wiersz w liście[cite: 51]
                TasksCollection.Remove(taskToRemove); // Kopiemy go z interfejsu (znika z ekranu)[cite: 51]
                
                // Jeśli to zadanie nie zostało dodane przed chwilą, tylko istniało wcześniej w bazie (ma jakieś konkretne ID),
                // to musimy wydać bazie oficjalny nakaz jego usunięcia z dysku[cite: 51].
                if (taskToRemove.Id != 0) 
                {
                    _dbContext.WrittenWorkTasks.Remove(taskToRemove);
                }
            }
        }

        // Kliknięcie potężnego przycisku "Zapisz zmiany"[cite: 51].
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // MEGA WAŻNE: Kiedy wpisujesz coś w komórkę DataGrid, to pole jest cały czas "aktywne".
            // CommitEdit siłowo odklikuje się z tej komórki i wciska to, co wpisałeś do pamięci aplikacji przed samym zapisem[cite: 51].
            TasksDataGrid.CommitEdit(DataGridEditingUnit.Row, true);

            // ====================================================================
            // START WALIDACJI (Blokujemy użytkownika przed zrobieniem głupot)
            // ====================================================================

            string newTitle = TitleTextBox.Text.Trim();
            if (string.IsNullOrEmpty(newTitle))
            {
                MessageBox.Show("Podaj tytuł oceny.", "Ajajajaj! Błąd...", MessageBoxButton.OK, MessageBoxImage.Warning);
                return; // Wywalamy się z funkcji, nie ma po co iść dalej, jeśli są błędy[cite: 51].
            }

            // NOWE SPRAWDZANIE M: Sprawdzamy czy M to faktycznie poprawny ułamek dziesiętny (double) i czy jest większe od zera.
            if (!double.TryParse(MaxPointsTextBox.Text.Trim(), out double newMaxPoints) || newMaxPoints <= 0)
            {
                MessageBox.Show("Wartość Max musi być prawidłową liczbą większą od zera.", "Ajajajaj! Błąd...", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (TasksCollection.Count == 0)
            {
                MessageBox.Show("Praca pisemna musi zawierać przynajmniej jedno zadanie.", "Ajajajaj! Błąd...", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Używamy .Any() - to skaner, który szuka, czy którekolwiek zadanie na liście ma chociaż jedno niewypełnione (puste) pole[cite: 51].
            bool hasEmptyFields = TasksCollection.Any(t => 
                t.MaxPointsLevel1 == null || 
                t.MaxPointsLevel2 == null || 
                t.MaxPointsLevel3 == null);

            if (hasEmptyFields)
            {
                MessageBox.Show("Musisz wypełnić wszystkie pola punktacji. Wpisz 0, jeśli dany poziom nie jest punktowany.", 
                                "Ajajajaj! Brakujące dane...", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Skanujemy ponownie, czy ktoś dla żartu nie napisał ujemnych punktów, bo matematyka z PSO by po prostu wybuchła[cite: 51].
            bool hasNegativePoints = TasksCollection.Any(t => 
                t.MaxPointsLevel1 < 0 || t.MaxPointsLevel2 < 0 || t.MaxPointsLevel3 < 0);

            if (hasNegativePoints)
            {
                MessageBox.Show("Liczba punktów nie może być ujemna.", "Ajajajaj! Błąd wartości...", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // ====================================================================
            // KONIEC WALIDACJI. Jak program doszedł tu, to znaczy, że wpisy są super.
            // ====================================================================

            // Wrzucamy dane z okienek tekstowych i kalendarzy do naszej głównej zmiennej _work[cite: 51].
            _work.Title = newTitle;
            _work.MaxFinalPoints = newMaxPoints; // Przepisujemy nową wartość zmiennej M do obiektu pracy.
            _work.WorkType = (WorkTypeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "inne";
            _work.DateWritten = DateWrittenPicker.SelectedDate;
            _work.DateEntered = DateEnteredPicker.SelectedDate;

            // Magia Entity Framework: Odpalamy tę komendę, a on sam analizuje, co dodaliśmy, co zmieniliśmy i co usunęliśmy.
            // Następnie automatycznie układa kwerendy SQL i modyfikuje plik .db na dysku[cite: 51].
            _dbContext.SaveChanges();
            
            // To mówi okienku, żeby się zamknęło ze statusem "powodzenie" (True)[cite: 51]. 
            // Dzięki temu główny program wie, że ma odświeżyć swoją dużą tabelę z pracami.
            DialogResult = true;
        }

        // Ktoś stwierdził, że jednak rezygnuje z edycji[cite: 51]
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            // Ustawiamy status na False i po prostu uciekamy z okna. 
            // Nasz _dbContext nigdy nie odpali SaveChanges(), więc wszystko co facet wpisał w formularz zginie jak łzy w deszczu, 
            // a plik bazy danych pozostanie nienaruszony[cite: 51].
            DialogResult = false; 
        }
    }
}