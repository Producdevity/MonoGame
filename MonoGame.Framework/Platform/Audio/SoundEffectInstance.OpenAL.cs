// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using System;
using MonoGame.OpenAL;

namespace Microsoft.Xna.Framework.Audio
{
    public partial class SoundEffectInstance : IDisposable
    {
        [System.Diagnostics.Conditional("DEBUG")]
        private static void Log(string message)
        {
            System.Console.WriteLine("AudioTrace: " + message);
        }

		internal SoundState SoundState = SoundState.Stopped;
		private bool _looped = false;
		private float _alVolume = 1f;

		internal int SourceId;
        private float reverb = 0f;
        bool applyFilter = false;
        EfxFilterType filterType;
        float filterQ;
        float frequency;
        int pauseCount;
        int[] buffers;
        long currentBufferPosition;

        internal readonly object sourceMutex = new object();
        
        internal OpenALSoundController controller;
        
        internal bool HasSourceId = false;

#region Initialization

        /// <summary>
        /// Creates a standalone SoundEffectInstance from given wavedata.
        /// </summary>
        internal void PlatformInitialize(byte[] buffer, int sampleRate, int channels)
        {
            InitializeSound();
        }

        /// <summary>
        /// Gets the OpenAL sound controller, constructs the sound buffer, and sets up the event delegates for
        /// the reserved and recycled events.
        /// </summary>
        internal void InitializeSound()
        {
            controller = OpenALSoundController.Instance;
        }

#endregion // Initialization

        /// <summary>
        /// Converts the XNA [-1, 1] pitch range to OpenAL pitch (0, INF) or Android SoundPool playback rate [0.5, 2].
        /// <param name="xnaPitch">The pitch of the sound in the Microsoft XNA range.</param>
        /// </summary>
        private static float XnaPitchToAlPitch(float xnaPitch)
        {
            return (float)Math.Pow(2, xnaPitch);
        }

        private void PlatformApply3D(AudioListener listener, AudioEmitter emitter)
        {
            if (!HasSourceId)
                return;
            // get AL's listener position
            float x, y, z;
            AL.GetListener(ALListener3f.Position, out x, out y, out z);
            ALHelper.CheckError("Failed to get source position.");

            // get the emitter offset from origin
            Vector3 posOffset = emitter.Position - listener.Position;
            // set up orientation matrix
            Matrix orientation = Matrix.CreateWorld(Vector3.Zero, listener.Forward, listener.Up);
            // set up our final position and velocity according to orientation of listener
            Vector3 finalPos = new Vector3(x + posOffset.X, y + posOffset.Y, z + posOffset.Z);
            finalPos = Vector3.Transform(finalPos, orientation);
            Vector3 finalVel = emitter.Velocity;
            finalVel = Vector3.Transform(finalVel, orientation);

            // set the position based on relative positon
            AL.Source(SourceId, ALSource3f.Position, finalPos.X, finalPos.Y, finalPos.Z);
            ALHelper.CheckError("Failed to set source position.");
            AL.Source(SourceId, ALSource3f.Velocity, finalVel.X, finalVel.Y, finalVel.Z);
            ALHelper.CheckError("Failed to set source velocity.");

            AL.Source(SourceId, ALSourcef.ReferenceDistance, SoundEffect.DistanceScale);
            ALHelper.CheckError("Failed to set source distance scale.");
            AL.DopplerFactor(SoundEffect.DopplerScale);
            ALHelper.CheckError("Failed to set Doppler scale.");
        }

        private void PlatformPause()
        {
            if (!HasSourceId || SoundState != SoundState.Playing)
                return;

            if (pauseCount == 0)
            {
                AL.SourcePause(SourceId);
                ALHelper.CheckError("Failed to pause source.");
            }
            ++pauseCount;
            SoundState = SoundState.Paused;
        }

        private void PlatformPlay()
        {
            if (_effect.SoundBufferStreamed != null)
            {
                PlayStreamed();
                return;
            }

            PlayMemoryResident();
        }

