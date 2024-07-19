using System;

namespace DailyUploader
{
    public sealed class AttendanceRecord
    {
        public long? EmployeeId { get; set; }
        public string TextCardNumber { get; set; }
        public DateTime Date { get; set; }
        public char InOutType { get; set; }
        public string IpAddress { get; set; }
        public string Latitude { get; set; }
        public string Longitude { get; set; }
        public string Location { get; set; }
        public string LocationDetails { get; set; }
        public string Comment { get; set; }
        public string DeviceId { get; set; }
        public long CreatedBy { get; set; }
        public int Status { get; set; }
        public string UQHash { get; set; }
    }
}
