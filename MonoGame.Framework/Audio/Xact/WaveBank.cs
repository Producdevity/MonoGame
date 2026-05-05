// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using System;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace Microsoft.Xna.Framework.Audio
{
    /// <summary>Represents a collection of wave files.</summary>
    public partial class WaveBank : IDisposable
    {
        [System.Diagnostics.Conditional("DEBUG")]
        private static void Log(string message)
        {
            System.Console.WriteLine("XactTrace: " + message);
        }

        private readonly SoundEffect[] _sounds;
        private readonly StreamInfo[] _streams;
        private readonly string _bankName;
        private readonly string _waveBankFileName;
        private readonly bool _streaming;
        private readonly int _offset;
        private readonly int _packetSize;

        private readonly int _version;
        private readonly int _playRegionOffset;
        private MemoryMappedFile _waveBankMap;
        private MemoryMappedViewAccessor _waveBankView;
        private IntPtr _waveBankData;
        private readonly object _loadLock = new object();

        struct Segment
        {
            public int Offset;
            public int Length;
        }

        struct WaveBankHeader
        {
            public int Version;
            public Segment[] Segments;
        }

        struct WaveBankData
        {
            public int    Flags;                                // Bank flags
            public int    EntryCount;                           // Number of entries in the bank
            public string BankName;                             // Bank friendly name
            public int    EntryMetaDataElementSize;             // Size of each entry meta-data element, in bytes
            public int    EntryNameElementSize;                 // Size of each entry name element, in bytes
            public int    Alignment;                            // Entry alignment, in bytes
            public int    CompactFormat;                        // Format data for compact bank
            public int    BuildTime;                            // Build timestamp
        }

        struct StreamInfo
        {
            public int Format;
            public int FileOffset;
            public int FileLength;
            public int LoopStart;
            public int LoopLength;
        }

        private const int Flag_EntryNames = 0x00010000; // Bank includes entry names
        private const int Flag_Compact = 0x00020000; // Bank uses compact format
        private const int Flag_SyncDisabled = 0x00040000; // Bank is disabled for audition sync
        private const int Flag_SeekTables = 0x00080000; // Bank includes seek tables.
        private const int Flag_Mask = 0x000F0000;

        /// <summary>
        /// </summary>
        public bool IsInUse { get; private set; }

        /// <summary>
        /// </summary>
        public bool IsPrepared { get; private set; }

        /// <param name="audioEngine">Instance of the AudioEngine to associate this wave bank with.</param>
        /// <param name="nonStreamingWaveBankFilename">Path to the .xwb file to load.</param>
        /// <remarks>This constructor immediately loads all wave data into memory at once.</remarks>
        public WaveBank(AudioEngine audioEngine, string nonStreamingWaveBankFilename)
            : this(audioEngine, nonStreamingWaveBankFilename, false, 0, 0)
        {
        }

        // TODO: Clean this mess up when we figure out what parts we don't need
        private WaveBank(AudioEngine audioEngine, string waveBankFilename, bool streaming, int offset, int packetsize)
        {
            if (audioEngine == null)
                throw new ArgumentNullException("audioEngine");
            if (string.IsNullOrEmpty(waveBankFilename))
                throw new ArgumentNullException("nonStreamingWaveBankFilename");

            _streaming = streaming;
            _offset = offset;
            _packetSize = packetsize;

            try
            {
                // Is this a streaming wavebank?
                if (streaming)
                {
                    if (offset != 0)
                        throw new ArgumentException("We only support a zero offset in streaming banks.", "offset");
                    if (packetsize < 2)
                        throw new ArgumentException("The packet size must be greater than 2.", "packetsize");

                    // DesktopGL doesn't implement PlatformCreateStream(). Keep
                    // streamed XACT banks on the same mapped-file path used by
                    // resident banks, with per-wave streaming handled later.
                    Log("WaveBank streaming fallback file=" + waveBankFilename + " offset=" + offset + " packetSize=" + packetsize);
                }
                else
                {
                    Log("WaveBank ctor file=" + waveBankFilename + " streaming=false");
                }

                //XWB PARSING
                //Adapted from MonoXNA
                //Originally adaped from Luigi Auriemma's unxwb

                WaveBankHeader wavebankheader;
                WaveBankData wavebankdata;

                wavebankdata.EntryNameElementSize = 0;
                wavebankdata.CompactFormat = 0;
                wavebankdata.Alignment = 0;
                wavebankdata.BuildTime = 0;

                int wavebank_offset = 0;

                _waveBankFileName = waveBankFilename;

                var baseStream = AudioEngine.OpenStream(waveBankFilename);
                Log(
                    "WaveBank stream opened file=" + waveBankFilename
                    + " type=" + baseStream.GetType().FullName
                    + " canSeek=" + baseStream.CanSeek
                    + " length=" + (baseStream.CanSeek ? baseStream.Length.ToString() : "-1"));

                BinaryReader reader = new BinaryReader(baseStream);

                var signature = reader.ReadBytes(4);
                Log("WaveBank signature file=" + waveBankFilename + " bytes=" + BitConverter.ToString(signature));

                _version = wavebankheader.Version = reader.ReadInt32();
                Log("WaveBank version file=" + waveBankFilename + " version=" + _version);

                int last_segment = 4;
                //if (wavebankheader.Version == 1) goto WAVEBANKDATA;
                if (wavebankheader.Version <= 3) last_segment = 3;
                if (wavebankheader.Version >= 42)
                {
                    var headerVersion = reader.ReadInt32();    // skip HeaderVersion
                    Log("WaveBank headerVersion file=" + waveBankFilename + " headerVersion=" + headerVersion);
                }

                wavebankheader.Segments = new Segment[5];

                for (int i = 0; i <= last_segment; i++)
                {
                    wavebankheader.Segments[i].Offset = reader.ReadInt32();
                    wavebankheader.Segments[i].Length = reader.ReadInt32();
                }

                reader.BaseStream.Seek(wavebankheader.Segments[0].Offset, SeekOrigin.Begin);

                //WAVEBANKDATA:

                wavebankdata.Flags = reader.ReadInt32();
                wavebankdata.EntryCount = reader.ReadInt32();

                if ((wavebankheader.Version == 2) || (wavebankheader.Version == 3))
                {
                    wavebankdata.BankName = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(16),0,16).Replace("\0", "");
                }
                else
                {
                    wavebankdata.BankName = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(64),0,64).Replace("\0", "");
                }

                _bankName = wavebankdata.BankName;

                if (wavebankheader.Version == 1)
                {
                    //wavebank_offset = (int)ftell(fd) - file_offset;
                    wavebankdata.EntryMetaDataElementSize = 20;
                }
                else
                {
                    wavebankdata.EntryMetaDataElementSize = reader.ReadInt32();
                    wavebankdata.EntryNameElementSize = reader.ReadInt32();
                    wavebankdata.Alignment = reader.ReadInt32();
                    wavebank_offset = wavebankheader.Segments[1].Offset; //METADATASEGMENT
                }

                if ((wavebankdata.Flags & Flag_Compact) != 0)
                {
                    reader.ReadInt32(); // compact_format
                }

                _playRegionOffset = wavebankheader.Segments[last_segment].Offset;
                if (_playRegionOffset == 0)
                {
                    _playRegionOffset =
                        wavebank_offset +
                        (wavebankdata.EntryCount * wavebankdata.EntryMetaDataElementSize);
                }

                Log(
                    "WaveBank metadata"
                    + " name=" + _bankName
                    + " version=" + _version
                    + " entries=" + wavebankdata.EntryCount
                    + " flags=0x" + wavebankdata.Flags.ToString("X")
                    + " metaSize=" + wavebankdata.EntryMetaDataElementSize
                    + " playRegionOffset=" + _playRegionOffset);

                int segidx_entry_name = 2;
                if (wavebankheader.Version >= 42) segidx_entry_name = 3;

                if ((wavebankheader.Segments[segidx_entry_name].Offset != 0) &&
                    (wavebankheader.Segments[segidx_entry_name].Length != 0))
                {
                    if (wavebankdata.EntryNameElementSize == -1) wavebankdata.EntryNameElementSize = 0;
                    byte[] entry_name = new byte[wavebankdata.EntryNameElementSize + 1];
                    entry_name[wavebankdata.EntryNameElementSize] = 0;
                }

                _sounds = new SoundEffect[wavebankdata.EntryCount];
                _streams = new StreamInfo[wavebankdata.EntryCount];

                reader.BaseStream.Seek(wavebank_offset, SeekOrigin.Begin);

                // The compact format requires us to load stuff differently.
                var isCompactFormat = (wavebankdata.Flags & Flag_Compact) != 0;
                if (isCompactFormat)
                {
                    // Load the sound data offset table from disk.
                    for (var i = 0; i < wavebankdata.EntryCount; i++)
                    {
                        var len = reader.ReadInt32();
                        _streams[i].Format = wavebankdata.CompactFormat;
                        _streams[i].FileOffset = (len & ((1 << 21) - 1))*wavebankdata.Alignment;
                    }

                    // Now figure out the sound data lengths.
                    for (var i = 0; i < wavebankdata.EntryCount; i++)
                    {
                        int nextOffset;
                        if (i == (wavebankdata.EntryCount - 1))
                            nextOffset = wavebankheader.Segments[last_segment].Length;
                        else
                            nextOffset = _streams[i + 1].FileOffset;

                        // The next and current offsets used to calculate the length.
                        _streams[i].FileLength = nextOffset - _streams[i].FileOffset;
                    }
                }
                else
                {
                    for (var i = 0; i < wavebankdata.EntryCount; i++)
                    {
                        var info = new StreamInfo();
                        if (wavebankheader.Version == 1)
                        {
                            info.Format = reader.ReadInt32();
                            info.FileOffset = reader.ReadInt32();
                            info.FileLength = reader.ReadInt32();
                            info.LoopStart = reader.ReadInt32();
                            info.LoopLength = reader.ReadInt32();
                        }
                        else
                        {
                            var flagsAndDuration = reader.ReadInt32(); // Unused

                            if (wavebankdata.EntryMetaDataElementSize >= 8)
                                info.Format = reader.ReadInt32();
                            if (wavebankdata.EntryMetaDataElementSize >= 12)
                                info.FileOffset = reader.ReadInt32();
                            if (wavebankdata.EntryMetaDataElementSize >= 16)
                                info.FileLength = reader.ReadInt32();
                            if (wavebankdata.EntryMetaDataElementSize >= 20)
                                info.LoopStart = reader.ReadInt32();
                            if (wavebankdata.EntryMetaDataElementSize >= 24)
                                info.LoopLength = reader.ReadInt32();
                        }

                        // TODO: What is this doing?
                        if (wavebankdata.EntryMetaDataElementSize < 24)
                        {
                            if (info.FileLength != 0)
                                info.FileLength = wavebankheader.Segments[last_segment].Length;
                        }

                        _streams[i] = info;
                    }
                }

                if (!_streaming)
                {
                    _waveBankMap = MemoryMappedFile.CreateFromFile(_waveBankFileName, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
                    _waveBankView = _waveBankMap.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

                    unsafe
                    {
                        byte* pointer = null;
                        _waveBankView.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
                        _waveBankData = (IntPtr)pointer;
                    }

                    Log("WaveBank resident mode using mapped lazy track loads file=" + waveBankFilename + " entries=" + _streams.Length);
                }

                audioEngine.Wavebanks[_bankName] = this;
                Log("WaveBank ready name=" + _bankName + " entries=" + _sounds.Length + " streaming=" + _streaming);

                IsPrepared = true;
            }
            catch (Exception ex)
            {
                try
                {
                    Log(
                        "WaveBank ctor failed file=" + waveBankFilename
                        + " type=" + ex.GetType().FullName
                        + " message=" + ex.Message);
                }
                catch
                {
                }
                throw;
            }
        }

        private void DecodeFormat(int format, out MiniFormatTag codec, out int channels, out int rate, out int alignment)
        {
            if (_version == 1)
            {
                // I'm not 100% sure if the following is correct
                // version 1:
                // 1 00000000 000101011000100010 0 001 0
                // | |         |                 | |   |
                // | |         |                 | |   wFormatTag
                // | |         |                 | nChannels
                // | |         |                 ???
                // | |         nSamplesPerSec
                // | wBlockAlign
                // wBitsPerSample

                codec = (MiniFormatTag)((format) & ((1 << 1) - 1));
                channels = (format >> (1)) & ((1 << 3) - 1);
                rate = (format >> (1 + 3 + 1)) & ((1 << 18) - 1);
                alignment = (format >> (1 + 3 + 1 + 18)) & ((1 << 8) - 1);
                //bits = (format >> (1 + 3 + 1 + 18 + 8)) & ((1 << 1) - 1);

                /*} else if(wavebankheader.dwVersion == 23) { // I'm not 100% sure if the following is correct
                    // version 23:
                    // 1000000000 001011101110000000 001 1
                    // | |        |                  |   |
                    // | |        |                  |   ???
                    // | |        |                  nChannels?
                    // | |        nSamplesPerSec
                    // | ???
                    // !!!UNKNOWN FORMAT!!!

                    //codec = -1;
                    //chans = (wavebankentry.Format >>  1) & ((1 <<  3) - 1);
                    //rate  = (wavebankentry.Format >>  4) & ((1 << 18) - 1);
                    //bits  = (wavebankentry.Format >> 31) & ((1 <<  1) - 1);
                    codec = (wavebankentry.Format                    ) & ((1 <<  1) - 1);
                    chans = (wavebankentry.Format >> (1)             ) & ((1 <<  3) - 1);
                    rate  = (wavebankentry.Format >> (1 + 3)         ) & ((1 << 18) - 1);
                    align = (wavebankentry.Format >> (1 + 3 + 18)    ) & ((1 <<  9) - 1);
                    bits  = (wavebankentry.Format >> (1 + 3 + 18 + 9)) & ((1 <<  1) - 1); */

            }
            else
            {
                // 0 00000000 000111110100000000 010 01
                // | |        |                  |   |
                // | |        |                  |   wFormatTag
                // | |        |                  nChannels
                // | |        nSamplesPerSec
                // | wBlockAlign
                // wBitsPerSample

                codec = (MiniFormatTag)((format) & ((1 << 2) - 1));
                channels = (format >> (2)) & ((1 << 3) - 1);
                rate = (format >> (2 + 3)) & ((1 << 18) - 1);
                alignment = (format >> (2 + 3 + 18)) & ((1 << 8) - 1);
                //bits = (info.Format >> (2 + 3 + 18 + 8)) & ((1 << 1) - 1);
            }
        }

        /// <param name="audioEngine">Instance of the AudioEngine to associate this wave bank with.</param>
        /// <param name="streamingWaveBankFilename">Path to the .xwb to stream from.</param>
        /// <param name="offset">DVD sector-aligned offset within the wave bank data file.</param>
        /// <param name="packetsize">Stream packet size, in sectors, to use for each stream. The minimum value is 2.</param>
        /// <remarks>
        /// <para>This constructor streams wave data as needed.</para>
        /// <para>Note that packetsize is in sectors, which is 2048 bytes.</para>
        /// <para>AudioEngine.Update() must be called at least once before using data from a streaming wave bank.</para>
        /// </remarks>
        public WaveBank(AudioEngine audioEngine, string streamingWaveBankFilename, int offset, short packetsize)
            : this(audioEngine, streamingWaveBankFilename, true, offset, packetsize)
        {
        }

        internal SoundEffectInstance GetSoundEffectInstance(int trackIndex, out bool streaming)
        {
            if (_streaming)
            {
                streaming = true;
                var stream = _streams[trackIndex];
                Log("WaveBank.GetSoundEffectInstance streamed track=" + trackIndex);
                return PlatformCreateStream(stream);
            }
            else
            {
                EnsureResidentSoundLoaded(trackIndex);
                var sound = _sounds[trackIndex];
                streaming = sound.SoundBufferStreamed != null;
                Log("WaveBank.GetSoundEffectInstance resident track=" + trackIndex);
                return sound.GetPooledInstance(true);
            }
        }

        internal SoundEffect GetSoundEffect(int trackIndex)
        {
            if (_streaming)
                throw new NotSupportedException("Cannot return a resident SoundEffect from a streaming wave bank.");

            EnsureResidentSoundLoaded(trackIndex);
            return _sounds[trackIndex];
        }

        private void EnsureResidentSoundLoaded(int trackIndex)
        {
            if (_sounds[trackIndex] != null)
                return;

            lock (_loadLock)
            {
                if (_sounds[trackIndex] != null)
                    return;

                var info = _streams[trackIndex];

                MiniFormatTag codec;
                int channels, rate, alignment;
                DecodeFormat(info.Format, out codec, out channels, out rate, out alignment);

                Log(
                    "WaveBank lazy load track="
                    + trackIndex
                    + " codec=" + codec
                    + " channels=" + channels
                    + " rate=" + rate
                    + " alignment=" + alignment
                    + " offset=" + info.FileOffset
                    + " length=" + info.FileLength
                    + " loopStart=" + info.LoopStart
                    + " loopLength=" + info.LoopLength);

                if (_waveBankData == IntPtr.Zero)
                    throw new InvalidOperationException("Wave bank memory map is not available for resident audio.");

                var absoluteOffset = info.FileOffset + _playRegionOffset;
                var audioPointer = IntPtr.Add(_waveBankData, absoluteOffset);
                _sounds[trackIndex] = new SoundEffect(codec, audioPointer, info.FileLength, channels, rate, alignment, info.LoopStart, info.LoopLength);
            }
        }

        /// <summary>
        /// This event is triggered when the WaveBank is disposed.
        /// </summary>
        public event EventHandler<EventArgs> Disposing;

        /// <summary>
        /// Is true if the WaveBank has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Disposes the WaveBank.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~WaveBank()
        {
            Dispose(false);
        }

        private void Dispose(bool disposing)
        {
            if (IsDisposed)
                return;

            IsDisposed = true;

            if (disposing)
            {
                foreach (var s in _sounds)
                    if (s != null)
                        s.Dispose();

                if (_waveBankView != null)
                {
                    if (_waveBankData != IntPtr.Zero)
                    {
                        _waveBankView.SafeMemoryMappedViewHandle.ReleasePointer();
                        _waveBankData = IntPtr.Zero;
                    }

                    _waveBankView.Dispose();
                    _waveBankView = null;
                }

                if (_waveBankMap != null)
                {
                    _waveBankMap.Dispose();
                    _waveBankMap = null;
                }

                IsPrepared = false;
                IsInUse = false;
                EventHelpers.Raise(this, Disposing, EventArgs.Empty);
            }
        }
    }
}
