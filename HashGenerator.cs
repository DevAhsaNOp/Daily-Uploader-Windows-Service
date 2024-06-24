using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DailyUploader
{
    public class HashGenerator
    {
        const string KEY = "546C8DF2";

        public static DateTime GetHashedDateTime(string HashString)
        {
            var dateTime = HashString.Split('_')[1];
            return StringToDateTime(dateTime);
        }

        public static string RandomHashGenerator(AttendanceRecord record)
        {
            var rawData = $"{Guid.NewGuid()}-{record.EmployeeId}{record.InOutType}_{record.Date:ddMMyyyyHHmmss}";
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
                var rawData = $"{record.EmployeeId}{record.InOutType}_{record.Date:ddMMyyyyHHmmss}"; // Concatenate all fields
                var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                return BitConverter.ToString(bytes).Replace("-", "").ToLower();
            }
        }

        public static string Decryptor(string stringToDecrypt)
        {
            byte[] inputByteArray = new byte[stringToDecrypt.Length];
            byte[] byKey = { };
            byte[] IV =
            {
                18,52,86,120,144,171,205,239
            };

            byKey = Encoding.UTF8.GetBytes(KEY);
            DES des = DES.Create();
            inputByteArray = Convert.FromBase64String(stringToDecrypt);
            MemoryStream ms = new MemoryStream();
            CryptoStream cs = new CryptoStream(ms, des.CreateDecryptor(byKey, IV), CryptoStreamMode.Write);
            cs.Write(inputByteArray, 0, inputByteArray.Length);
            cs.FlushFinalBlock();
            Encoding encoding = Encoding.UTF8;
            return encoding.GetString(ms.ToArray());
        }
    }
}
