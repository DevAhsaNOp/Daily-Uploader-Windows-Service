using DailyUploader.Logger;
using DailyUploader.Logger.Interface;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.ServiceProcess;
using System.Threading;

namespace DailyUploader
{
    [RunInstaller(true)]
    public partial class DailyUploaderService : ServiceBase
    {
        public Thread worker = null;
        private static string LastProcessedHashPath = string.Empty;
        private static HashSet<string> ProcessedHashes = new HashSet<string>();
        private static readonly System.Timers.Timer timer = new System.Timers.Timer(120000);
        private readonly string UserEmail = ConfigurationSettings.AppSettings["UserEmail"];
        private readonly ICustomLogHandler logHandler = new CustomLogHandler(LogsBaseDir);
        private readonly string UserPassword = ConfigurationSettings.AppSettings["UserPassword"];
        private readonly static string LogsBaseDir = ConfigurationSettings.AppSettings["LogsBaseDir"];
        private readonly string ConnectionString = ConfigurationSettings.AppSettings["ConnectionString"];
        private readonly string ProcessedHashBaseDir = ConfigurationSettings.AppSettings["ProcessedHashBaseDir"];
        private readonly int ScheduleTime = Convert.ToInt32(ConfigurationSettings.AppSettings["ScheduleTime"]);
        private readonly int RecordsToBeRead = Convert.ToInt32(ConfigurationSettings.AppSettings["RecordsToBeRead"]);
        private readonly int ReadAttendanceDaysCount = Convert.ToInt32(ConfigurationSettings.AppSettings["ReadAttendanceDaysCount"]);

        public DailyUploaderService()
        {
            InitializeComponent();
        }

