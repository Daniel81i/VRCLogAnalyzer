namespace VRCLogAnalyzer.Core.Legacy
{
    /// <summary>v0.x（sechiro 版）のユーザー遭遇履歴テーブル。移行元として読み取りにのみ使う。</summary>
    public class UserEncounterHistory
    {
        public int Id { get; set; }
        public string? Timestamp { get; set; }
        public string? DisplayName { get; set; }
        public string? WorldName { get; set; }
        public string? WorldVisitTimestamp { get; set; }
        public string? Bio { get; set; }
    }
}
