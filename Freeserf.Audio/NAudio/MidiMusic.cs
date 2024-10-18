
using NAudio.Midi;
using System;
using System.Collections.Generic;

namespace Freeserf.Audio.NAudio
{
    internal class MidiMusic : Music
    {
        public MidiMusic(XMI xMI) : base(xMI)
        {
        }

        public static List<MidiEvent> ConvertToMidiEvents(XMI xmi)
        {
            var midiEvents = new List<MidiEvent>();

            foreach (var e in xmi)
            {
                switch (e)
                {
                    case XMI.PlayNoteEvent playNoteEvent:
                        var noteOn = new NoteOnEvent((int)playNoteEvent.StartTime, playNoteEvent.Channel, playNoteEvent.Note, playNoteEvent.Velocity, 0);
                        var noteOff = new NoteEvent((int)(playNoteEvent.StartTime + playNoteEvent.Duration), playNoteEvent.Channel, MidiCommandCode.NoteOff, playNoteEvent.Note, 0);
                        midiEvents.Add(noteOn);
                        midiEvents.Add(noteOff);
                        break;
                    case XMI.SetTempoEvent setTempoEvent:
                        var tempoChange = new TempoEvent((int)setTempoEvent.StartTime, setTempoEvent.Tempo);
                        midiEvents.Add(tempoChange);
                        break;
                        // Füge andere Eventtypen nach Bedarf hinzu
                }
            }

            return midiEvents;
        }
    }
}