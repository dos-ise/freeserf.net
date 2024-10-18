using NAudio.Midi;
using System;
using System.Collections.Generic;
using static Freeserf.Audio.XMI;

namespace Freeserf.Audio.NAudio
{
    internal abstract class Music : Audio.ITrack, IDisposable
    {
        protected Music(XMI xmi)
        {
        }

        public void Play(Audio.Player player)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }


    }
}