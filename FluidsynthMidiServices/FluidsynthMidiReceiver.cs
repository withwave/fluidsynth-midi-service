#define MIDI_MANAGER
using System;
using System.IO;
using System.Linq;
using Android.Media.Midi;
using Android.App;
using NFluidsynth;
using Android.Content;
using Java.Interop;
using Android.Media;
using Android.Util;
using NFluidsynth.MidiManager;
using Commons.Music.Midi;

namespace FluidsynthMidiServices
{
	class FluidsynthMidiReceiver : MidiReceiver
	{
		public FluidsynthMidiReceiver (Context context)
		{
#if MIDI_MANAGER
			access = new FluidsynthMidiAccess ();
			access.ConfigureSettings = (settings) => {
#else
			var settings = new Settings ();
#endif
			//settings [ConfigurationKeys.AudioDriver].StringValue = "opensles";
			
			//settings [ConfigurationKeys.SynthParallelRender].IntValue = 0;
			//settings [ConfigurationKeys.SynthThreadsafeApi].IntValue = 0;
			//settings [ConfigurationKeys.AudioPeriods].IntValue = 16;
			//settings [ConfigurationKeys.AudioPeriods].IntValue = 64;

			// This is required.
			settings [ConfigurationKeys.AudioSampleFormat].StringValue = "16bits";

			// Note that it is NOT audio sample rate but *synthesizing* sample rate.
			// So it is wrong assumption that AudioManager.PropertyOutputSampleRate would give the best outcome.
			//
			// 44.1KHz seems too much. 22.05 works on kvm-based emulator on Ubuntu.
			//settings [ConfigurationKeys.SynthSampleRate].DoubleValue = 44100;
			//settings [ConfigurationKeys.SynthSampleRate].DoubleValue = 22050;
			settings [ConfigurationKeys.SynthSampleRate].DoubleValue = 11025;
			
			//settings ["audio.opensles.buffering-sleep-rate"].DoubleValue = 0.85;

			var manager = context.GetSystemService (Context.AudioService).JavaCast<AudioManager> ();
			var fpb = double.Parse (manager.GetProperty (AudioManager.PropertyOutputFramesPerBuffer));

			// This adjusted number seems good for Android (at least on my kvm-based emulator on Ubuntu).
			settings [ConfigurationKeys.AudioPeriodSize].IntValue = (int) fpb;
#if MIDI_MANAGER
			};
			access.Soundfonts.Add (default_soundfont);
			output = access.OpenOutputAsync (access.Outputs.First ().Id).Result;
#else
			syn = new Synth (settings);
			LoadDefaultSoundFontSpecial (context, syn);

			adriver = new AudioDriver (syn.Settings, syn);
#endif
		}

#if MIDI_MANAGER
		FluidsynthMidiAccess access;
		IMidiOutput output;
#else
		Synth syn;
		AudioDriver adriver;
#endif
		
		// FIXME: this is hack until we get proper sf loader.
		const string default_soundfont = "/sdcard/tmp/FluidR3_GM.sf2";

		void LoadDefaultSoundFontSpecial (Context context, Synth synth)
		{
			//Console.WriteLine ("!!!!!!!!!!!!!!! {0}", context.ApplicationInfo.DataDir);
			synth.LoadSoundFont (default_soundfont, true);
		}

		protected override void Dispose (bool disposing)
		{
#if MIDI_MANAGER
#else
			if (disposing) {
				adriver.Dispose ();
				syn.Dispose ();
			}
#endif
			base.Dispose (disposing);
		}

		public override void OnSend (byte[] msg, int offset, int count, long timestamp)
		{
			try {
				DoSend (msg, offset, count, timestamp);
			} catch (Exception) {
				Log.Error ("FluidsynthMidiService", "Failed to send MIDI message: offset: {0}, count: {1}, timestamp: {2}, message length: {3}",
					offset, count, timestamp, msg.Length);
				Log.Error ("FluidsynthMidiService", "  message: {0}",
					msg.Skip (offset).Take (count).Select (b => b.ToString ("X") + ' '));
				throw;
			}
		}
		
		void DoSend (byte[] msg, int offset, int count, long timestamp)
		{
#if MIDI_MANAGER
			output.SendAsync (msg, offset, count, timestamp);
#else
			// FIXME: consider timestamp.

			int ch = msg [offset] & 0x0F;
			switch (msg [offset] & 0xF0) {
			case 0x80:
				syn.NoteOff (ch, msg [offset + 1]);
				break;
			case 0x90:
				if (msg [offset + 2] == 0)
					syn.NoteOff (ch, msg [offset + 1]);
				else
					syn.NoteOn (ch, msg [offset + 1], msg [offset + 2]);
				break;
			case 0xA0:
				// No PAf in fluidsynth?
				break;
			case 0xB0:
				syn.CC (ch, msg [offset + 1], msg [offset + 2]);
				break;
			case 0xC0:
				syn.ProgramChange (ch, msg [offset + 1]);
				break;
			case 0xD0:
				syn.ChannelPressure (ch, msg [offset + 1]);
				break;
			case 0xE0:
				syn.PitchBend (ch, (msg [offset + 1] << 14) + msg [offset + 2]);
				break;
			case 0xF0:
				syn.Sysex (new ArraySegment<byte> (msg, offset, count).ToArray (), null);
				break;
			}
#endif
		}
	}
}

