﻿using Freeserf.Data;
using System;
using System.Collections.Generic;
namespace Freeserf.Audio.NAudio
{
    internal class MidiPlayer : MusicPlayer
    {
        public MidiPlayer(DataSource dataSource)
            : base(dataSource)
        {

        }

        protected override Audio.ITrack CreateTrack(int trackID)
        {
            var music = AudioImpl.GetMusicTrackData(dataSource, trackID);

            if (DataSource.DosMusic(dataSource))
                return new MidiMusic(new XMI(music));

            throw new ExceptionFreeserf(ErrorSystemType.Data, $"Only DOS data uses MIDI music");
        }
    }
}
