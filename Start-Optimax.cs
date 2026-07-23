using System;
using System.Diagnostics;
using System.IO;

namespace OptimaxLauncher
{
    class Program
    {
        static void Main(string[] args)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string exeTarget = Path.Combine(baseDir, "OptimaxServer.exe");
            string jsTarget = Path.Combine(baseDir, "server.js");

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            psi.WorkingDirectory = baseDir;

            if (File.Exists(exeTarget))
            {
                psi.FileName = exeTarget;
            }
            else if (File.Exists(jsTarget))
            {
                psi.FileName = "cmd.exe";
                psi.Arguments = "/c node server.js";
            }
            else
            {
                Console.WriteLine("Error: OptimaxServer.exe or server.js not found in directory.");
                Console.ReadLine();
                return;
            }

            try
            {
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Admin Elevation Cancelled or Error: " + ex.Message);
            }
        }
    }
}
