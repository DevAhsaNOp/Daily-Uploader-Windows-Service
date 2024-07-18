using DailyUploader.Logger;
using DailyUploader.Logger.Interface;
using DailyUploaderService;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.ServiceProcess;
using System.Text;
using System.Threading;

namespace DailyUploader
{
    [RunInstaller(true)]
    public partial class DailyUploaderService : ServiceBase
    {
        public Thread worker = null;
        private static long UserId = 0;
        private static string AuthToken = string.Empty;
        private static string RefreshToken = string.Empty;
        private static string LastProcessedHashPath = string.Empty;
        private static HashSet<string> ProcessedHashes = new HashSet<string>();
        private readonly string BaseURL = ConfigurationManager.AppSettings["BaseURL"];
        private static readonly System.Timers.Timer timer = new System.Timers.Timer(120000);
        private readonly string UserEmail = ConfigurationManager.AppSettings["UserEmail"];
        private readonly ICustomLogHandler logHandler = new CustomLogHandler(LogsBaseDir);
        private readonly string UserPassword = ConfigurationManager.AppSettings["UserPassword"];
        private readonly static string LogsBaseDir = ConfigurationManager.AppSettings["LogsBaseDir"];
        private readonly string ConnectionString = ConfigurationManager.AppSettings["ConnectionString"];
        private readonly string ProcessedHashBaseDir = ConfigurationManager.AppSettings["ProcessedHashBaseDir"];
        private readonly int ScheduleTime = Convert.ToInt32(ConfigurationManager.AppSettings["ScheduleTime"]);
        private readonly int RecordsToBeRead = Convert.ToInt32(ConfigurationManager.AppSettings["RecordsToBeRead"]);
        private static readonly int ReadAttendanceDaysCount = Convert.ToInt32(ConfigurationManager.AppSettings["ReadAttendanceDaysCount"]);
        private DateTime? _startDateTime = null;
        private DateTime? _endDateTime = null;

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
                Login();

                LoadProcessedHashes();

                // Fetch new records
                var newRecords = FetchNewRecords();

