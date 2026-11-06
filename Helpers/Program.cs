using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Web;

namespace DreamMover.Helpers
{
    public class Program
    {
        private static readonly int PROCESS_FIND_INTERVAL = 100;

        public static bool VERBOSE_ACTIVE;
        public static bool MATCH_ACTIVE;
        public static bool PROCESS_ACTIVE;

        private static List<MugenManager> managers = new List<MugenManager>();
        private static bool mugenFound = false;

        static void Main(string[] args)
        {
            VERBOSE_ACTIVE = args.Contains("-v") || args.Contains("--verbose");
            MATCH_ACTIVE = args.Contains("-m") || args.Contains("--match");
            PROCESS_ACTIVE = args.Contains("-p") || args.Contains("--process");
            Run();
        }

        private static void Run()
        {
            while (true)
            {
                foreach (MugenManager mm in managers)
                {
                    if (!mm.Running)
                    {
                        managers.Remove(mm);
                        if (MATCH_ACTIVE || PROCESS_ACTIVE) return;
                    }
                }

                if (!mugenFound)
                {
                    List<Process> mugens = FindUntrackedMugens();
                    if (mugens.Count > 0)
                    {
                        foreach (Process p in mugens)
                        {
                            managers.Add(new MugenManager(p));
                            if (MATCH_ACTIVE || PROCESS_ACTIVE)
                            {
                                mugenFound = true;
                                break;
                            }
                        }
                    }
                }

                Thread.Sleep(PROCESS_FIND_INTERVAL);
            }
        }

        private static List<Process> FindUntrackedMugens()
        {
            Process[] mugens = MemoryReader.GetMugenHandles();
            List<Process> untracked = new List<Process>();
            foreach (Process p in mugens)
            {
                bool tracked = false;
                foreach (MugenManager mm in managers)
                {
                    if (mm.mugen.Id == p.Id)
                    {
                        tracked = true;
                        break;
                    }
                }
                if (!tracked)
                {
                    untracked.Add(p);
                }
            }
            return untracked;
        }
    }
}