using System;
using System.Security.Cryptography;
using System.Text;

namespace DailyUploader
{
    public class HashGenerator
    {
        public static DateTime GetHashedDateTime(string HashString)
        {
            var dateTime = HashString.Split('_')[1];
            return StringToDateTime(dateTime);
        }

        public static string RandomHashGenerator(AttendanceRecord record)
        {
            var rawData = $"{Guid.NewGuid()}-{record.EmployeeId}{record.InOutType}_{record.DateTime:ddMMyyyyHHmmss}";
            return rawData;
        }

        private static DateTime StringToDateTime(string dateTime)
        {
            if (string.IsNullOrEmpty(dateTime))
                return DateTime.MinValue;

            return DateTime.ParseExact(dateTime, "ddMMyyyyHHmmss", null);
        }

        public static string GenerateHash(AttendanceRecord record)
        {
            using (var sha256 = SHA256.Create())
            {
                var rawData = $"{record.EmployeeId}{record.InOutType}_{record.DateTime:ddMMyyyyHHmmss}"; // Concatenate all fields
                var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                return BitConverter.ToString(bytes).Replace("-", "").ToLower();
            }
        }
    }
}
