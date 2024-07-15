﻿using System.ServiceProcess;

namespace DailyUploader
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static void Main()
        {
            //DailyUploaderService service1 = new DailyUploaderService();
            //service1.OnDebug();

            ServiceBase[] ServicesToRun;
            ServicesToRun = new ServiceBase[]
            {
                new DailyUploaderService()
            };
            ServiceBase.Run(ServicesToRun);
        }
    }
}
