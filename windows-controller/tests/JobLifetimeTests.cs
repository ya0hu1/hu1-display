using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace DeskMateLearning
{
    internal static class JobLifetimeTests
    {
        public static int Run(string report)
        {
            Process child=null;OwnedEngineJob job=null;
            try {
                job=new OwnedEngineJob();
                child=new Process();
                child.StartInfo=new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,"--job-test-child") {
                    UseShellExecute=false,CreateNoWindow=true
                };
                if(!child.Start()) throw new Exception("Synthetic child did not start.");
                job.Attach(child);
                if(child.WaitForExit(150)) throw new Exception("Synthetic child ended before job cleanup.");
                job.Dispose();job=null;
                if(!child.WaitForExit(4000)) throw new Exception("Closing owned job did not stop its synthetic child.");
                File.WriteAllText(report,"PASS owned job closes only its synthetic controller child\r\nRESULT passed=1 failed=0\r\nScope: own synthetic process only; no phone, ADB, engine or drivers.\r\n",Encoding.UTF8);
                return 0;
            }
            catch(Exception e) {File.WriteAllText(report,"FAIL "+e+"\r\nRESULT passed=0 failed=1\r\n",Encoding.UTF8);return 1;}
            finally {
                if(job!=null)job.Dispose();
                if(child!=null){try {if(!child.HasExited){child.Kill();child.WaitForExit(3000);}}catch(Exception){}child.Dispose();}
            }
        }
    }
}
