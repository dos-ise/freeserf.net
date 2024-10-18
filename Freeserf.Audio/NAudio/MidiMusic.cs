
using System;

namespace Freeserf.Audio.NAudio
{
    internal class MidiMusic : Music
    {
        public MidiMusic(XMI xMI) : base(LoadMidi(xmi))
        {
        }

        private static object LoadMidi(object xmi)
        {
            throw new NotImplementedException();
        }
    }
}