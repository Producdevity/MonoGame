// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using System;
using System.Threading;
using Microsoft.Xna.Framework.Audio;

namespace Microsoft.Xna.Framework
{
    /// <summary>
    /// Helper class for processing internal framework events.
    /// </summary>
    /// <remarks>
    /// If you use <see cref="Game"/> class, <see cref="Update()"/> is called automatically.
    /// Otherwise you must call it as part of your game loop.
    /// </remarks>
    public static class FrameworkDispatcher
    {
        private static bool _initialized = false;
        internal static readonly object UpdateSyncRoot = new object();
        private static Thread _updateWorkerThread;

        /// <summary>
        /// Processes framework events.
        /// </summary>
        public static void Update()
        {
            if (!_initialized)
                Initialize();
        }

        private static void UpdateWork()
        {
            while (true)
            {
                lock (UpdateSyncRoot)
                    DoUpdate();

                Thread.Sleep(16);
            }
        }

        private static void DoUpdate()
        {
            DynamicSoundEffectInstanceManager.UpdatePlayingInstances();
            SoundEffectInstancePool.Update();
            Microphone.UpdateMicrophones();
        }

        private static void Initialize()
        {
            SoundEffect.Initialize();
            _updateWorkerThread = new Thread(UpdateWork);
            _updateWorkerThread.IsBackground = true;
            _updateWorkerThread.Start();
            _initialized = true;
        }
    }
}

