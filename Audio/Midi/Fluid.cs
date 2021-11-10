﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace OpenLaMulana.Audio.Midi
{
    /// <summary>
    /// FluidSynth 2.0.5 + libinstpatch DLS support implementation for playing DirectMusic without
    /// DirectX for Linux, Windows64 and other platforms
    /// VERY massive thank you to Sebanisu (github.com/Sebanisu) from the OpenVIII project for not only this class, but also providing the most documentation on the .sgt music format I've seen anywhere online! :D
    /// </summary>
    public sealed class Fluid : IDisposable
    {
        private static bool bValid = true;

        private static IntPtr driver;
        private static IntPtr synth;
        private static IntPtr settings;
        private static IntPtr player;

        private static GCHandle[] handles;

        private enum ThreadFluidState
        {
            /// <summary>
            /// Idle means player is ready for new action. Either it finished playing or was never run
            /// </summary>
            idle,

            /// <summary>
            /// Playing means the player is actively running the sequence and working with
            /// synthesizer. Every kind of seeking and music handling is automatic
            /// </summary>
            playing,

            /// <summary>
            /// Paused actually makes it possible to pause the sequencer in place. Change to running
            /// to continue playing
            /// </summary>
            paused,

            /// <summary>
            /// Reset stops playing, clears the sequence and all related helpers and falls back into
            /// idle mode
            /// </summary>
            reset,

            /// <summary>
            /// New song is a special state, where it does the resetting, idle and then goes into
            /// playing state. You should always set state to newSong when you point to new music collection.
            /// </summary>
            newSong,

            /// <summary>
            /// Call only at the very exit of application. This resets and aborts the thread
            /// </summary>
            kill
        }

        private static ThreadFluidState fluidState;

        public byte[] midBuffer = null;

#if _WINDOWS
    private const string fluidLibName = "x86/libfluidsynth.dll";
#else
    private const string fluidLibName = "x64/libfluidsynth.dll";
#endif

        internal static class NativeMethods
        {


#region P/INVOKES
            [DllImport(fluidLibName)]
            internal static extern IntPtr new_fluid_settings();

            [DllImport(fluidLibName)]
            internal static extern IntPtr new_fluid_player(IntPtr synth);

            [DllImport(fluidLibName)]
            internal static extern IntPtr delete_fluid_player(IntPtr player);

            [DllImport(fluidLibName)]
            internal static extern IntPtr new_fluid_audio_driver(IntPtr settings, IntPtr synth);

            [DllImport(fluidLibName)]
            internal static extern int fluid_player_play(IntPtr player);

            [DllImport(fluidLibName)]
            internal static extern int fluid_player_stop(IntPtr player);

            [DllImport(fluidLibName)]
            internal static extern int fluid_player_join(IntPtr player);

            [DllImport(fluidLibName, CharSet = CharSet.Ansi, SetLastError = true, ThrowOnUnmappableChar = true, BestFitMapping = false)]
            internal static extern int fluid_player_add(IntPtr player, string mid);

            [DllImport(fluidLibName)]
            internal static extern int fluid_player_add_mem(IntPtr player, byte[] mid, uint len);

            internal enum fluid_types_enum
            {
                FLUID_NO_TYPE = -1, /**< Undefined type */
                FLUID_NUM_TYPE,     /**< Numeric (double) */
                FLUID_INT_TYPE,     /**< Integer */
                FLUID_STR_TYPE,     /**< String */
                FLUID_SET_TYPE      /**< Set of values */
            };

            [DllImport(fluidLibName)]
            internal static extern void delete_fluid_settings(IntPtr settings);

            [DllImport(fluidLibName, CharSet = CharSet.Ansi, SetLastError = true, ThrowOnUnmappableChar = true, BestFitMapping = false)]
            internal static extern int fluid_settings_get_type(IntPtr settings, string name);

            [DllImport(fluidLibName, CharSet = CharSet.Ansi, SetLastError = true, ThrowOnUnmappableChar = true, BestFitMapping = false)]
            internal static extern int fluid_settings_get_hints(IntPtr settings, string name, out int val);

            [DllImport(fluidLibName, CharSet = CharSet.Ansi, SetLastError = true, ThrowOnUnmappableChar = true, BestFitMapping = false)]
            internal static extern int fluid_settings_is_realtime(IntPtr settings, string name);

            [DllImport(fluidLibName, CharSet = CharSet.Ansi, SetLastError = true, ThrowOnUnmappableChar = true, BestFitMapping = false)]
            internal static extern int fluid_settings_setstr(IntPtr settings, string name, string str);

            [DllImport(fluidLibName, CharSet = CharSet.Ansi, SetLastError = true, ThrowOnUnmappableChar = true, BestFitMapping = false)]
            internal static extern int fluid_settings_copystr(IntPtr settings, string name, byte[] str, int len);

            [DllImport(fluidLibName, CharSet = CharSet.Ansi, SetLastError = true, ThrowOnUnmappableChar = true, BestFitMapping = false)]
            internal static extern int fluid_settings_dupstr(IntPtr settings, string name, out string str);

            [DllImport(fluidLibName, CharSet = CharSet.Ansi, SetLastError = true, ThrowOnUnmappableChar = true, BestFitMapping = false)]
            internal static extern int fluid_settings_getstr_default(IntPtr settings, string name, out string def);

            [DllImport(fluidLibName, CharSet = CharSet.Ansi, SetLastError = true, ThrowOnUnmappableChar = true, BestFitMapping = false)]
            internal static extern int fluid_settings_str_equal(IntPtr settings, string name, string value);

            [DllImport(fluidLibName, CharSet = CharSet.Ansi, SetLastError = true, ThrowOnUnmappableChar = true, BestFitMapping = false)]
            internal static extern int fluid_settings_setnum(IntPtr settings, string name, double val);

            [DllImport(fluidLibName, CharSet = CharSet.Ansi, SetLastError = true, ThrowOnUnmappableChar = true, BestFitMapping = false)]
            internal static extern int fluid_settings_getnum(IntPtr settings, string name, out double val);

            [DllImport(fluidLibName, CharSet = CharSet.Ansi, SetLastError = true, ThrowOnUnmappableChar = true, BestFitMapping = false)]
            internal static extern int fluid_settings_getnum_default(IntPtr settings, string name, out double val);

            [DllImport(fluidLibName, CharSet = CharSet.Ansi, SetLastError = true, ThrowOnUnmappableChar = true, BestFitMapping = false)]
            internal static extern int fluid_settings_getnum_range(IntPtr settings, string name,
                                    double min, double max);

            [DllImport(fluidLibName, CharSet = CharSet.Ansi, SetLastError = true, ThrowOnUnmappableChar = true, BestFitMapping = false)]
            internal static extern int fluid_settings_setint(IntPtr settings, string name, int val);

            [DllImport(fluidLibName, CharSet = CharSet.Ansi, SetLastError = true, ThrowOnUnmappableChar = true, BestFitMapping = false)]
            internal static extern int fluid_settings_getint(IntPtr settings, string name, out int val);

            [DllImport(fluidLibName, CharSet = CharSet.Ansi, SetLastError = true, ThrowOnUnmappableChar = true, BestFitMapping = false)]
            internal static extern int fluid_settings_getint_default(IntPtr settings, string name, out int val);

            [DllImport(fluidLibName, CharSet = CharSet.Ansi, SetLastError = true, ThrowOnUnmappableChar = true, BestFitMapping = false)]
            internal static extern int fluid_settings_getint_range(IntPtr settings, string name,
                                    out int min, out int max);

            [DllImport(fluidLibName)]
            internal static extern IntPtr new_fluid_synth(IntPtr settings);

            [DllImport(fluidLibName)]
            internal static extern void delete_fluid_synth(IntPtr synth);

            [DllImport(fluidLibName)]
            internal static extern IntPtr fluid_synth_get_settings(IntPtr synth);

            /* MIDI channel messages */

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_noteon(IntPtr synth, int chan, int key, int vel);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_noteoff(IntPtr synth, int chan, int key);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_cc(IntPtr synth, int chan, int ctrl, int val);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_get_cc(IntPtr synth, int chan, int ctrl, int pval);

            [DllImport(fluidLibName, CharSet = CharSet.Ansi, SetLastError = true, ThrowOnUnmappableChar = true, BestFitMapping = false)]
            internal static extern int fluid_synth_sysex(IntPtr synth, string data, int len, string response, int response_len, int handled, int dryrun);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_pitch_bend(IntPtr synth, int chan, int val);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_get_pitch_bend(IntPtr synth, int chan, int ppitch_bend);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_pitch_wheel_sens(IntPtr synth, int chan, int val);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_get_pitch_wheel_sens(IntPtr synth, int chan, int pval);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_program_change(IntPtr synth, int chan, int program);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_channel_pressure(IntPtr synth, int chan, int val);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_key_pressure(IntPtr synth, int chan, int key, int val);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_bank_select(IntPtr synth, int chan, int bank);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_sfont_select(IntPtr synth, int chan, int sfont_id);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_program_select(IntPtr synth, int chan, int sfont_id, int bank_num, int preset_num);

            [DllImport(fluidLibName, CharSet = CharSet.Ansi, SetLastError = true, ThrowOnUnmappableChar = true, BestFitMapping = false)]
            internal static extern int fluid_synth_program_select_by_sfont_name(IntPtr synth, int chan, string sfont_name, int bank_num, int preset_num);

            [DllImport(fluidLibName)]
            internal static extern
            int fluid_synth_get_program(IntPtr synth, int chan, int sfont_id, int bank_num, int preset_num);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_unset_program(IntPtr synth, int chan);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_program_reset(IntPtr synth);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_system_reset(IntPtr synth);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_all_notes_off(IntPtr synth, int chan);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_all_sounds_off(IntPtr synth, int chan);

            internal enum fluid_midi_channel_type
            {
                CHANNEL_TYPE_MELODIC = 0, /**< Melodic midi channel */
                CHANNEL_TYPE_DRUM = 1 /**< Drum midi channel */
            };

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_set_channel_type(IntPtr synth, int chan, int type);

            [DllImport(fluidLibName)]
            /* Low level access */
            internal static extern IntPtr fluid_synth_get_channel_preset(IntPtr synth, int chan);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_start(IntPtr synth, uint id,
                                                 IntPtr preset, int audio_chan,
                                                 int midi_chan, int key, int vel);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_stop(IntPtr synth, uint id);

            /* SoundFont management */

            [DllImport(fluidLibName, CharSet = CharSet.Ansi, SetLastError = true, ThrowOnUnmappableChar = true, BestFitMapping = false)]
            internal static extern int fluid_synth_sfload(IntPtr synth, string filename, int reset_presets);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_sfreload(IntPtr synth, int id);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_sfunload(IntPtr synth, int id, int reset_presets);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_add_sfont(IntPtr synth, IntPtr sfont);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_remove_sfont(IntPtr synth, IntPtr sfont);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_sfcount(IntPtr synth);

            [DllImport(fluidLibName)]
            internal static extern IntPtr fluid_synth_get_sfont(IntPtr synth, uint num);

            [DllImport(fluidLibName)]
            internal static extern IntPtr fluid_synth_get_sfont_by_id(IntPtr synth, int id);

            [DllImport(fluidLibName, CharSet = CharSet.Ansi, SetLastError = true, ThrowOnUnmappableChar = true, BestFitMapping = false)]
            internal static extern IntPtr fluid_synth_get_sfont_by_name(IntPtr synth, string name);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_set_bank_offset(IntPtr synth, int sfont_id, int offset);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_get_bank_offset(IntPtr synth, int sfont_id);

            /* Reverb  */

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_set_reverb(IntPtr synth, double roomsize,
                    double damping, double width, double level);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_set_reverb_roomsize(IntPtr synth, double roomsize);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_set_reverb_damp(IntPtr synth, double damping);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_set_reverb_width(IntPtr synth, double width);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_set_reverb_level(IntPtr synth, double level);

            [DllImport(fluidLibName)]
            internal static extern void fluid_synth_set_reverb_on(IntPtr synth, int on);

            [DllImport(fluidLibName)]
            internal static extern double fluid_synth_get_reverb_roomsize(IntPtr synth);

            [DllImport(fluidLibName)]
            internal static extern double fluid_synth_get_reverb_damp(IntPtr synth);

            [DllImport(fluidLibName)]
            internal static extern double fluid_synth_get_reverb_level(IntPtr synth);

            [DllImport(fluidLibName)]
            internal static extern double fluid_synth_get_reverb_width(IntPtr synth);

            /* Chorus */

            /**
             * Chorus modulation waveform type.
             */

            internal enum fluid_chorus_mod
            {
                FLUID_CHORUS_MOD_SINE = 0,            /**< Sine wave chorus modulation */
                FLUID_CHORUS_MOD_TRIANGLE = 1         /**< Triangle wave chorus modulation */
            };

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_set_chorus(IntPtr synth, int nr, double level, double speed, double depth_ms, fluid_chorus_mod type);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_set_chorus_nr(IntPtr synth, int nr);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_set_chorus_level(IntPtr synth, double level);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_set_chorus_speed(IntPtr synth, double speed);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_set_chorus_depth(IntPtr synth, double depth_ms);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_set_chorus_type(IntPtr synth, int type);

            [DllImport(fluidLibName)]
            internal static extern void fluid_synth_set_chorus_on(IntPtr synth, int on);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_get_chorus_nr(IntPtr synth);

            [DllImport(fluidLibName)]
            internal static extern double fluid_synth_get_chorus_level(IntPtr synth);

            [DllImport(fluidLibName)]
            internal static extern double fluid_synth_get_chorus_speed(IntPtr synth);

            [DllImport(fluidLibName)]
            internal static extern double fluid_synth_get_chorus_depth(IntPtr synth);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_get_chorus_type(IntPtr synth); /* see fluid_chorus_mod */

            /* Audio and MIDI channels */

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_count_midi_channels(IntPtr synth);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_count_audio_channels(IntPtr synth);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_count_audio_groups(IntPtr synth);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_count_effects_channels(IntPtr synth);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_count_effects_groups(IntPtr synth);

            /* Synthesis parameters */

            [DllImport(fluidLibName)]
            internal static extern void fluid_synth_set_sample_rate(IntPtr synth, float sample_rate);

            [DllImport(fluidLibName)]
            internal static extern void fluid_synth_set_gain(IntPtr synth, float gain);

            [DllImport(fluidLibName)]
            internal static extern float fluid_synth_get_gain(IntPtr synth);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_set_polyphony(IntPtr synth, int polyphony);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_get_polyphony(IntPtr synth);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_get_active_voice_count(IntPtr synth);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_get_internal_bufsize(IntPtr synth);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_set_interp_method(IntPtr synth, int chan, fluid_interp interp_method);

            /**
             * Synthesis interpolation method.
             */

            internal enum fluid_interp
            {
                FLUID_INTERP_NONE = 0,        /**< No interpolation: Fastest, but questionable audio quality */
                FLUID_INTERP_LINEAR = 1,      /**< Straight-line interpolation: A bit slower, reasonable audio quality */
                FLUID_INTERP_4THORDER = 4,    /**< Fourth-order interpolation, good quality, the default */
                FLUID_INTERP_7THORDER = 7,    /**< Seventh-order interpolation */

                FLUID_INTERP_DEFAULT = FLUID_INTERP_4THORDER, /**< Default interpolation method */
                FLUID_INTERP_HIGHEST = FLUID_INTERP_7THORDER, /**< Highest interpolation method */
            };

            /* Generator interface */

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_set_gen(IntPtr synth, int chan,
                                                   fluid_gen_type param, float value);

            internal enum fluid_gen_type
            {
                GEN_STARTADDROFS,       /**< Sample start address offset (0-32767) */
                GEN_ENDADDROFS,     /**< Sample end address offset (-32767-0) */
                GEN_STARTLOOPADDROFS,       /**< Sample loop start address offset (-32767-32767) */
                GEN_ENDLOOPADDROFS,     /**< Sample loop end address offset (-32767-32767) */
                GEN_STARTADDRCOARSEOFS, /**< Sample start address coarse offset (X 32768) */
                GEN_MODLFOTOPITCH,      /**< Modulation LFO to pitch */
                GEN_VIBLFOTOPITCH,      /**< Vibrato LFO to pitch */
                GEN_MODENVTOPITCH,      /**< Modulation envelope to pitch */
                GEN_FILTERFC,           /**< Filter cutoff */
                GEN_FILTERQ,            /**< Filter Q */
                GEN_MODLFOTOFILTERFC,       /**< Modulation LFO to filter cutoff */
                GEN_MODENVTOFILTERFC,       /**< Modulation envelope to filter cutoff */
                GEN_ENDADDRCOARSEOFS,       /**< Sample end address coarse offset (X 32768) */
                GEN_MODLFOTOVOL,        /**< Modulation LFO to volume */
                GEN_UNUSED1,            /**< Unused */
                GEN_CHORUSSEND,     /**< Chorus send amount */
                GEN_REVERBSEND,     /**< Reverb send amount */
                GEN_PAN,            /**< Stereo panning */
                GEN_UNUSED2,            /**< Unused */
                GEN_UNUSED3,            /**< Unused */
                GEN_UNUSED4,            /**< Unused */
                GEN_MODLFODELAY,        /**< Modulation LFO delay */
                GEN_MODLFOFREQ,     /**< Modulation LFO frequency */
                GEN_VIBLFODELAY,        /**< Vibrato LFO delay */
                GEN_VIBLFOFREQ,     /**< Vibrato LFO frequency */
                GEN_MODENVDELAY,        /**< Modulation envelope delay */
                GEN_MODENVATTACK,       /**< Modulation envelope attack */
                GEN_MODENVHOLD,     /**< Modulation envelope hold */
                GEN_MODENVDECAY,        /**< Modulation envelope decay */
                GEN_MODENVSUSTAIN,      /**< Modulation envelope sustain */
                GEN_MODENVRELEASE,      /**< Modulation envelope release */
                GEN_KEYTOMODENVHOLD,        /**< Key to modulation envelope hold */
                GEN_KEYTOMODENVDECAY,       /**< Key to modulation envelope decay */
                GEN_VOLENVDELAY,        /**< Volume envelope delay */
                GEN_VOLENVATTACK,       /**< Volume envelope attack */
                GEN_VOLENVHOLD,     /**< Volume envelope hold */
                GEN_VOLENVDECAY,        /**< Volume envelope decay */
                GEN_VOLENVSUSTAIN,      /**< Volume envelope sustain */
                GEN_VOLENVRELEASE,      /**< Volume envelope release */
                GEN_KEYTOVOLENVHOLD,        /**< Key to volume envelope hold */
                GEN_KEYTOVOLENVDECAY,       /**< Key to volume envelope decay */
                GEN_INSTRUMENT,     /**< Instrument ID (shouldn't be set by user) */
                GEN_RESERVED1,      /**< Reserved */
                GEN_KEYRANGE,           /**< MIDI note range */
                GEN_VELRANGE,           /**< MIDI velocity range */
                GEN_STARTLOOPADDRCOARSEOFS, /**< Sample start loop address coarse offset (X 32768) */
                GEN_KEYNUM,         /**< Fixed MIDI note number */
                GEN_VELOCITY,           /**< Fixed MIDI velocity value */
                GEN_ATTENUATION,        /**< Initial volume attenuation */
                GEN_RESERVED2,      /**< Reserved */
                GEN_ENDLOOPADDRCOARSEOFS,   /**< Sample end loop address coarse offset (X 32768) */
                GEN_COARSETUNE,     /**< Coarse tuning */
                GEN_FINETUNE,           /**< Fine tuning */
                GEN_SAMPLEID,           /**< Sample ID (shouldn't be set by user) */
                GEN_SAMPLEMODE,     /**< Sample mode flags */
                GEN_RESERVED3,      /**< Reserved */
                GEN_SCALETUNE,      /**< Scale tuning */
                GEN_EXCLUSIVECLASS,     /**< Exclusive class number */
                GEN_OVERRIDEROOTKEY,        /**< Sample root note override */

                /* the initial pitch is not a "standard" generator. It is not
                 * mentioned in the list of generator in the SF2 specifications. It
                 * is used, however, as the destination for the default pitch wheel
                 * modulator. */
                GEN_PITCH,          /**< Pitch @note Not a real SoundFont generator */

                GEN_CUSTOM_BALANCE,          /**< Balance @note Not a real SoundFont generator */
                /* non-standard generator for an additional custom high- or low-pass filter */
                GEN_CUSTOM_FILTERFC,        /**< Custom filter cutoff frequency */
                GEN_CUSTOM_FILTERQ     /**< Custom filter Q */
            }

            [DllImport(fluidLibName)]
            internal static extern float fluid_synth_get_gen(IntPtr synth, int chan, fluid_gen_type param);

            /* Tuning */

            [DllImport(fluidLibName, CharSet = CharSet.Ansi, SetLastError = true, ThrowOnUnmappableChar = true, BestFitMapping = false)]
            internal static extern int fluid_synth_activate_key_tuning(IntPtr synth, int bank, int prog,
                                        string name, double pitch, int apply);

            [DllImport(fluidLibName, CharSet = CharSet.Ansi, SetLastError = true, ThrowOnUnmappableChar = true, BestFitMapping = false)]
            internal static extern int fluid_synth_activate_octave_tuning(IntPtr synth, int bank, int prog,
                                           string name, double pitch, int apply);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_tune_notes(IntPtr synth, int bank, int prog,
                                       int len, int keys, double pitch, int apply);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_activate_tuning(IntPtr synth, int chan, int bank, int prog,
                                            int apply);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_deactivate_tuning(IntPtr synth, int chan, int apply);

            [DllImport(fluidLibName)]
            internal static extern void fluid_synth_tuning_iteration_start(IntPtr synth);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_tuning_iteration_next(IntPtr synth, int bank, int prog);

            [DllImport(fluidLibName, CharSet = CharSet.Ansi, SetLastError = true, ThrowOnUnmappableChar = true, BestFitMapping = false)]
            internal static extern int fluid_synth_tuning_dump(IntPtr synth, int bank, int prog,
                    string name, int len, double pitch);

            /* Misc */

            [DllImport(fluidLibName)]
            internal static extern double fluid_synth_get_cpu_load(IntPtr synth);

            /* Default modulators */

            /**
             * Enum used with fluid_synth_add_default_mod() to specify how to handle duplicate modulators.
             */

            internal enum fluid_synth_add_mod
            {
                FLUID_SYNTH_OVERWRITE,        /**< Overwrite any existing matching modulator */
                FLUID_SYNTH_ADD,              /**< Add (sum) modulator amounts */
            };

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_add_default_mod(IntPtr synth, IntPtr mod, fluid_synth_add_mod mode);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_remove_default_mod(IntPtr synth, IntPtr mod);

            /* Synthesizer's interface to handle SoundFont loaders */

            [DllImport(fluidLibName)]
            internal static extern void fluid_synth_add_sfloader(IntPtr synth, IntPtr loader);

            [DllImport(fluidLibName)]
            internal static extern IntPtr fluid_synth_alloc_voice(IntPtr synth,
                    IntPtr sample,
                    int channum, int key, int vel);

            [DllImport(fluidLibName)]
            internal static extern void fluid_synth_start_voice(IntPtr synth, IntPtr voice);

            [DllImport(fluidLibName)]
            internal static extern void fluid_synth_get_voicelist(IntPtr synth,
                    byte[] buf, int bufsize, int ID);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_handle_midi_event(IntPtr data, IntPtr @event);

            /**
             * Specifies the type of filter to use for the custom IIR filter
             */

            internal enum fluid_iir_filter_type
            {
                FLUID_IIR_DISABLED = 0, /**< Custom IIR filter is not operating */
                FLUID_IIR_LOWPASS, /**< Custom IIR filter is operating as low-pass filter */
                FLUID_IIR_HIGHPASS, /**< Custom IIR filter is operating as high-pass filter */
                FLUID_IIR_LAST /**< @internal Value defines the Count of filter types (#fluid_iir_filter_type) @warning This symbol is not part of the public API and ABI stability guarantee and may change at any time! */
            };

            /**
             * Specifies optional settings to use for the custom IIR filter
             */

            internal enum fluid_iir_filter_flags
            {
                FLUID_IIR_Q_LINEAR = 1 << 0, /**< The Soundfont spec requires the filter Q to be interpreted in dB. If this flag is set the filter Q is instead assumed to be in a linear range */
                FLUID_IIR_Q_ZERO_OFF = 1 << 1, /**< If this flag the filter is switched off if Q == 0 (prior to any transformation) */
                FLUID_IIR_NO_GAIN_AMP = 1 << 2 /**< The Soundfont spec requires to correct the gain of the filter depending on the filter's Q. If this flag is set the filter gain will not be corrected. */
            };

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_set_custom_filter(IntPtr synth, int type, int flags);

            /* LADSPA */

            [DllImport(fluidLibName)]
            internal static extern IntPtr fluid_synth_get_ladspa_fx(IntPtr synth);

            /* API: Poly mono mode */

            /** Interface to poly/mono mode variables
             *
             * Channel mode bits OR-ed together so that it matches with the midi spec: poly omnion (0), mono omnion (1), poly omnioff (2), mono omnioff (3)
             */

            internal enum fluid_channel_mode_flags
            {
                FLUID_CHANNEL_POLY_OFF = 0x01, /**< if flag is set, the basic channel is in mono on state, if not set poly is on */
                FLUID_CHANNEL_OMNI_OFF = 0x02, /**< if flag is set, the basic channel is in omni off state, if not set omni is on */
            };

            /** Indicates the breath mode a channel is set to */

            internal enum fluid_channel_breath_flags
            {
                FLUID_CHANNEL_BREATH_POLY = 0x10,  /**< when channel is poly, this flag indicates that the default velocity to initial attenuation modulator is replaced by a breath to initial attenuation modulator */
                FLUID_CHANNEL_BREATH_MONO = 0x20,  /**< when channel is mono, this flag indicates that the default velocity to initial attenuation modulator is replaced by a breath modulator */
                FLUID_CHANNEL_BREATH_SYNC = 0x40,  /**< when channel is mono, this flag indicates that the breath controler(MSB)triggers noteon/noteoff on the running note */
            };

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_reset_basic_channel(IntPtr synth, int chan);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_get_basic_channel(IntPtr synth, int chan,
                    int basic_chan_out,
                    int mode_chan_out,
                    int basic_val_out);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_set_basic_channel(IntPtr synth, int chan, int mode, int val);

            /** Interface to mono legato mode
             *
             * Indicates the legato mode a channel is set to
             * n1,n2,n3,.. is a legato passage. n1 is the first note, and n2,n3,n4 are played legato with previous note. */

            internal enum fluid_channel_legato_mode
            {
                FLUID_CHANNEL_LEGATO_MODE_RETRIGGER, /**< Mode 0 - Release previous note, start a new note */
                FLUID_CHANNEL_LEGATO_MODE_MULTI_RETRIGGER, /**< Mode 1 - On contiguous notes retrigger in attack section using current value, shape attack using current dynamic and make use of previous voices if any */
                FLUID_CHANNEL_LEGATO_MODE_LAST /**< @internal Value defines the Count of legato modes (#fluid_channel_legato_mode) @warning This symbol is not part of the public API and ABI stability guarantee and may change at any time! */
            };

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_set_legato_mode(IntPtr synth, int chan, int legatomode);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_get_legato_mode(IntPtr synth, int chan, int legatomode);

            /** Interface to portamento mode
             *
             * Indicates the portamento mode a channel is set to
             */

            internal enum fluid_channel_portamento_mode
            {
                FLUID_CHANNEL_PORTAMENTO_MODE_EACH_NOTE, /**< Mode 0 - Portamento on each note (staccato or legato) */
                FLUID_CHANNEL_PORTAMENTO_MODE_LEGATO_ONLY, /**< Mode 1 - Portamento only on legato note */
                FLUID_CHANNEL_PORTAMENTO_MODE_STACCATO_ONLY, /**< Mode 2 - Portamento only on staccato note */
                FLUID_CHANNEL_PORTAMENTO_MODE_LAST /**< @internal Value defines the Count of portamento modes (#fluid_channel_portamento_mode) @warning This symbol is not part of the public API and ABI stability guarantee and may change at any time! */
            };

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_set_portamento_mode(IntPtr synth, int chan, int portamentomode);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_get_portamento_mode(IntPtr synth, int chan, int portamentomode);

            /* Interface to breath mode   */

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_set_breath_mode(IntPtr synth, int chan, int breathmode);

            [DllImport(fluidLibName)]
            internal static extern int fluid_synth_get_breath_mode(IntPtr synth, int chan, int breathmode);

#endregion P/INVOKES
        }
#region DMUS structures

        public struct REFERENCE_TIME
        {
            ulong value;
        }

        public struct MUSIC_TIME
        {
            Int32 value;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 56)] // La-Mulana's .sgt file headers are larger for some reason? Changed from 24 from OpenVIII
        private struct DMUS_IO_SEGMENT_HEADER // DirectMusic Version 8
        {
            public uint dwRepeats;
            public MUSIC_TIME mtLength;
            public MUSIC_TIME mtPlayStart;
            public MUSIC_TIME mtLoopStart;
            public MUSIC_TIME mtLoopEnd;
            public uint dwResolution;

            public uint Unknown1;
            public uint Unknown2;
            public uint Unknown3;
            public uint Unknown4;
            public uint Unknown5;
            public uint Unknown6;
            public uint Unknown7;
            public uint Unknown8;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 8)]
        private struct DMUS_IO_VERSION
        {
            private uint dwVersionMS;
            private uint dwVersionLS;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 32)]
        private struct DMUS_IO_TRACK_HEADER
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            private byte[] guidClassID;

            private uint dwPosition;
            private uint dwGroup;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            private char[] _ckid;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            private char[] _fccType;

            public string Ckid => new string(_ckid).Trim('\0');
            public string FccType => new string(_fccType).Trim('\0');
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 8)]
        public struct DMUS_IO_TIMESIGNATURE_ITEM
        {
            public uint lTime;
            public byte bBeatsPerMeasure;
            public byte bBeat;
            public ushort wGridsPerBeat;
        }

        public struct DMUS_IO_TRACK_EXTRAS_HEADER
        {
            public UInt32 dwFlags;
        }

        public struct DMUS_IO_TEMPO_ITEM
        {
            public int lTime;
            public double dblTempo;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 23, CharSet = CharSet.Unicode)]
        private struct DMUS_IO_CHORD
        {
            [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.ByValTStr, SizeConst = 16)] //wchars are used, therefore we need to force 2 bytes per char
            private char[] wszName;

            private uint mtTime;
            private ushort wMeasure;
            private byte bBeat;
            private byte padding;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 18)]
        private struct DMUS_IO_SUBCHORD
        {
            private uint dwChordPattern;
            private uint dwScalePattern;
            private uint dwInversionPoints;
            private uint dwLevels;
            private byte bChordRoot;
            private byte bScaleRoot;
        }

        public struct DMUS_IO_SEQ_ITEM
        {
            public uint mtTime; //EVENT TIME
            public uint mtDuration; //DURATION OF THE EVENT
            public uint dwPChannel;
            public short nOffset; //Grid=Subdivision of a beat. The number of grids per beat is part of the Microsoft® DirectMusic® time signature.
            public byte bStatus; //MIDI event type
            public byte bByte1; //1st MIDI data
            public byte bByte2; //2nd MIDI data
        }

        private struct DMUS_IO_CURVE_ITEM
        {
            private uint mtStart;
            private uint mtDuration;
            private uint mtResetDuration;
            private uint dwPChannel;
            private short nOffset;
            private short nStartValue;
            private short nEndValue;
            private short nResetValue;
            private byte bType;
            private byte bCurveShape;
            private byte bCCData;
            private byte bFlags;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 42)]
        public struct DMUS_IO_INSTRUMENT
        {
            public uint dwPatch;
            public uint dwAssignPatch;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public uint[] dwNoteRanges;

            public uint dwPChannel;
            public uint dwFlags;
            public byte bPan;
            public byte bVolume;
            public short nTranspose;
            public int dwChannelPriority;
        }

        private static DMUS_IO_SEGMENT_HEADER segh = new DMUS_IO_SEGMENT_HEADER();
        private static DMUS_IO_VERSION vers = new DMUS_IO_VERSION();
        public static List<DMUS_IO_TIMESIGNATURE_ITEM> tims;
        private static List<DMUS_IO_TRACK_HEADER> trkh;
        public static DMUS_IO_TEMPO_ITEM tetr;
        public static DMUS_IO_TRACK_EXTRAS_HEADER trkx;
        private static DMUS_IO_CHORD crdh;
        private static List<DMUS_IO_SUBCHORD> crdb;
        public static List<DMUS_IO_SEQ_ITEM> seqt;
        private static List<DMUS_IO_CURVE_ITEM> curl;
        public static List<DMUS_IO_INSTRUMENT> lbinbins;

