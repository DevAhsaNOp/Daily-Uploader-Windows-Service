using System;
using System.ComponentModel;
using System.ServiceProcess;

namespace DailyUploader
{
    [RunInstaller(true)]
    public partial class ProjectInstaller : System.Configuration.Install.Installer
    {
        private ServiceInstaller serviceInstaller;
        private ServiceProcessInstaller processInstaller;

        public ProjectInstaller()
        {
            serviceInstaller = new ServiceInstaller();
            processInstaller = new ServiceProcessInstaller();

            serviceInstaller.ServiceName = "DailyUploaderTest";
            serviceInstaller.StartType = ServiceStartMode.Automatic;

            processInstaller.Account = ServiceAccount.LocalSystem;

            Installers.Add(serviceInstaller);
            Installers.Add(processInstaller);

            InitializeComponent();
        }

        public override void Install(System.Collections.IDictionary stateSaver)
        {
            base.Install(stateSaver);

            // Start the service after installation
            using (ServiceController sc = new ServiceController(serviceInstaller.ServiceName))
            {
                try
                {
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error starting service: " + ex.Message);
                }
            }
        }
    }
}
