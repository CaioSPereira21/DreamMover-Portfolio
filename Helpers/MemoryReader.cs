using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web;

namespace DreamMover.Helpers
{
    public class MemoryReader
    {
        private const int PROCESS_WM_READ = 0x00000010;
        private const int FRAMES = 0x00504718;

        private const int WIN_ADDRESS = 0x001040E8;
        private const int RED_OFFSET = 0x0000871C;
        private const int BLUE_OFFSET = 0x00008728;

        private const string MUGEN_PROCESS_NAME = "mugen";
        private const string MUGEN_WINDOW_NAME = "M.U.G.E.N";

        public static bool ReadMatchMemory(MatchStats ms)
        {
            try
            {
                int bytesRead = 0;
                byte[] buffer = new byte[4];

                if (!ms.Mugen.Responding) throw new Exception();
                ReadProcessMemory(ms.MugenHandle, FRAMES, buffer, buffer.Length, ref bytesRead);

                if (ms.WinsAddress != 0)
                {
                    ReadProcessMemory(ms.MugenHandle, ms.WinsAddress + RED_OFFSET, buffer, buffer.Length, ref bytesRead);
                    int winsRight = BitConverter.ToInt32(buffer, 0);
                    ReadProcessMemory(ms.MugenHandle, ms.WinsAddress + BLUE_OFFSET, buffer, buffer.Length, ref bytesRead);
                    int winsLeft = BitConverter.ToInt32(buffer, 0);

                    if ((ms.WinsLeft > 0 || ms.WinsRight > 0) && winsLeft == 0 && winsRight == 0)
                    {
                        return true;
                    }
                    else
                    {
                        ms.UpdateWins(winsRight, winsLeft);
                    }
                }
                else
                {
                    int mugen = ms.Mugen.MainModule.BaseAddress.ToInt32() + WIN_ADDRESS;
                    ReadProcessMemory(ms.MugenHandle, mugen, buffer, buffer.Length, ref bytesRead);
                    int address = BitConverter.ToInt32(buffer, 0);
                    ReadProcessMemory(ms.MugenHandle, address + RED_OFFSET, buffer, buffer.Length, ref bytesRead);
                    int winsRight = BitConverter.ToInt32(buffer, 0);
                    ReadProcessMemory(ms.MugenHandle, address + BLUE_OFFSET, buffer, buffer.Length, ref bytesRead);
                    int winsLeft = BitConverter.ToInt32(buffer, 0);
                    if (winsRight + winsLeft == 1 || winsRight + winsLeft == 2)
                    {
                        ms.WinsAddress = address;
                        ms.UpdateWins(winsRight, winsLeft);
                    }
                }

                if (Program.VERBOSE_ACTIVE)
                {
                    Console.Write("\r[" + DateTime.Now + "] SCORE: " + ms.WinsLeft + " - " + ms.WinsRight);
                }
            }
            catch (Exception e)
            {
                if (Program.VERBOSE_ACTIVE)
                {
                    Console.WriteLine("Error while attempting to read MUGEN memory.");
                }
            }
            return false;
        }

        public static int OpenMugenProcess(Process mugen)
        {
            return OpenProcess(PROCESS_WM_READ, false, mugen.Id).ToInt32();
        }

        public static Process[] GetMugenHandles()
        {
            return Process.GetProcessesByName(MUGEN_PROCESS_NAME);
        }

        //Memory reading methods
        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll")]
        public static extern bool ReadProcessMemory(int hProcess,
          int lpBaseAddress, byte[] lpBuffer, int dwSize, ref int lpNumberOfBytesRead);
    }
}