        private void PlayMemoryResident()
        {
            Log("SoundEffectInstance.PlayMemoryResident start");
            SourceId = 0;
            HasSourceId = false;
            SourceId = controller.ReserveSource();
            HasSourceId = true;

            int bufferId = _effect.SoundBuffer.OpenALDataBuffer;
            Log(
                "SoundEffectInstance.PlayMemoryResident buffer=" + bufferId
                + " format=" + _effect.SoundBuffer.Format
                + " size=" + _effect.SoundBuffer.DataSize
                + " rate=" + _effect.SoundBuffer.SampleRate
                + " looped=" + IsLooped
                + " volume=" + _alVolume
                + " pitch=" + _pitch
                + " pan=" + _pan);
            AL.Source(SourceId, ALSourcei.Buffer, bufferId);
            ALHelper.CheckError("Failed to bind buffer to source.");

            // Send the position, gain, looping, pitch, and distance model to the OpenAL driver.
            if (!HasSourceId)
				return;

            AL.Source(SourceId, ALSourcei.SourceRelative, 1);
            ALHelper.CheckError("Failed set source relative.");
            // Distance Model
			AL.DistanceModel (ALDistanceModel.InverseDistanceClamped);
            ALHelper.CheckError("Failed set source distance.");
			// Pan
			AL.Source (SourceId, ALSource3f.Position, _pan, 0f, 0f);
            ALHelper.CheckError("Failed to set source pan.");
            // Velocity
			AL.Source (SourceId, ALSource3f.Velocity, 0f, 0f, 0f);
            ALHelper.CheckError("Failed to set source pan.");
			// Volume
            AL.Source(SourceId, ALSourcef.Gain, _alVolume);
            ALHelper.CheckError("Failed to set source volume.");
			// Looping
			AL.Source (SourceId, ALSourceb.Looping, IsLooped);
            ALHelper.CheckError("Failed to set source loop state.");
			// Pitch
			AL.Source (SourceId, ALSourcef.Pitch, XnaPitchToAlPitch(_pitch));
            ALHelper.CheckError("Failed to set source pitch.");

            ApplyReverb ();
            ApplyFilter ();

            AL.SourcePlay(SourceId);
            ALHelper.CheckError("Failed to play source.");
            Log("SoundEffectInstance.PlayMemoryResident playing source=" + SourceId);

            SoundState = SoundState.Playing;
        }

        private const int MaxStreamBuffers = 5;
        private const int StreamBufferFillSize = 131072;

        private void PlayStreamed()
        {
            Log("SoundEffectInstance.PlayStreamed start");
            currentBufferPosition = 0;
            buffers = AL.GenBuffers(MaxStreamBuffers);
            ALHelper.CheckError("Failed to generate stream buffers.");

            SourceId = 0;
            HasSourceId = false;
            SourceId = controller.ReserveSource();
            HasSourceId = true;
            ALHelper.CheckError("Failed to reserve source.");

            AL.Source(SourceId, ALSourcei.Buffer, 0);
            ALHelper.CheckError("Failed to clear source buffer.");

            for (var i = 0; i < buffers.Length; i++)
            {
                if (_effect.SoundBufferStreamed.Alignment > 0)
                {
                    AL.Bufferi(buffers[i], ALBufferi.UnpackBlockAlignmentSoft, _effect.SoundBufferStreamed.Alignment);
                    ALHelper.CheckError("Failed to set buffer alignment.");
                }
            }

            AL.Source(SourceId, ALSourcei.SourceRelative, 1);
            ALHelper.CheckError("Failed set source relative.");
            AL.DistanceModel(ALDistanceModel.InverseDistanceClamped);
            ALHelper.CheckError("Failed set source distance.");
            AL.Source(SourceId, ALSource3f.Position, _pan, 0f, 0f);
            ALHelper.CheckError("Failed to set source pan.");
            AL.Source(SourceId, ALSource3f.Velocity, 0f, 0f, 0f);
            ALHelper.CheckError("Failed to set source pan.");
            AL.Source(SourceId, ALSourcef.Gain, _alVolume);
            ALHelper.CheckError("Failed to set source volume.");
            AL.Source(SourceId, ALSourcef.Pitch, XnaPitchToAlPitch(_pitch));
            ALHelper.CheckError("Failed to set source pitch.");

            ApplyReverb();
            ApplyFilter();

            QueueStreamBuffers(buffers);
            AL.SourcePlay(SourceId);
            ALHelper.CheckError("Failed to play streamed source.");
            Log("SoundEffectInstance.PlayStreamed playing source=" + SourceId);
            SoundState = SoundState.Playing;
        }

        private void PlatformResume()
        {
            if (!HasSourceId)
            {
                Play();
                return;
            }

            if (SoundState == SoundState.Paused)
            {
                --pauseCount;
                if (pauseCount == 0)
                {
                    AL.SourcePlay(SourceId);
                    ALHelper.CheckError("Failed to play source.");
                }
            }
            SoundState = SoundState.Playing;
        }

