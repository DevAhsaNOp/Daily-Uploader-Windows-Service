using System;

namespace DailyUploader
{
    public sealed class AttendanceRecord
    {
        public long EmployeeId { get; set; }
        public DateTime DateTime { get; set; }
        public char InOutType { get; set; }
        public string UQHash { get; set; }
    }
}
