using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Web;

namespace DreamMover.Helpers
{
    public enum MatchWinner { LEFT, RIGHT, NONE }

    public class MatchStats
    {
        public Process Mugen { get; private set; }
        public int MugenHandle { get; private set; }
        public int NumberOfPlayers { get; private set; }
        public int WinsAddress { get; set; }
        public int WinsLeft { get; private set; }
        public int WinsRight { get; private set; }

        public MatchStats(Process mugen, int mugenHandle)
        {
            this.Mugen = mugen;
            this.MugenHandle = mugenHandle;
            this.WinsAddress = 0;
            this.WinsLeft = 0;
            this.WinsRight = 0;
        }

        public void UpdateWins(int winsLeft, int winsRight)
        {
            if (this.WinsLeft + 1 == winsLeft)
            {
                this.WinsLeft = winsLeft;
            }

            if (this.WinsRight + 1 == winsRight)
            {
                this.WinsRight = winsRight;
            }
        }

        public MatchWinner getWinner()
        {
            return WinsLeft > WinsRight ? MatchWinner.LEFT :
                WinsRight > WinsLeft ? MatchWinner.RIGHT :
                MatchWinner.NONE;
        }
    }
}