        private void PlatformStop(bool immediate)
        {
            FreeSource();
            SoundState = SoundState.Stopped;
        }

        partial void PlatformPushIfNeeded()
        {
            if (State != SoundState.Playing || _effect.SoundBufferStreamed == null || !HasSourceId)
                return;

            int buffersProcessed;
            AL.GetSource(SourceId, ALGetSourcei.BuffersProcessed, out buffersProcessed);
            ALHelper.CheckError("Failed to get processed buffer count.");
            if (buffersProcessed <= 0)
                return;

            var processedBuffers = AL.SourceUnqueueBuffers(SourceId, buffersProcessed);
            ALHelper.CheckError("Failed to unqueue processed buffers.");
            QueueStreamBuffers(processedBuffers);
        }

        private void QueueStreamBuffers(int[] bufferSet)
        {
            if (_effect.SoundBufferStreamed == null || bufferSet == null || bufferSet.Length == 0)
                return;

            var stream = _effect.SoundBufferStreamed;

            int buffersQueued;
            AL.GetSource(SourceId, ALGetSourcei.BuffersQueued, out buffersQueued);
            ALHelper.CheckError("Failed to get queued buffer count.");

            if (buffersQueued > 2)
                return;

            if (buffersQueued == 0 && currentBufferPosition == stream.Size && !_looped)
            {
                PlatformStop(true);
                HasSourceId = false;
                return;
            }

            var alignment = GetStreamAlignment(stream);
            for (var i = 0; i < bufferSet.Length; i++)
            {
                var size = Math.Min(StreamBufferFillSize, stream.Size - (int)currentBufferPosition);
                size -= size % alignment;

                if (size <= 0)
                {
                    if (!_looped)
                        break;

                    currentBufferPosition = 0;
                    size = Math.Min(StreamBufferFillSize, stream.Size);
                    size -= size % alignment;
                }

                AL.BufferData(bufferSet[i], stream.Format, IntPtr.Add(stream.DataBuffer, (int)currentBufferPosition), size, stream.SampleRate);
                ALHelper.CheckError("Failed to fill streamed buffer.");
                AL.SourceQueueBuffer(SourceId, bufferSet[i]);
                ALHelper.CheckError("Failed to queue streamed buffer.");
                currentBufferPosition += size;
            }
        }

        private static int GetStreamAlignment(OALSoundBufferStreamed stream)
        {
            if (stream.Alignment > 0)
            {
                if (stream.Format == ALFormat.MonoMSAdpcm || stream.Format == ALFormat.StereoMSAdpcm)
                    return (int)stream.Channels * ((stream.Alignment - 2) / 2 + 7);

                var bytesPerSample =
                    (stream.Format == ALFormat.Mono8 || stream.Format == ALFormat.Stereo8) ? 1 :
                    (stream.Format == ALFormat.Mono16 || stream.Format == ALFormat.Stereo16) ? 2 :
                    4;
                return (int)stream.Channels * bytesPerSample * stream.Alignment;
            }

            return ALHelper.IsStereoFormat(stream.Format) ? 4 : 2;
        }

        private void FreeSource()
        {
            if (!HasSourceId)
                return;

            lock (sourceMutex)
            {
                if (HasSourceId && AL.IsSource(SourceId))
                {
                    AL.SourceStop(SourceId);
                    ALHelper.CheckError("Failed to stop source.");

                    // Reset the SendFilter to 0 if we are NOT using reverb since
                    // sources are recycled
                    if (OpenALSoundController.Instance.SupportsEfx)
                    {
                        OpenALSoundController.Efx.BindSourceToAuxiliarySlot(SourceId, 0, 0, 0);
                        ALHelper.CheckError("Failed to unset reverb.");
                        AL.Source(SourceId, ALSourcei.EfxDirectFilter, 0);
                        ALHelper.CheckError("Failed to unset filter.");
                    }

                    // Detach any residual buffer bindings before the source is recycled.
                    AL.Source(SourceId, ALSourcei.Buffer, 0);
                    ALHelper.CheckError("Failed to clear source buffer.");

                    if (buffers != null && buffers.Length > 0)
                    {
                        AL.DeleteBuffers(buffers);
                        ALHelper.CheckError("Failed to delete stream buffers.");
                        buffers = null;
                    }

                    currentBufferPosition = 0;
                    controller.FreeSource(this);
                    HasSourceId = false;
                }
            }
        }

        private void PlatformSetIsLooped(bool value)
        {
            _looped = value;

            if (HasSourceId)
            {
                AL.Source(SourceId, ALSourceb.Looping, _looped);
                ALHelper.CheckError("Failed to set source loop state.");
            }
        }