                // Insert new records into the server database
                if (newRecords.Count > 0)
                {
                    logHandler.LogInformation($"App Config records uploading to the server database.", DateTime.Now);
                    UploadAppConfig();

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
                                //TextCardNumber = reader.GetString(reader.GetOrdinal("TextCardNumber")),
                                InOutType = reader.GetString(reader.GetOrdinal("InOutType"))[0],
                                Date = reader.GetDateTime(reader.GetOrdinal("DateTime"))
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

            var startDateTime = DateTime.Now.AddDays(-ReadAttendanceDaysCount).Date;
            var endDateTime = DateTime.Now;
            _startDateTime = startDateTime;
            _endDateTime = endDateTime;

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
            var apiURL = $"{BaseURL}OnTimeBackLog/BulkCreate";

            newRecords.ForEach(r =>
            {
                r.CreatedBy = UserId;
                r.Status = 1;
            });

            var jsonData = JsonConvert.SerializeObject(newRecords);

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {AuthToken}");
                client.DefaultRequestHeaders.Add("Access-Control-Allow-Origin", "*");

                var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
                logHandler.LogInformation($"Inserting new records into the server database.\n Data: {jsonData}", DateTime.Now);
                var response = client.PostAsync(apiURL, content).Result;

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = response.Content.ReadAsStringAsync().Result;
                    var responseJson = JsonConvert.DeserializeObject<dynamic>(responseContent);
                    logHandler.LogInformation($"{newRecords.Count} new records inserted into the server database with message {responseJson.message}", DateTime.Now);
                    logHandler.LogInformation($"{responseJson.message}", DateTime.Now);
                }
                else
                    logHandler.LogError("Inserting new records into the server database failed!", DateTime.Now);
            }
        }

        private void Login()
        {
            var loginURL = $"{BaseURL}User/Login";
            var loginData = new Dictionary<string, string>
            {
                { "email", UserEmail },
                { "password", HashGenerator.Decryptor(UserPassword) }
            };

            var jsonData = JsonConvert.SerializeObject(loginData);

            using (HttpClient client = new HttpClient())
            {
                var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
                var response = client.PostAsync(loginURL, content).Result;

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = response.Content.ReadAsStringAsync().Result;
                    dynamic responseJson = JsonConvert.DeserializeObject<dynamic>(responseContent);

                    AuthToken = responseJson.result.access_Token;
                    RefreshToken = responseJson.result.refresh_Token;

                    TokenDecoder(AuthToken);

                    logHandler.LogInformation("Login successful!", DateTime.Now);
                }
                else
                    logHandler.LogError("Login failed!", DateTime.Now);
            }
        }

        private void TokenDecoder(string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();

            var jwtToken = tokenHandler.ReadJwtToken(token);
            var claims = jwtToken.Claims.ToList();

            UserId = Convert.ToInt64(claims.FirstOrDefault(c => c.Type == "UserId").Value);
        }

        private void UploadAppConfig()
        {
            var apiURL = $"{BaseURL}Generic/BulkCreate";
            var genricRequest = new List<GenericRecord>
            {
                new GenericRecord
                {
                    Key = "LogsBaseDir",
                    Value = LogsBaseDir
                },
                new GenericRecord
                {
                    Key = "ConnectionString",
                    Value = ConnectionString
                },
                new GenericRecord
                {
                    Key = "ProcessedHashBaseDir",
                    Value = ProcessedHashBaseDir
                },
                new GenericRecord
                {
                    Key = "ScheduleTime",
                    Value = ScheduleTime.ToString()
                },
                new GenericRecord
                {
                    Key = "RecordsToBeRead",
                    Value = RecordsToBeRead.ToString()
                },
                new GenericRecord
                {
                    Key = "ReadAttendanceDaysCount",
                    Value = ReadAttendanceDaysCount.ToString()
                },
                new GenericRecord
                {
                    Key = "DatesWhichRecordsHasBeenProcessed",
                    Value = $"{_startDateTime} to {_endDateTime}"
                },
            };

            genricRequest.ForEach(r =>
            {
                r.CreatedBy = UserId;
                r.Status = 1;
            });

            var jsonData = JsonConvert.SerializeObject(genricRequest);

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {AuthToken}");
                client.DefaultRequestHeaders.Add("Access-Control-Allow-Origin", "*");

                var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
                logHandler.LogInformation($"Inserting App Config Data into the server database.\n Data: {jsonData}", DateTime.Now);
                var response = client.PostAsync(apiURL, content).Result;

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = response.Content.ReadAsStringAsync().Result;
                    var responseJson = JsonConvert.DeserializeObject<dynamic>(responseContent);
                    logHandler.LogInformation($"{genricRequest.Count} App Config Data inserted into the server database with message {responseJson.message}", DateTime.Now);
                    logHandler.LogInformation($"{responseJson}", DateTime.Now);
                }
                else
                    logHandler.LogError("Inserting App Config Data into the server database failed!", DateTime.Now);
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
            var key = "LastProcessedHashPath";
            var apiURL = $"{BaseURL}Generic/GetValueByKey?key={key}";

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {AuthToken}");
                client.DefaultRequestHeaders.Add("Access-Control-Allow-Origin", "*");

                var response = client.GetAsync(apiURL).Result;

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = response.Content.ReadAsStringAsync().Result;
                    var responseJson = JsonConvert.DeserializeObject<dynamic>(responseContent);

                    LastProcessedHashPath = responseJson.result;
                    if (string.IsNullOrEmpty(LastProcessedHashPath))
                        logHandler.LogError("Last processed hash path is empty.", DateTime.Now);
                    else
                        logHandler.LogInformation("Last processed hash path found!", DateTime.Now);
                }
                else
                    logHandler.LogError("Last processed hash path is not found!", DateTime.Now);
            }

            //using (var conn = new SqlConnection(ConnectionString))
            //{
            //    conn.Open();
            //    using (var cmd = new SqlCommand("SELECT TOP 1 Value FROM tblGeneric WHERE [Key] = 'LastProcessedHashPath' ", conn))
            //    {
            //        using (var reader = cmd.ExecuteReader())
            //        {
            //            if (reader.Read())
            //            {
            //                LastProcessedHashPath = reader.GetString(0);
            //                if (string.IsNullOrEmpty(LastProcessedHashPath))
            //                    logHandler.LogError("Last processed hash path is empty.", DateTime.Now);
            //                else
            //                    logHandler.LogInformation($"Last processed hash path found!", DateTime.Now);
            //            }
            //            else
            //                logHandler.LogInformation($"Last processed hash path is not found!", DateTime.Now);
            //        }
            //    }
            //}
        }

        private void UpdateLastProcessedHashPath(string hashPath)
        {
            var apiURL = $"{BaseURL}Generic/UpdateByKey";
            var genricRequest = new GenericRecord
            {
                Key = "LastProcessedHashPath",
                Value = hashPath,
                UpdatedBy = UserId,
                Status = 1
            };

            var jsonData = JsonConvert.SerializeObject(genricRequest);

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {AuthToken}");
                client.DefaultRequestHeaders.Add("Access-Control-Allow-Origin", "*");

                var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
                logHandler.LogInformation($"Updating Last processed hash path into the server database.\n Data: {jsonData}", DateTime.Now);
                var response = client.PostAsync(apiURL, content).Result;

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = response.Content.ReadAsStringAsync().Result;
                    var responseJson = JsonConvert.DeserializeObject<dynamic>(responseContent);
                    logHandler.LogInformation($"{genricRequest} Last processed hash path updated into the server database with message {responseJson.message}", DateTime.Now);
                    logHandler.LogInformation($"{responseJson}", DateTime.Now);
                }
                else
                    logHandler.LogError("Updating Last processed hash path into the server database failed!", DateTime.Now);
            }

            //using (var conn = new SqlConnection(ConnectionString))
            //{
            //    conn.Open();
            //    using (var cmd = new SqlCommand("UPDATE tblGeneric SET Value = @hashPath WHERE [Key] = 'LastProcessedHashPath'", conn))
            //    {
            //        cmd.Parameters.AddWithValue("@hashPath", hashPath);
            //        cmd.ExecuteNonQuery();
            //        logHandler.LogInformation("Last processed hash path updated successfully!", DateTime.Now);
            //    }
            //}
        }

        private void InsertLastProcessedHashPath(string hashPath)
        {
            var apiURL = $"{BaseURL}Generic/Create";
            var genricRequest = new GenericRecord
            {
                Key = "LastProcessedHashPath",
                Value = hashPath,
                CreatedBy = UserId,
                Status = 1
            };

            var jsonData = JsonConvert.SerializeObject(genricRequest);

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {AuthToken}");
                client.DefaultRequestHeaders.Add("Access-Control-Allow-Origin", "*");

                var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
                logHandler.LogInformation($"Inserting Last processed hash path into the server database.\n Data: {jsonData}", DateTime.Now);
                var response = client.PostAsync(apiURL, content).Result;

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = response.Content.ReadAsStringAsync().Result;
                    var responseJson = JsonConvert.DeserializeObject<dynamic>(responseContent);
                    logHandler.LogInformation($"{genricRequest} Last processed hash path inserted into the server database with message {responseJson.message}", DateTime.Now);
                    logHandler.LogInformation($"{responseJson}", DateTime.Now);
                }
                else
                    logHandler.LogError("Inserting Last processed hash path into the server database failed!", DateTime.Now);
            }

            //using (var conn = new SqlConnection(ConnectionString))
            //{
            //    conn.Open();
            //    using (var cmd = new SqlCommand("INSERT INTO tblGeneric ([Key], [Value]) VALUES ('LastProcessedHashPath', @hashPath)", conn))
            //    {
            //        cmd.Parameters.AddWithValue("@hashPath", hashPath);
            //        cmd.ExecuteNonQuery();
            //        logHandler.LogInformation("Last processed hash path inserted successfully!", DateTime.Now);
            //    }
            //}
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
