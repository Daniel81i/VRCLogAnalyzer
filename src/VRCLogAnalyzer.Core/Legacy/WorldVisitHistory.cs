namespace VRCLogAnalyzer.Core.Legacy
{
    /// <summary>v0.x（sechiro 版）のワールド訪問履歴テーブル。移行元として読み取りにのみ使う。</summary>
    public class WorldVisitHistory
    {
        public int Id { get; set; }
        public string? WorldName { get; set; }
        public string? WorldVisitTimestamp { get; set; }
        public string? WorldId { get; set; }
        public string? Description { get; set; }
    }
}
