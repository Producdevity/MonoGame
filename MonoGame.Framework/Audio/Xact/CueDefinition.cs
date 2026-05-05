// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using System;
using System.Collections.Generic;
using System.IO;

namespace Microsoft.Xna.Framework.Audio
{
    public class CueDefinition
    {
        public enum LimitBehavior
        {
            FailToPlay,
            ReplaceOldest
        }

        public string name;
        public List<XactSoundBankSound> sounds = new List<XactSoundBankSound>();
        public int instanceLimit = 255;
        public Action OnModified;
        public LimitBehavior limitBehavior;

        public CueDefinition()
        {
        }

        public CueDefinition(string name, SoundEffect soundEffect, int categoryId, bool loop = false, bool useReverb = false)
        {
            this.name = name;
            SetSound(soundEffect, categoryId, loop, useReverb);
        }

        public CueDefinition(string name, SoundEffect[] soundEffects, int categoryId, bool loop = false, bool useReverb = false)
        {
            this.name = name;
            SetSound(soundEffects, categoryId, loop, useReverb);
        }

        public virtual void SetSound(SoundEffect soundEffect, int categoryId, bool loop = false, bool useReverb = false)
        {
            SetSound(new[] { soundEffect }, categoryId, loop, useReverb);
        }

        public virtual void SetSound(SoundEffect[] soundEffects, int categoryId, bool loop = false, bool useReverb = false)
        {
            if (soundEffects == null || soundEffects.Length == 0)
                throw new ArgumentException("At least one sound effect is required.", "soundEffects");

            var loadedSoundEffects = new List<SoundEffect>();
            for (var i = 0; i < soundEffects.Length; i++)
            {
                if (soundEffects[i] != null)
                    loadedSoundEffects.Add(soundEffects[i]);
            }

            if (loadedSoundEffects.Count == 0)
                throw new ArgumentException("At least one non-null sound effect is required.", "soundEffects");

            RemoveDependencies();
            sounds.Clear();
            var sound = new XactSoundBankSound(loadedSoundEffects.ToArray(), categoryId, loop, useReverb);
            sounds.Add(sound);
            sound.AddSoundEffectDependencies();

            var modified = OnModified;
            if (modified != null)
                modified();
        }

        private void RemoveDependencies()
        {
            for (var i = 0; i < sounds.Count; i++)
                sounds[i].RemoveSoundEffectDependencies();
        }
    }

    public class XactSoundBankSound : XactSound
    {
        public bool complexSound;
        public XactClip[] soundClips;
        public int waveBankIndex;
        public int trackIndex;
        public float volume;
        public float pitch;
        public uint categoryID;
        public SoundBank soundBank;
        public bool useReverb;
        public int[] rpcCurves;

        public XactSoundBankSound(SoundEffect[] soundEffects, int categoryId, bool loop, bool useReverb)
            : base(soundEffects, categoryId, loop, useReverb)
        {
            CopyFromBase();
        }

        public XactSoundBankSound(SoundBank soundBank, int waveBankIndex, int trackIndex)
            : base(soundBank, waveBankIndex, trackIndex)
        {
            CopyFromBase();
        }

        public XactSoundBankSound(AudioEngine engine, SoundBank soundBank, BinaryReader soundReader)
            : base(engine, soundBank, soundReader)
        {
            CopyFromBase();
        }

        public SoundEffectInstance GetSimpleSoundInstance()
        {
            if (soundBank == null)
                return null;

            bool streaming;
            return soundBank.GetSoundEffectInstance(waveBankIndex, trackIndex, out streaming);
        }

        internal void AddSoundEffectDependencies()
        {
            foreach (var effect in GetReferencedSoundEffects())
                effect.AddDependency();
        }

        internal void RemoveSoundEffectDependencies()
        {
            foreach (var effect in GetReferencedSoundEffects())
                effect.RemoveDependency();
        }

        private void CopyFromBase()
        {
            complexSound = ComplexSound;
            soundClips = SoundClips;
            waveBankIndex = WaveBankIndex;
            trackIndex = TrackIndex;
            volume = VolumeScale;
            pitch = PitchScale;
            categoryID = CategoryId;
            soundBank = SoundBank;
            useReverb = UseReverb;
            rpcCurves = RpcCurves;
        }
    }
}
