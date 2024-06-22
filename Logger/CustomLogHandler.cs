using DailyUploader.Logger.Interface;
using System;
using System.IO;
using System.Text;

namespace DailyUploader.Logger
{
    public sealed class CustomLogHandler : ICustomLogHandler
    {
        private readonly string logDirectory;

        public CustomLogHandler()
        { }

        public CustomLogHandler(string logDirectory)
        {
            this.logDirectory = logDirectory;
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }
        }

        public void LogError(string message, DateTime dateTime)
        {
            LogMessage("Error", message, dateTime);
        }

        public void LogError(string message, DateTime dateTime, string stackTrace)
        {
            LogMessage("Error", message, dateTime, stackTrace);
        }

        public void LogInformation(string message, DateTime dateTime)
        {
            LogMessage("Info", message, dateTime);
        }

        public void LogInformation(string message, DateTime dateTime, string stackTrace)
        {
            LogMessage("Info", message, dateTime, stackTrace);
        }

        private void LogMessage(string logType, string message, DateTime dateTime, string stackTrace = null)
        {
            if (string.IsNullOrEmpty(logType))
            {
                throw new ArgumentNullException(nameof(logType));
            }

            string logFileName = $"{logType}_{dateTime:yyyyMMdd}.log";
            string logFilePath = Path.Combine(logDirectory, logFileName);

            try
            {
                // Check if the log file for the current date exists
                bool fileExists = File.Exists(logFilePath);

                // If the file doesn't exist, create a new one; otherwise, append to the existing file
                using (StreamWriter writer = new StreamWriter(logFilePath, true, Encoding.UTF8))
                {
                    if (new FileInfo(logFilePath).Length == 0)
                        writer.WriteLine($"[{dateTime}] [{logType}] {new string('-', 50)}");

                    writer.WriteLine($"[{dateTime}] [{logType}] {message}");

                    if (!string.IsNullOrEmpty(stackTrace))
                        writer.WriteLine($"StackTrace: {stackTrace}");

                    writer.WriteLine();
                }
            }
            catch (Exception ex)
            {
                // Handle exceptions related to file operations
                Console.WriteLine($"Error writing to log file: {ex.Message}");
            }
        }
    }
}
