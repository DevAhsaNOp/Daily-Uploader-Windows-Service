namespace DailyUploaderService
{
    public sealed class GenericRecord
    {
        public string Key { get; set; }
        public string Value { get; set; }
        public long? CreatedBy { get; set; }
        public long? UpdatedBy { get; set; }
        public int Status { get; set; }
    }
}
