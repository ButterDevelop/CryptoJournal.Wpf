namespace CryptoJournal.Wpf.Storage.Scenarios
{
    public sealed class ScenarioStateDto
    {
        public string                              EnvironmentId { get; set; } = "";
        public Dictionary<string, ScenarioPlanDto> PlansBySymbol { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class ScenarioPlanDto
    {
        public string               Symbol        { get; set; } = "";
        public bool                 IsPercentMode { get; set; }

        // Position quantity recorded at the time of the last save or normalization
        public decimal              BaseQty       { get; set; }

        public List<ScenarioLegDto> Legs          { get; set; } = [];
    }

    public sealed class ScenarioLegDto
    {
        public decimal InputAmount { get; set; }  // Defined allocation (either absolute quantity or percentage)
        public decimal TargetPrice { get; set; }
        public string  Note        { get; set; } = string.Empty;
    }

    public interface IScenarioStore
    {
        ScenarioPlanDto? TryGetPlan(string symbol);
        void SetPlan(ScenarioPlanDto plan);

        IReadOnlyList<ScenarioPlanDto> GetPlansSnapshot();
        bool RemovePlan(string symbol);

        void Load(string environmentId);
        void Save();

        event Action<string>? ScenariosChanged;
        void NotifyScenariosChanged(string symbol);
    }
}