#endregion DMUS structures

        public Fluid()
        {
            try
            {
                settings = NativeMethods.new_fluid_settings();
            }
            catch (Exception e)
            {
                Debug.WriteLine($"Fluid_Midi disabled: {e.Message} (Check DebugWindows/DebugLinux might be on wrong one)");
                bValid = false;
                return;
            }
#if !_WINDOWS
            //Forces alsa driver- in future we should allow user to choose it by his will
            NativeMethods.fluid_settings_setstr(settings, "audio.driver", "alsa");
#endif
            synth = NativeMethods.new_fluid_synth(settings);
            driver = NativeMethods.new_fluid_audio_driver(settings, synth);
            var music_pt = "Content/music/";
            var dlsName = "gm.dls";// "SanbikiScc.dls";
            var dlsPath = Path.Combine(music_pt, dlsName);
            NativeMethods.fluid_synth_sfload(synth, dlsPath, 1); //we should allow user to choose other SoundFont if he wants to
            player = NativeMethods.new_fluid_player(synth);
            handles = new GCHandle[4];
            handles[0] = GCHandle.Alloc(settings, GCHandleType.Pinned);
            handles[1] = GCHandle.Alloc(synth, GCHandleType.Pinned);
            handles[2] = GCHandle.Alloc(driver, GCHandleType.Pinned);
            handles[3] = GCHandle.Alloc(player, GCHandleType.Pinned);
            NativeMethods.fluid_synth_set_interp_method(synth, -1, NativeMethods.fluid_interp.FLUID_INTERP_HIGHEST);
            //fluid_synth_set_gain(synth, 0.8f);
            var fluidThread = new Thread(new ThreadStart(FluidWorker));
            fluidThread.Start();
        }

        public const int DMUS_PPQ = 768; //DirectMusic PulsePerQuarterNote
        public const int DMUS_MusicTimeMilisecond = 60000000; //not really sure why 60 000 000 instead of 60 000, but it works

        private void FluidWorker()
        {
            while (true)
            {
                switch (fluidState)
                {
                    //we are in the idle mode. We do nothing.
                    case ThreadFluidState.idle:
                        continue;

                    //This is almost the same as idle, but the paused mode is never meant to be destroyed or ignored. In idle mode the engine thinks the player has no song loaded and is available.
                    case ThreadFluidState.paused:
                        continue;

                    //We received the reset state. We have to clear all lists and helpers that were used for playing music.
                    case ThreadFluidState.reset:
                        //FluidWorker_Reset();
                        fluidState = ThreadFluidState.idle;
                        continue;

                    //We received the newSong state. We are resetting as in reset, but in the end we fall into playing
                    case ThreadFluidState.newSong:
                        FluidWorker_ProduceMid();
                        if (player != IntPtr.Zero)
                        {
                            NativeMethods.delete_fluid_player(player);
                            player = NativeMethods.new_fluid_player(synth);
                        }
                        NativeMethods.fluid_player_add_mem(player, midBuffer, (uint)midBuffer.Length);
                        NativeMethods.fluid_player_play(player);
                        fluidState = ThreadFluidState.playing;
                        NativeMethods.fluid_settings_getint(settings, "synth.polyphony", out var val);
                        continue;

                    //The most important state- it handles the real-time transmission to synth driver
                    case ThreadFluidState.playing:
                        //UpdateMusic();
                        continue;

                    case ThreadFluidState.kill:
                        fluidState = ThreadFluidState.idle;
                        Thread.CurrentThread.Abort();
                        break;
                }
            }
        }

        public void FluidWorker_ProduceMid()
        {
            OpenLaMulana.Audio.Midi.MidiProcessor midiProcessor = new OpenLaMulana.Audio.Midi.MidiProcessor(lbinbins, tetr, seqt, tims);
            var mid = midiProcessor.Process();

            //string directory = "";
            string midiFile = "out.mid";// Path.Combine(directory, "out.mid");
            //Directory.CreateDirectory(directory);
            if (File.Exists(midiFile))
                File.Delete(midiFile);

            using (var ms = new MemoryStream())
            {
                // pull request to get this added to naudio
                // https://github.com/naudio/NAudio/pull/499
                OpenLaMulana.Audio.Midi.MidiFile.Export(ms, mid);

                midBuffer = ms.ToArray();
                File.WriteAllBytes(midiFile, midBuffer);
            }
        }

        public void ReadSegmentFileManually(string pt)
        {
            FileStream fs = null;

            // fs is disposed of by binary reader
            using (var br = new BinaryReader(fs = new FileStream(pt, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
            {
                if (ReadFourCc(br) != "RIFF")
                {
                    Debug.WriteLine($"init_debugger_Audio::ReadSegmentFileManually: NOT RIFF!");
                    return;
                }
                fs.Seek(4, SeekOrigin.Current);
                if (ReadFourCc(br) != "DMSG")
                {
                    Debug.WriteLine($"init_debugger_Audio::ReadSegmentFileManually: Broken structure. Expected DMSG!");
                    return;
                }
                ReadSegmentForm(fs, br);
                if (seqt == null)
                {
                    Debug.WriteLine("init_debugger_Audio::ReadSegmentFileManually: Critical error. No sequences read!!!");
                    return;
                }
                fs = null;
            }
        }

        /// <summary>
        /// Major DMUS segment parsing function
        /// </summary>
        /// <param name="fs">filestream of SGT file</param>
        /// <param name="br">binaryreader created from fs</param>
        private static void ReadSegmentForm(FileStream fs, BinaryReader br)
        {
            string fourCc;
            trkh = new List<DMUS_IO_TRACK_HEADER>();
            tims = new List<DMUS_IO_TIMESIGNATURE_ITEM>();
            crdb = new List<DMUS_IO_SUBCHORD>();
            seqt = new List<DMUS_IO_SEQ_ITEM>();
            curl = new List<DMUS_IO_CURVE_ITEM>();
            lbinbins = new List<DMUS_IO_INSTRUMENT>();
            if ((fourCc = ReadFourCc(br)) != "segh")
            { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: Broken structure. Expected segh, got={fourCc}"); return; }
            var chunkSize = br.ReadUInt32();
            if (chunkSize != Marshal.SizeOf(segh))
            { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: chunkSize={chunkSize} is different than DMUS_IO_SEGMENT_HEADER sizeof={Marshal.SizeOf(segh)}"); return; }
            segh = Extended.ByteArrayToStructure<DMUS_IO_SEGMENT_HEADER>(br.ReadBytes((int)chunkSize));
            if ((fourCc = ReadFourCc(br)) != "guid")
            { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: expected guid, got={fourCc}"); return; }
            var guid = br.ReadBytes(br.ReadInt32());
            if ((fourCc = ReadFourCc(br)) != "vers")
            { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: expected vers, got={fourCc}"); return; }
            if ((chunkSize = br.ReadUInt32()) != Marshal.SizeOf(vers))
            { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: vers expected sizeof={Marshal.SizeOf(vers)}, got={chunkSize}"); return; }
            vers = Extended.ByteArrayToStructure<DMUS_IO_VERSION>(br.ReadBytes((int)chunkSize));
            if ((fourCc = ReadFourCc(br)) != "LIST")
            { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: expected LIST, got={fourCc}"); return; }
            //let's skip segment data for now, looks like it's not needed, it's not even oficially a part of segh
            // Figure out how large the chunk is, then skip it
            chunkSize = br.ReadUInt32();
            //this list should now contain metadata like name, authors and etc. It's completely useless in this project scope
            var metadata = new string(br.ReadChars((int)(chunkSize)));
            if ((fourCc = ReadFourCc(br)) != "LIST")
            { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: expected LIST, got={fourCc}"); return; }
            chunkSize = br.ReadUInt32();
            if ((fourCc = ReadFourCc(br)) != "trkl")
            { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: expected trkl, got={fourCc}"); return; }
            //at this point we are free to read the file up to the end by reading all available DMTK RIFFs;
            var eof = (uint)fs.Position + chunkSize - 4;
            while (fs.Position < eof)
            {
                if ((fourCc = ReadFourCc(br)) != "RIFF")
                { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: expected RIFF, got={fourCc}"); return; }
                chunkSize = br.ReadUInt32();
                var skipTell = fs.Position;
                Debug.WriteLine($"RIFF entry: {ReadFourCc(br)}/{ReadFourCc(br)}");
                var trkhEntry = Extended.ByteArrayToStructure<DMUS_IO_TRACK_HEADER>(br.ReadBytes((int)br.ReadUInt32()));
                trkh.Add(trkhEntry);
                var moduleName = string.IsNullOrEmpty(trkhEntry.Ckid) ? trkhEntry.FccType : trkhEntry.Ckid;

                fourCc = ReadFourCc(br);

                if (fourCc == "trkx")
                {
                    // Optional track flags
                    var trkxChunkSize = br.ReadUInt32();
                    trkx = Extended.ByteArrayToStructure<DMUS_IO_TRACK_EXTRAS_HEADER>(br.ReadBytes((int)trkxChunkSize));
                    if (moduleName.ToLower() != "dmbt")
                        fourCc = ReadFourCc(br);
                }

                switch (moduleName.ToLower())
                {
                    case "cord": //Chord track list =[DONE]
                        if (fourCc != "LIST")
                        { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: expected cord/LIST, got={fourCc}"); break; }
                        var cordListChunkSize = br.ReadUInt32();
                        if ((fourCc = ReadFourCc(br)) != "cord")
                        { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: expected cord/cord, got={fourCc}"); break; }
                        if ((fourCc = ReadFourCc(br)) != "crdh")
                        { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: expected cord/crdh, got={fourCc}"); break; }
                        fs.Seek(4, SeekOrigin.Current); //crdh size. It's always one DWORD, so...
                        var crdhDword = br.ReadUInt32();
                        var crdhRoot = (byte)(crdhDword >> 24);
                        var crdhScale = crdhDword & 0xFFFFFF;
                        if ((fourCc = ReadFourCc(br)) != "crdb")
                        { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: expected cord/crdb, got={fourCc}"); break; }
                        var crdbChunkSize = br.ReadUInt32();
                        crdh = Extended.ByteArrayToStructure<DMUS_IO_CHORD>(br.ReadBytes((int)br.ReadUInt32()));
                        var cSubChords = br.ReadUInt32();
                        var subChordSize = br.ReadUInt32();
                        for (var k = 0; k < cSubChords; k++)
                            crdb.Add(Extended.ByteArrayToStructure<DMUS_IO_SUBCHORD>(br.ReadBytes((int)subChordSize)));
                        break;

                    case "tetr":
                        if (fourCc != "tetr")
                        { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: expected tetr/tetr, got={fourCc}"); break; }
                        var tetrChunkSize = br.ReadUInt32();
                        var tetrEntrySize = br.ReadUInt32();
                        fs.Seek(4, SeekOrigin.Current); //???
                        tetr = Extended.ByteArrayToStructure<DMUS_IO_TEMPO_ITEM>(br.ReadBytes((int)tetrEntrySize - 4));
                        var doubleBuffer = BitConverter.GetBytes(tetr.dblTempo);
                        var newDoubleBUffer = new byte[8];
                        Array.Copy(doubleBuffer, 4, newDoubleBUffer, 0, 4);
                        Array.Copy(doubleBuffer, 0, newDoubleBUffer, 4, 4);
                        tetr.dblTempo = BitConverter.ToDouble(newDoubleBUffer, 0);
                        break;

                    case "seqt": //Sequence Track Chunk
                        if (fourCc != "seqt")
                        { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: expected seqt/seqt, got={fourCc}"); break; }
                        var seqtChunkSize = br.ReadUInt32();
                        if ((fourCc = ReadFourCc(br)) != "evtl")
                        { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: expected seqt/evtl, got={fourCc}"); break; }
                        var evtlChunkSize = br.ReadUInt32();
                        var sequenceItemSize = br.ReadUInt32();
                        var sequenceItemsCount = (evtlChunkSize - 4) / sequenceItemSize;
                        for (var k = 0; k < sequenceItemsCount; k++)
                            seqt.Add(Extended.ByteArrayToStructure<DMUS_IO_SEQ_ITEM>(br.ReadBytes((int)sequenceItemSize)));
                        if ((fourCc = ReadFourCc(br)) != "curl")
                        { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: expected seqt/curl, got={fourCc}"); break; }
                        var curlChunkSize = br.ReadUInt32();
                        var curveItemSize = br.ReadUInt32();
                        var curvesItemCount = (curlChunkSize - 4) / curveItemSize;
                        for (var k = 0; k < curvesItemCount; k++)
                            curl.Add(Extended.ByteArrayToStructure<DMUS_IO_CURVE_ITEM>(br.ReadBytes((int)curveItemSize)));
                        break;

                    case "tims": //Time Signature Track List  =[DONE]
                        if (fourCc == "LIST")
                        {
                            // Optional track flags
                            var timsListChunkSize = br.ReadUInt32();
                            fourCc = ReadFourCc(br); // Second "TIMS" before "tims"; just eat it
                            fourCc = ReadFourCc(br);
                        }
                        if (fourCc != "tims")
                        { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: expected tims/tims, got={fourCc}"); break; }
                        var timsChunkSize = br.ReadUInt32();
                        var timsEntrySize = br.ReadUInt32();
                        for (var n = 0; n < (timsChunkSize - 4) / 8; n++)
                            tims.Add(Extended.ByteArrayToStructure<DMUS_IO_TIMESIGNATURE_ITEM>(br.ReadBytes((int)timsEntrySize)));
                        break;

                    case "dmbt": //Band segment
                        fs.Seek(12, SeekOrigin.Current); //We are skipping RIFF and the segment size. Useless for us
                        fourCc = ReadFourCc(br);
                        if (fourCc != "LIST")
                        { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: expected dmbt/LIST, got={fourCc}"); break; }
                        var lbdlChunkSize = br.ReadUInt32();
                        if ((fourCc = ReadFourCc(br)) != "lbdl")
                        { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: expected dmbt/lbdl, got={fourCc}"); break; }
                        if ((fourCc = ReadFourCc(br)) != "LIST")
                        { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: expected dmbt/LIST, got={fourCc}"); break; }
                        _ = br.ReadUInt32();
                        if ((fourCc = ReadFourCc(br)) != "lbnd")
                        { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: expected dmbt/lbnd, got={fourCc}"); break; }
                        fourCc = ReadFourCc(br);
                        if (fourCc != "bdih" && fourCc != "bd2h")
                        { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: expected dmbt/bdih or dmbt/bd2h, got={fourCc}"); break; }
                        fs.Seek(br.ReadUInt32(), SeekOrigin.Current);
                        if ((fourCc = ReadFourCc(br)) != "RIFF")
                        { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: expected dmbt/RIFF, got={fourCc}"); break; }
                        _ = br.ReadUInt32();

                        //Band SEGMENT
                        if ((fourCc = ReadFourCc(br)) != "DMBD")
                        { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: expected dmbt/DMBD, got={fourCc}"); break; }
                        if ((fourCc = ReadFourCc(br)) != "guid")
                        { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: expected dmbt/guid, got={fourCc}"); break; }
                        fs.Seek(br.ReadUInt32(), SeekOrigin.Current); //No one cares for guid

                        if ((fourCc = ReadFourCc(br)) != "LIST")
                        { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: expected dmbt/LIST, got={fourCc}"); break; }
                        fs.Seek(br.ReadUInt32() + 4, SeekOrigin.Current); //we skip the UNFOunam, we don't care for this too
                        var lbilSegmentSize = br.ReadUInt32();
                        var lbilSegment = br.ReadBytes((int)lbilSegmentSize);

                        //now the list is varied- therefore we need to work on the segment and iterate. Let's create memorystream from memory buffer of segment
                        var msB = new MemoryStream(lbilSegment);
                        using (var brB = new BinaryReader(msB))
                        {
                            if (msB.Position == msB.Length)
                                break;
                            if ((fourCc = ReadFourCc(brB)) != "lbil")
                            { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: expected dmbt/lbil, got={fourCc}"); break; }
                            while (true) //this is LIST loop. Always starts with loop and determines the segment true data by the sizeof
                            {
                                if ((fourCc = ReadFourCc(brB)) != "LIST")
                                { if (msB.Position == msB.Length) break; else { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: expected dmbt/LIST, got={fourCc}"); break; } }
                                var listBufferSize = brB.ReadUInt32();
                                if (listBufferSize != 52)
                                {
                                    msB.Seek(listBufferSize, SeekOrigin.Current); //the other data is useless for us. We want the bands only.
                                    //Actually there's pointer to DLS file, but who is going to replace the DLS in file when you can do it much easier
                                    //with a mod manager/code modification. You can change the constant FF8.DLS to other filename somewhere above here.
                                    continue;
                                }
                                else
                                {
                                    var bandHeader = $"{ReadFourCc(brB)}{ReadFourCc(brB)}";
                                    if (bandHeader != "lbinbins")
                                    { Debug.WriteLine($"init_debugger_Audio::ReadSegmentForm: the band LIST reader got this magic: {bandHeader} instead of lbinbins"); break; }
                                    var sizeofDMUS_IO_INSTRUMENT = brB.ReadUInt32();
                                    var instrument = Extended.ByteArrayToStructure<DMUS_IO_INSTRUMENT>(brB.ReadBytes((int)sizeofDMUS_IO_INSTRUMENT));
                                    lbinbins.Add(instrument);
                                    continue;
                                }
                            }
                            msB = null;
                        }
                        break;

                    default:
                        break;
                }
                fs.Seek(skipTell + chunkSize, SeekOrigin.Begin);
            }
        }

        private static string ReadFourCc(BinaryReader br) => new string(br.ReadChars(4));

        public void Play() => fluidState = ThreadFluidState.newSong;

        public void Stop()
        {
            if (!bValid)
                return;
            NativeMethods.fluid_synth_all_notes_off(synth, -1);
            NativeMethods.fluid_player_stop(player);
            fluidState = ThreadFluidState.idle;
        }

#region IDisposable Support

        private bool disposedValue = false; // To detect redundant calls

        private void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects).
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                Stop();
                fluidState = ThreadFluidState.kill;
                if (handles != null)
                    foreach (var hwnd in handles)
                        hwnd.Free();
                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        ~Fluid()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(false);
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            GC.SuppressFinalize(this);
        }

#endregion IDisposable Support
    }
}
