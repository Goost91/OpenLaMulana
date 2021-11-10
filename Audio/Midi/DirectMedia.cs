﻿using System;
using System.IO;
using Microsoft.Xna.Framework;

#if _WINDOWS && !_X64

using DirectMidi;

#endif

using System.Diagnostics;

namespace OpenLaMulana.Audio.Midi
{
    /// <summary>
    /// Direct Music
    /// </summary>
    /// <see cref="http://directmidi.sourceforge.net/"/>
    /// <seealso cref="http://directmidinet.sourceforge.net/"/>
    public sealed class DirectMedia : IDisposable
    {
#if _WINDOWS && (_X86 || !_X64)
        private CDirectMusic cdm;
        private CDLSLoader loader;
        private CSegment segment;
        private CAPathPerformance path;
        private CPortPerformance cport; //public explicit
        private COutputPort outport;
        private CCollection ccollection;
        private CInstrument[] instruments;

        public const int S_OK = 0x00000000;

        public void Play(string pt, bool loop = true)
        {
            if (cdm == null)
            {
                cdm = new CDirectMusic();
                cdm.Initialize();
                loader = new CDLSLoader();
                loader.Initialize();
                loader.LoadSegment(pt, out segment);
                ccollection = new CCollection();
                string pathDLS = "Content/music/SanbikiSCC.dls";
                if (!File.Exists(pathDLS))
                {
                    pathDLS = "Content/music/SanbikiSCC.dls";
                }

                loader.LoadDLS(pathDLS, out ccollection);
                uint dwInstrumentIndex = 0;
                while (ccollection.EnumInstrument(++dwInstrumentIndex, out INSTRUMENTINFO iInfo) == S_OK)
                {
                    Debug.WriteLine(iInfo.szInstName);
                }
                instruments = new CInstrument[dwInstrumentIndex];

                path = new CAPathPerformance();
                path.Initialize(cdm, null, null, DMUS_APATH.DYNAMIC_3D, 128);
                cport = new CPortPerformance();
                cport.Initialize(cdm, null, null);
                outport = new COutputPort();
                outport.Initialize(cdm);

                uint dwPortCount = 0;
                INFOPORT infoport;
                do
                {
                    outport.GetPortInfo(++dwPortCount, out infoport);
                }
                while ((infoport.dwFlags & DMUS_PC.SOFTWARESYNTH) == 0);

                outport.SetPortParams(0, 0, 0, DirectMidi.SET.REVERB | DirectMidi.SET.CHORUS, 44100);
                outport.ActivatePort(infoport);

                cport.AddPort(outport, 0, 1);

                for (int i = 0; i < dwInstrumentIndex; i++)
                {
                    ccollection.GetInstrument(out instruments[i], i);
                    outport.DownloadInstrument(instruments[i]);
                }
                segment.Download(cport);
                if(!loop)
                    segment.SetRepeats(0);

                cport.PlaySegment(segment);
            }
            else
            {
                cport.Stop(segment);
                segment.Dispose();
                //segment.ConnectToDLS
                loader.LoadSegment(pt, out segment);
                segment.Download(cport);
                if (!loop)
                    segment.SetRepeats(0);
                cport.PlaySegment(segment);
                cdm.Dispose();
            }

            //GCHandle.Alloc(cdm, GCHandleType.Pinned);
            //GCHandle.Alloc(loader, GCHandleType.Pinned);
            //GCHandle.Alloc(segment, GCHandleType.Pinned);
            //GCHandle.Alloc(path, GCHandleType.Pinned);
            //GCHandle.Alloc(cport, GCHandleType.Pinned);
            //GCHandle.Alloc(outport, GCHandleType.Pinned);
            //GCHandle.Alloc(infoport, GCHandleType.Pinned);
        }

        public void Stop() => cport.StopAll();

        public void Dispose()
        {
            Stop();
            segment.Dispose();
            instruments.ForEach(action => action.Dispose());
            cport.Dispose();
            ccollection.Dispose();
            loader.Dispose();
            outport.Dispose();
            path.Dispose();
            cdm.Dispose();
        }

#else
        public void Play(string pt)
        {}
        public void Stop()
        {}
        public void Dispose()
        {}
#endif
    }
}