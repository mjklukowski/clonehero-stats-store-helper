using Google.Apis.Sheets.v4.Data;

namespace StatsStoreHelper.Apis.GoogleApi
{
    public class FindRowResult
    {
        public int Index { get; set; }
        public RowData RowData { get; set; }
    }
}