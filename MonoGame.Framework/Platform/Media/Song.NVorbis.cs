// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using System;
using System.IO;
using Microsoft.Xna.Framework.Audio;
using MonoGame.OpenAL;

namespace Microsoft.Xna.Framework.Media
{
    public sealed partial class Song : IEquatable<Song>, IDisposable
    {
        private OggStream stream;
        private float _volume = 1f;
        private readonly object _sourceMutex = new object();

        [System.Diagnostics.Conditional("DEBUG")]
        private static void Log(string message)
        {
            System.Console.WriteLine("SongTrace: " + message);
        }

        private void PlatformInitialize(string fileName)
        {
            Log("PlatformInitialize: file=" + fileName);
            // init OpenAL if need be
            OpenALSoundController.EnsureInitialized();
            Log("PlatformInitialize: OpenAL initialized");

            stream = new OggStream(fileName, OnFinishedPlaying);
            stream.Prepare();
            Log("PlatformInitialize: stream prepared");

            _duration = stream.GetLength();
            Log("PlatformInitialize: duration=" + _duration);
        }
        
        internal void SetEventHandler(FinishedPlayingHandler handler) { }

        internal void OnFinishedPlaying()
        {
            MediaPlayer.OnSongFinishedPlaying(null, null);
        }
		
        void PlatformDispose(bool disposing)
        {
            lock (_sourceMutex)
            {
                if (stream == null)
                    return;

                stream.Dispose();
                stream = null;
            }
        }

        internal void Play(TimeSpan? startPosition)
        {
            if (stream == null)
                return;

            Log("Play: startPosition=" + (startPosition.HasValue ? startPosition.Value.ToString() : "<null>"));
            stream.Play();
            if (startPosition != null)
                stream.SeekToPosition((TimeSpan)startPosition);

            _playCount++;
            Log("Play: playCount=" + _playCount);
        }

        internal void Resume()
        {
            if (stream == null)
                return;

            Log("Resume");
            stream.Resume();
        }

        internal void Pause()
        {
            if (stream == null)
                return;

            Log("Pause");
            stream.Pause();
        }

        internal void Stop()
        {
            if (stream == null)
                return;

            Log("Stop");
            stream.Stop();
            _playCount = 0;
        }

        internal float Volume
        {
            get
            {
                if (stream == null)
                    return 0.0f;
                return _volume; 
            }
            set
            {
                _volume = value;
                if (stream != null)
                    stream.Volume = _volume;
            }
        }

        public TimeSpan Position
        {
            get
            {
                if (stream == null)
                    return TimeSpan.FromSeconds(0.0);
                return stream.GetPosition();
            }
        }

        private Album PlatformGetAlbum()
        {
            return null;
        }

        private Artist PlatformGetArtist()
        {
            return null;
        }

        private Genre PlatformGetGenre()
        {
            return null;
        }

        private TimeSpan PlatformGetDuration()
        {
            return _duration;
        }

        private bool PlatformIsProtected()
        {
            return false;
        }

        private bool PlatformIsRated()
        {
            return false;
        }

        private string PlatformGetName()
        {
            return Path.GetFileNameWithoutExtension(_name);
        }

        private int PlatformGetPlayCount()
        {
            return _playCount;
        }

        private int PlatformGetRating()
        {
            return 0;
        }

        private int PlatformGetTrackNumber()
        {
            return 0;
        }
    }
}