        public void OnDebug()
        {
            OnStart(null);
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                ThreadStart start = new ThreadStart(Working);
                worker = new Thread(start);
                worker.Start();
                if ((worker != null) && worker.IsAlive)
                {
                    logHandler.LogInformation("Windows Service is started at: " + DateTime.Now, DateTime.Now);
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        private void Working()
        {
            while (true)
            {
                LoadProcessedHashes();

                // Fetch new records
                var newRecords = FetchNewRecords();

                // Insert new records into the server database
                if (newRecords.Count > 0)
                {
                    logHandler.LogInformation("Inserting new records into the server database.", DateTime.Now);
                    InsertRecordsToServer(newRecords);

                    // Update processed hashes and persist them
                    logHandler.LogInformation("Updating processed hashes and persisting them.", DateTime.Now);
                    UpdateProcessedHashes(newRecords);
                }
                else
                    logHandler.LogInformation("No new records found in the Local database.", DateTime.Now);

                Thread.Sleep(ScheduleTime * 60 * 1000);
            }
        }

        protected override void OnStop()
        {
            try
            {
                if ((worker != null) && worker.IsAlive)
                {
                    worker.Abort();
                    logHandler.LogInformation($"Windows Service is stopped at: {DateTime.Now}", DateTime.Now);
                    logHandler.LogInformation(new string('-', 50), DateTime.Now);
                }
            }
            catch (Exception)
            {

                throw;
            }
        }

        public void CalculateTotalPagesAndRowCountBetweenDates(int pageSize, DateTime? startDateTime, DateTime? endDateTime, out int totalPages, out int totalRowCount)
        {
            using (SqlConnection connection = new SqlConnection(ConnectionString))
            {
                using (SqlCommand command = new SqlCommand("spCalculateTotalPagesAndRowCountBetweenDates", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;

                    command.Parameters.Add(new SqlParameter("@PageSize", SqlDbType.Int)).Value = pageSize;
                    command.Parameters.Add(new SqlParameter("@StartDateTime", SqlDbType.DateTime)).Value = (object)startDateTime ?? DBNull.Value;
                    command.Parameters.Add(new SqlParameter("@EndDateTime", SqlDbType.DateTime)).Value = (object)endDateTime ?? DBNull.Value;

                    SqlParameter totalPagesParam = new SqlParameter("@TotalPages", SqlDbType.Int)
                    {
                        Direction = ParameterDirection.Output
                    };
                    command.Parameters.Add(totalPagesParam);

                    SqlParameter totalRowCountParam = new SqlParameter("@TotalRowCount", SqlDbType.Int)
                    {
                        Direction = ParameterDirection.Output
                    };
                    command.Parameters.Add(totalRowCountParam);

                    connection.Open();
                    command.ExecuteNonQuery();

                    totalPages = (int)totalPagesParam.Value;
                    totalRowCount = (int)totalRowCountParam.Value;

                    logHandler.LogInformation($"Total pages: {totalPages}, Total row count: {totalRowCount}", DateTime.Now);
                }
            }
        }

        public List<AttendanceRecord> GetFilteredAttendanceBetweenDates(int pageNumber, int pageSize, DateTime? startDateTime, DateTime? endDateTime)
        {
            List<AttendanceRecord> attendanceRecords = new List<AttendanceRecord>();

            using (SqlConnection connection = new SqlConnection(ConnectionString))
            {
                using (SqlCommand command = new SqlCommand("spGetFilteredAttendanceBetweenDates", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;

                    command.Parameters.Add(new SqlParameter("@PageNumber", SqlDbType.Int)).Value = pageNumber;
                    command.Parameters.Add(new SqlParameter("@PageSize", SqlDbType.Int)).Value = pageSize;
                    command.Parameters.Add(new SqlParameter("@StartDateTime", SqlDbType.DateTime)).Value = (object)startDateTime ?? DBNull.Value;
                    command.Parameters.Add(new SqlParameter("@EndDateTime", SqlDbType.DateTime)).Value = (object)endDateTime ?? DBNull.Value;

                    connection.Open();
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        if (!reader.HasRows)
                        {
                            logHandler.LogInformation("No new records found in the local database.", DateTime.Now);
                            return attendanceRecords;
                        }

                        while (reader.Read())
                        {
                            AttendanceRecord record = new AttendanceRecord
                            {
                                EmployeeId = reader.GetInt64(reader.GetOrdinal("EmployeeId")),
                                InOutType = reader.GetString(reader.GetOrdinal("InOutType"))[0],
                                DateTime = reader.GetDateTime(reader.GetOrdinal("DateTime"))
                            };

                            attendanceRecords.Add(record);
                        }

                        logHandler.LogInformation($"{attendanceRecords.Count} new records found in the local database.", DateTime.Now);
                    }
                }
            }

            return attendanceRecords;
        }

        private List<AttendanceRecord> FetchNewRecords()
        {
            var newRecords = new List<AttendanceRecord>();

            var startDateTime = DateTime.Now.AddDays(-ReadAttendanceDaysCount);
            var endDateTime = DateTime.Now;

            CalculateTotalPagesAndRowCountBetweenDates(RecordsToBeRead, startDateTime, endDateTime, out int totalPages, out int totalRowCount);

            if (totalPages == 0)
            {
                logHandler.LogInformation("No new records found in the local database.", DateTime.Now);
                return newRecords;
            }

            for (int i = 1; i <= totalPages; i++)
            {
                var records = GetFilteredAttendanceBetweenDates(i, RecordsToBeRead, startDateTime, endDateTime);

                foreach (var record in records)
                {
                    var hash = HashGenerator.GenerateHash(record);

                    if (!ProcessedHashes.Contains(hash))
                    {
                        record.UQHash = hash;
                        newRecords.Add(record);
                    }
                }
            }

            return newRecords;
        }

        private void InsertRecordsToServer(List<AttendanceRecord> newRecords)
        {
            using (var connection = new SqlConnection(ConnectionString))
            {
                connection.Open();
                foreach (var record in newRecords)
                {
                    var query = "INSERT INTO [dbo].[tblAttendanceServer] ([EmployeeId] ,[InOutType] ,[DateTime]) VALUES (@EmpID, @InOut, @DateTime)";
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@InOut", record.InOutType);
                        command.Parameters.AddWithValue("@EmpID", record.EmployeeId);
                        command.Parameters.AddWithValue("@DateTime", record.DateTime);
                        command.ExecuteNonQuery();
                    }
                }
                logHandler.LogInformation($"{newRecords.Count} new records inserted into the server database.", DateTime.Now);
            }
        }

        private void LoadProcessedHashes()
        {
            GetLastProcessedHashPath();

            if (File.Exists(LastProcessedHashPath))
            {
                var lines = File.ReadAllLines(LastProcessedHashPath);

                if (lines.Length > 0)
                    logHandler.LogInformation($"{lines.Length} Processed hashes loaded from the last processed hash file.", DateTime.Now);
                else
                    logHandler.LogInformation("No processed hashes found in the last processed hash file.", DateTime.Now);

                ProcessedHashes = new HashSet<string>(lines);
            }
        }

        private void GetLastProcessedHashPath()
        {
            using (var conn = new SqlConnection(ConnectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("SELECT TOP 1 Value FROM tblGeneric WHERE [Key] = 'LastProcessedHashPath' ", conn))
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            LastProcessedHashPath = reader.GetString(0);
                            if (string.IsNullOrEmpty(LastProcessedHashPath))
                                logHandler.LogError("Last processed hash path is empty.", DateTime.Now);
                            else
                                logHandler.LogInformation($"Last processed hash path found!", DateTime.Now);
                        }
                        else
                            logHandler.LogInformation($"Last processed hash path is not found!", DateTime.Now);
                    }
                }
            }
        }

