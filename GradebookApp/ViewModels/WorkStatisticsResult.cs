namespace GradebookApp.ViewModels
{
    public class WorkStatisticsResult
    {
        public int TotalRetakes { get; set; }
        
        // Poprawy z 1
        public int SuccessfulRetakesFrom1To2Plus { get; set; } // Zdał (40%+)
        public int SuccessfulRetakesFrom1To1 { get; set; }     // Poprawił punkty, ale wciąż 1 (<40%)
        public int FailedRetakesFrom1 { get; set; }            // Ten sam lub gorszy wynik
        
        // Poprawy z 2+
        public int SuccessfulRetakesFrom2Plus { get; set; }
        public int FailedRetakesFrom2Plus { get; set; }
        
        // Zmiany p.p.
        public double AveragePercentagePointGain { get; set; }
        public double AveragePercentagePointGainFrom1 { get; set; }
        public double AveragePercentagePointGainFrom2Plus { get; set; }
    }
}