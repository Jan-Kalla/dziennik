using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace GradebookApp
{
    // Dobra, to są nasze tabele w bazie danych. Entity Framework Core (EF Core) 
    // jest na tyle mądry, że sam robi z tych klas gotowe tabele w pliku SQLite.
    
    // TABELA: Klasy szkolne
    public class SchoolClass
    {
        public int Id { get; set; } // Główny klucz (ID). EF Core sam go podbija (AutoIncrement).
        public string Name { get; set; } = string.Empty; // Np. "1A"
        
        // Magia EF Core - wystarczy wrzucić tu listę (List<Student>), a on sam ogarnie 
        // relację jeden-do-wielu (jedna klasa ma wielu uczniów).
        public List<Student> Students { get; set; } = new List<Student>();
        public List<WrittenWork> WrittenWorks { get; set; } = new List<WrittenWork>();
    }

    // TABELA: Uczniowie
    // Ten interfejs INotifyPropertyChanged to taki dynks, który krzyczy do interfejsu (WPF): 
    // "Hej, moje dane się zmieniły, odśwież widok!". Przydatne przy liczeniu średniej.
    public class Student : INotifyPropertyChanged
    {
        public int Id { get; set; }
        
        private int _journalNumber;
        public int JournalNumber { get => _journalNumber; set { _journalNumber = value; OnPropertyChanged(); } }

        private string _firstName = string.Empty;
        public string FirstName { get => _firstName; set { _firstName = value; OnPropertyChanged(); OnPropertyChanged(nameof(FullName)); } }

        private string _lastName = string.Empty;
        public string LastName { get => _lastName; set { _lastName = value; OnPropertyChanged(); OnPropertyChanged(nameof(FullName)); } }

        // Zwykły skrót dla wygody, żeby nie łączyć tego ręcznie w oknach
        public string FullName => $"{FirstName} {LastName}";

        private double _averagePercentage;
        public double AveragePercentage { get => _averagePercentage; set { _averagePercentage = value; OnPropertyChanged(); } }

        // Klucz obcy - to łączy ucznia z jego klasą
        public int SchoolClassId { get; set; }
        public SchoolClass SchoolClass { get; set; } = null!;
        public List<WrittenWork> WrittenWorks { get; set; } = new List<WrittenWork>();

        // Boilerplate do odświeżania UI. Nie musisz tego dotykać, po prostu działa.
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // TABELA: Prace pisemne (ogólne info o sprawdzianie, np. temat, data)
    public class WrittenWork
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty; 
        public string WorkType { get; set; } = string.Empty; 
        
        // To jest to 'M' z dokumentacji PSO (Max punktów, które można ugrać z całego sprawdzianu)
        public double MaxFinalPoints { get; set; }

        public DateTime? DateWritten { get; set; } // Domyślna data pisania dla całej klasy
        public DateTime? DateEntered { get; set; } 

        public int SchoolClassId { get; set; }
        public SchoolClass SchoolClass { get; set; } = null!;

        public List<Student> Students { get; set; } = new List<Student>();
        public List<WrittenWorkTask> Tasks { get; set; } = new List<WrittenWorkTask>(); // Lista zadań w tym sprawdzianie
    }

    // TABELA: Zadania w sprawdzianie (1, 2, 3...)
    public class WrittenWorkTask
    {
        public int Id { get; set; }
        public int TaskNumber { get; set; } 
        
        // To jest 'P_i,j' z PSO. Czyli ile maksymalnie punktów nauczyciel przewidział za dane zadanie.
        public double? MaxPointsLevel1 { get; set; }
        public double? MaxPointsLevel2 { get; set; }
        public double? MaxPointsLevel3 { get; set; }

        public int WrittenWorkId { get; set; }
        public WrittenWork WrittenWork { get; set; } = null!;
    }

    // TABELA: Konkretne metadane ucznia dla danej pracy (np. czy był chory, inna data pisania)
    public class StudentWorkRecord
    {
        public int Id { get; set; }
        
        // Relacja z uczniem
        public int StudentId { get; set; }
        public Student Student { get; set; } = null!;
        
        // Relacja z konkretną pracą
        public int WrittenWorkId { get; set; }
        public WrittenWork WrittenWork { get; set; } = null!;
        
        // Flaga czy uczeń był nieobecny (dostaje 'NB')
        public bool IsAbsent { get; set; }
        
        // Indywidualne daty dla ucznia (np. pisał sprawdzian 3 dni później bo chorował)
        public string? Group { get; set; } // Grupa, np. A, B
        public DateTime? CustomDateWritten { get; set; }
        public DateTime? CustomDateEntered { get; set; }
        
        // DANE DOTYCZĄCE POPRAWY
        public bool IsRetakeActive { get; set; }
        public string? RetakeGroup { get; set; } // NOWOŚĆ: Grupa dla poprawy
        public DateTime? RetakeDeadline { get; set; }
        public DateTime? RetakeDateWritten { get; set; }
        public DateTime? RetakeDateEntered { get; set; }
    }

    // TABELA: Konkretne punkty zdobyte przez ucznia w danym zadaniu
    public class StudentTaskScore
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public Student Student { get; set; } = null!;
        public int WrittenWorkTaskId { get; set; }
        public WrittenWorkTask WrittenWorkTask { get; set; } = null!;
        
        // Zmienna 'p_i,j' z PSO. To wpisuje nauczyciel z palca w tabeli (Termin pierwszy).
        public double? PointsLevel1 { get; set; }
        public double? PointsLevel2 { get; set; }
        public double? PointsLevel3 { get; set; }
        
        // A tu lecą punkty z poprawy.
        public double? RetakePointsLevel1 { get; set; }
        public double? RetakePointsLevel2 { get; set; }
        public double? RetakePointsLevel3 { get; set; }
    }

    // KONFIGURACJA BAZY (EF Core)
    public class AppDbContext : DbContext
    {
        // Tu musisz dopisać każdą nową klasę/tabelę, którą stworzysz, żeby EF Core o niej wiedział
        public DbSet<SchoolClass> Classes { get; set; }
        public DbSet<Student> Students { get; set; }
        public DbSet<WrittenWork> WrittenWorks { get; set; }
        public DbSet<WrittenWorkTask> WrittenWorkTasks { get; set; }
        public DbSet<StudentTaskScore> StudentTaskScores { get; set; }
        public DbSet<StudentWorkRecord> StudentWorkRecords { get; set; } 

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // Trzymamy wszystko w jednym pliku na dysku, super proste do przenoszenia (gradebook.db)
            optionsBuilder.UseSqlite("Data Source=gradebook.db")
                          .LogTo(message => System.Diagnostics.Debug.WriteLine(message), Microsoft.Extensions.Logging.LogLevel.Information);
        }
    }

    // =========================================================================================
    // MATEMATYKA PSO
    // To jest główny kalkulator. Cała aplikacja wali tutaj, jak chce znać końcowy wynik ucznia.
    // =========================================================================================
    public static class GradeCalculator
    {
        public static double CalculateWorkScore(double M, IEnumerable<WrittenWorkTask> tasks, IEnumerable<StudentTaskScore> scores, bool isRetake)
        {
            // Ochrona przed patologiami - jak brak zadań to zwracamy 0 pkt, żeby nie wywaliło DivideByZero
            if (tasks == null || !tasks.Any() || M <= 0) return 0;

            // Kary za niższe poziomy 
            double alpha1 = 0.54;
            double alpha2 = 0.9;
            
            int n = tasks.Count(); // Liczba wszystkich zadań w sprawdzianie

            double sumQ1 = 0, sumQ2 = 0, sumQ3 = 0;

            foreach (var task in tasks)
            {
                var score = scores.FirstOrDefault(s => s.WrittenWorkTaskId == task.Id);
                
                // Jak to jest poprawa, bierzemy punkty z kolumn poprawy. Jak nie, to bazowe.
                double p1 = isRetake ? (score?.RetakePointsLevel1 ?? 0) : (score?.PointsLevel1 ?? 0);
                double p2 = isRetake ? (score?.RetakePointsLevel2 ?? 0) : (score?.PointsLevel2 ?? 0);
                double p3 = isRetake ? (score?.RetakePointsLevel3 ?? 0) : (score?.PointsLevel3 ?? 0);

                double P1 = task.MaxPointsLevel1 ?? 0;
                double P2 = task.MaxPointsLevel2 ?? 0;
                double P3 = task.MaxPointsLevel3 ?? 0;

                // q_ij to po prostu zdobyte pkt (p) podzielone przez max (P).
                // Ten if (P > 0) to prostacki trick - jak nauczyciel przypisał 0 punktów
                // maksymalnych (bo np. to zadanie nie ma 3 poziomu), to po prostu dodajemy 0 do średniej.
                sumQ1 += (P1 > 0) ? (p1 / P1) : 0;
                sumQ2 += (P2 > 0) ? (p2 / P2) : 0;
                sumQ3 += (P3 > 0) ? (p3 / P3) : 0;
            }

            // r_j to po prostu średnia ułamków dla danego poziomu
            double r1 = sumQ1 / n;
            double r2 = sumQ2 / n;
            double r3 = sumQ3 / n;

            // Finał: P = M * sumy. I Marcinie wedle prośby: zaokrąglamy do pełnej liczby punktów. 
            // MidpointRounding.AwayFromZero to taki trick, żeby 0.5 szło w górę, a nie w dół.
            return Math.Round(M * (alpha1 * r1 + alpha2 * r2 + r3), 0, MidpointRounding.AwayFromZero);
        }
    }
}