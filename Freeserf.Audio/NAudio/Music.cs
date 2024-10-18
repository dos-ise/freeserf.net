using System;

namespace Freeserf.Audio.NAudio
{
    internal abstract class Music : Audio.ITrack, IDisposable
    {
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