using System;

namespace DailyUploader
{
    public sealed class AttendanceRecord
    {
        public long? EmployeeId { get; set; }
        public string TextCardNumber { get; set; }
        public DateTime Date { get; set; }
        public char InOutType { get; set; }
        public long CreatedBy { get; set; }
        public int Status { get; set; }
        public string UQHash { get; set; }
    }
}
