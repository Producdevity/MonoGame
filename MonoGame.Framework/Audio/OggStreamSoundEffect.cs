// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using System;
using NVorbis;

namespace Microsoft.Xna.Framework.Audio
{
    public class OggStreamSoundEffect : SoundEffect
    {
        private readonly string _oggFileName;
        private readonly int _sampleRate;
        private readonly AudioChannels _channels;

        public OggStreamSoundEffect(string oggFileName)
        {
            if (string.IsNullOrEmpty(oggFileName))
                throw new ArgumentNullException("oggFileName");

            _oggFileName = oggFileName;

            using (var reader = new VorbisReader(oggFileName))
            {
                if (reader.Channels != 1 && reader.Channels != 2)
                    throw new NotSupportedException("Only mono and stereo Vorbis streams are supported.");

                _duration = reader.TotalTime;
                _sampleRate = reader.SampleRate;
                _channels = (AudioChannels)reader.Channels;
            }
        }

        public override SoundEffectInstance GetPooledInstance(bool forXAct)
        {
            var instance = new OggStreamSoundEffectInstance(_oggFileName, _sampleRate, _channels);
            instance._effect = this;
            instance._isPooled = false;
            instance._isXAct = forXAct;
            return instance;
        }
    }

    internal sealed class OggStreamSoundEffectInstance : SoundEffectInstance
    {
        private readonly string _oggFileName;
        private readonly int _sampleRate;
        private readonly AudioChannels _channels;
        private OggStream _stream;
        private bool _looped;

        internal OggStreamSoundEffectInstance(string oggFileName, int sampleRate, AudioChannels channels)
        {
            _oggFileName = oggFileName;
            _sampleRate = sampleRate;
            _channels = channels;
        }

        public override bool IsLooped
        {
            get { return _looped; }
            set
            {
                _looped = value;
                if (_stream != null)
                    _stream.IsLooped = value;
            }
        }

        public override float Pan
        {
            get { return base.Pan; }
            set
            {
                base.Pan = value;
                if (_stream != null)
                    _stream.Pan = value;
            }
        }

        public override float Pitch
        {
            get { return base.Pitch; }
            set
            {
                base.Pitch = value;
                if (_stream != null)
                    _stream.Pitch = value;
            }
        }

        public override float Volume
        {
            get { return base.Volume; }
            set
            {
                base.Volume = value;
                if (_stream != null)
                    _stream.Volume = value;
            }
        }

        public override SoundState State
        {
            get { return _stream == null ? SoundState.Stopped : _stream.State; }
        }

        public override void Play()
        {
            SoundEffect.Initialize();
            if (SoundEffect._systemState != SoundEffect.SoundSystemState.Initialized)
                throw new NoAudioHardwareException("Audio has failed to initialize. Call SoundEffect.Initialize() before sound operation to get more specific errors.");

            if (_sampleRate < 8000 || _sampleRate > 48000)
                throw new ArgumentOutOfRangeException("sampleRate");
            if (_channels != AudioChannels.Mono && _channels != AudioChannels.Stereo)
                throw new ArgumentOutOfRangeException("channels");

            if (_stream == null)
                CreateStream();

            if (_stream.State == SoundState.Paused)
            {
                _stream.Resume();
                return;
            }

            _stream.Play();
        }

        public override void Pause()
        {
            if (_stream != null)
                _stream.Pause();
        }

        public override void Resume()
        {
            if (_stream == null)
                Play();
            else
                _stream.Resume();
        }

        public override void Stop()
        {
            Stop(true);
        }

        public override void Stop(bool immediate)
        {
            CloseStream();
        }

        protected override void Dispose(bool disposing)
        {
            CloseStream();
            base.Dispose(disposing);
        }

        private void CreateStream()
        {
            _stream = new OggStream(_oggFileName);
            _stream.IsLooped = _looped;
            _stream.Volume = Volume;
            _stream.Pitch = Pitch;
            _stream.Pan = Pan;
        }

        private void CloseStream()
        {
            if (_stream == null)
                return;

            _stream.Dispose();
            _stream = null;
        }
    }
}
