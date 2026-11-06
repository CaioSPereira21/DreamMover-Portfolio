using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Web;

namespace DreamMover.Helpers
{
    public class MugenManager
    {
        private static readonly int MEMORY_READ_INTERVAL = 100;

        public MatchStats matchStats;

        public readonly Process mugen;
        public readonly int mugenHandle;

        public bool Running { get; private set; }

        public MugenManager(Process mugen)
        {
            if (Program.VERBOSE_ACTIVE) Console.WriteLine("Tracking process: " + mugen.ProcessName);
            this.mugen = mugen;
            this.mugenHandle = MemoryReader.OpenMugenProcess(mugen);
            this.matchStats = new MatchStats(mugen, mugenHandle);
            Thread thread = new Thread(new ThreadStart(this.Run));
            thread.Start();
            this.Running = true;
        }

        public void Run()
        {
            while (matchStats.WinsLeft < 2 && matchStats.WinsRight < 2)
            {
                if (mugen.HasExited)
                {
                    return;
                }

                if (MemoryReader.ReadMatchMemory(matchStats))
                {
                    if (Program.MATCH_ACTIVE) return;
                    matchStats = new MatchStats(mugen, mugenHandle);
                }
                Thread.Sleep(MEMORY_READ_INTERVAL);
            }
        }
    }
}