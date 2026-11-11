namespace GradebookApp.ViewModels
{
    public class WorkStatisticsResult
    {
        public int TotalRetakes { get; set; }
        public int SuccessfulRetakesFrom1To2Plus { get; set; }
        public int SuccessfulRetakesFrom1To1 { get; set; } // DODANE: Częściowo udane
        public int FailedRetakesFrom1 { get; set; }
        public int SuccessfulRetakesFrom2Plus { get; set; }
        public int FailedRetakesFrom2Plus { get; set; }
        public double AveragePercentagePointGain { get; set; }
        public double AveragePercentagePointGainFrom1 { get; set; }
        public double AveragePercentagePointGainFrom2Plus { get; set; }
    }
}