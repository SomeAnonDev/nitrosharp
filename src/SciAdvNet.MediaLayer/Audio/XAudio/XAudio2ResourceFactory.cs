﻿using System;
using System.Collections.Generic;
using System.Text;

namespace SciAdvNet.MediaLayer.Audio.XAudio
{
    public class XAudio2ResourceFactory : ResourceFactory
    {
        private readonly XAudio2AudioEngine _engine;

        internal XAudio2ResourceFactory(XAudio2AudioEngine engine)
        {
            _engine = engine;
        }

        public override AudioSource CreateAudioSource()
        {
            return new XAudio2AudioSource(_engine);
        }
    }
}