        private void UpdateLastProcessedHashPath(string hashPath)
        {
            using (var conn = new SqlConnection(ConnectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("UPDATE tblGeneric SET Value = @hashPath WHERE [Key] = 'LastProcessedHashPath'", conn))
                {
                    cmd.Parameters.AddWithValue("@hashPath", hashPath);
                    cmd.ExecuteNonQuery();
                    logHandler.LogInformation("Last processed hash path updated successfully!", DateTime.Now);
                }
            }
        }

        private void InsertLastProcessedHashPath(string hashPath)
        {
            using (var conn = new SqlConnection(ConnectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("INSERT INTO tblGeneric ([Key], [Value]) VALUES ('LastProcessedHashPath', @hashPath)", conn))
                {
                    cmd.Parameters.AddWithValue("@hashPath", hashPath);
                    cmd.ExecuteNonQuery();
                    logHandler.LogInformation("Last processed hash path inserted successfully!", DateTime.Now);
                }
            }
        }

        private void UpdateProcessedHashes(List<AttendanceRecord> newRecords)
        {
            foreach (var record in newRecords)
                ProcessedHashes.Add(record.UQHash);

            var newHashPath = GenerateNewFilePath();
            File.WriteAllLines(newHashPath, ProcessedHashes);

            if (File.Exists(newHashPath))
            {
                if (new FileInfo(newHashPath).Length > 0)
                {
                    logHandler.LogInformation($"{newRecords.Count} new hashes written to the new hash file.", DateTime.Now);
                    if (!string.IsNullOrEmpty(LastProcessedHashPath))
                        UpdateLastProcessedHashPath(newHashPath);
                    else
                        InsertLastProcessedHashPath(newHashPath);
                }
            }
        }

        private string GenerateNewFilePath()
        {
            if (!Directory.Exists(ProcessedHashBaseDir))
            {
                logHandler.LogInformation("Processed hash base directory not found. Creating a new one.", DateTime.Now);
                Directory.CreateDirectory(ProcessedHashBaseDir);
            }

            var fileName = $"ProcessedHashes_{DateTime.Now:yyyyMMddHHmmss}.txt";
            var hashPath = Path.Combine(ProcessedHashBaseDir, fileName);

            if (!File.Exists(hashPath))
            {
                logHandler.LogInformation("Processed hash file not found. Creating a new one.", DateTime.Now);
                File.Create(hashPath).Close();
            }

            return hashPath;
        }
    }
}