        private bool PlatformGetIsLooped()
        {
            return _looped;
        }

        private void PlatformSetPan(float value)
        {
            if (HasSourceId)
            {
                AL.Source(SourceId, ALSource3f.Position, value, 0.0f, 0.1f);
                ALHelper.CheckError("Failed to set source pan.");
            }
        }

        private void PlatformSetPitch(float value)
        {
            if (HasSourceId)
            {
                AL.Source(SourceId, ALSourcef.Pitch, XnaPitchToAlPitch(value));
                ALHelper.CheckError("Failed to set source pitch.");
            }
        }

        private SoundState PlatformGetState()
        {
            if (!HasSourceId)
                return SoundState.Stopped;
            
            var alState = AL.GetSourceState(SourceId);
            ALHelper.CheckError("Failed to get source state.");

            switch (alState)
            {
                case ALSourceState.Initial:
                case ALSourceState.Stopped:
                    SoundState = SoundState.Stopped;
                    break;

                case ALSourceState.Paused:
                    SoundState = SoundState.Paused;
                    break;

                case ALSourceState.Playing:
                    SoundState = SoundState.Playing;
                    break;
            }

            return SoundState;
        }

        private void PlatformSetVolume(float value)
        {
            _alVolume = value;

            if (HasSourceId)
            {
                AL.Source(SourceId, ALSourcef.Gain, _alVolume);
                ALHelper.CheckError("Failed to set source volume.");
            }
        }

        internal void PlatformSetReverbMix(float mix)
        {
            if (!OpenALSoundController.Efx.IsInitialized)
                return;
            reverb = mix;
            if (State == SoundState.Playing)
            {
                ApplyReverb();
                reverb = 0f;
            }
        }

        void ApplyReverb()
        {
            if (reverb > 0f && SoundEffect.ReverbSlot != 0)
            {
                OpenALSoundController.Efx.BindSourceToAuxiliarySlot(SourceId, (int)SoundEffect.ReverbSlot, 0, 0);
                ALHelper.CheckError("Failed to set reverb.");
            }
        }

        void ApplyFilter()
        {
            if (applyFilter && controller.Filter > 0)
            {
                var freq = frequency / 20000f;
                var lf = 1.0f - freq;
                var efx = OpenALSoundController.Efx;
                efx.Filter(controller.Filter, EfxFilteri.FilterType, (int)filterType);
                ALHelper.CheckError("Failed to set filter.");
                switch (filterType)
                {
                case EfxFilterType.Lowpass:
                    efx.Filter(controller.Filter, EfxFilterf.LowpassGainHF, freq);
                    ALHelper.CheckError("Failed to set LowpassGainHF.");
                    break;
                case EfxFilterType.Highpass:
                    efx.Filter(controller.Filter, EfxFilterf.HighpassGainLF, freq);
                    ALHelper.CheckError("Failed to set HighpassGainLF.");
                    break;
                case EfxFilterType.Bandpass:
                    efx.Filter(controller.Filter, EfxFilterf.BandpassGainHF, freq);
                    ALHelper.CheckError("Failed to set BandpassGainHF.");
                    efx.Filter(controller.Filter, EfxFilterf.BandpassGainLF, lf);
                    ALHelper.CheckError("Failed to set BandpassGainLF.");
                    break;
                }
                AL.Source(SourceId, ALSourcei.EfxDirectFilter, controller.Filter);
                ALHelper.CheckError("Failed to set DirectFilter.");
            }
        }

        internal void PlatformSetFilter(FilterMode mode, float filterQ, float frequency)
        {
            if (!OpenALSoundController.Efx.IsInitialized)
                return;

            applyFilter = true;
            switch (mode)
            {
            case FilterMode.BandPass:
                filterType = EfxFilterType.Bandpass;
                break;
                case FilterMode.LowPass:
                filterType = EfxFilterType.Lowpass;
                break;
                case FilterMode.HighPass:
                filterType = EfxFilterType.Highpass;
                break;
            }
            this.filterQ = filterQ;
            this.frequency = frequency;
            if (State == SoundState.Playing)
            {
                ApplyFilter();
                applyFilter = false;
            }
        }

        internal void PlatformClearFilter()
        {
            if (!OpenALSoundController.Efx.IsInitialized)
                return;

            applyFilter = false;
        }

        private void PlatformDispose(bool disposing)
        {
            FreeSource();
        }
    }
}
