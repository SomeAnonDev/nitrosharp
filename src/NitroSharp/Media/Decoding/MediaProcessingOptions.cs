﻿using System;
using System.Runtime.CompilerServices;
using NitroSharp.Primitives;
using NitroSharp.Utilities;

namespace NitroSharp.Media.Decoding
{
    public readonly struct MediaProcessingOptions : IEquatable<MediaProcessingOptions>
    {
        public readonly VideoFrameConverter FrameConverter;
        public readonly Size? OutputVideoResolution;
        public readonly AudioParameters OutputAudioParameters;

        public MediaProcessingOptions(
            in AudioParameters outputAudioParameters,
            VideoFrameConverter frameConverter,
            Size? outputVideoResolution = null)
        {
            if (frameConverter == null)
            {
                throw new ArgumentNullException(nameof(frameConverter));
            }

            OutputAudioParameters = outputAudioParameters;
            FrameConverter = frameConverter;
            OutputVideoResolution = outputVideoResolution;
        }

        public override bool Equals(object obj) => obj is MediaProcessingOptions other && Equals(other);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(MediaProcessingOptions other)
        {
            if (OutputAudioParameters.Equals(other.OutputAudioParameters) && FrameConverter == other.FrameConverter)
            {
                if (OutputVideoResolution.HasValue == other.OutputVideoResolution.HasValue)
                {
                    return OutputVideoResolution.HasValue
                        ? OutputVideoResolution.Value.Equals(other.OutputVideoResolution.Value)
                        : true;
                }
            }

            return false;
        }

        public override int GetHashCode()
        {
            return HashHelper.Combine(
                OutputVideoResolution.GetHashCode(),
                FrameConverter.GetHashCode(),
                OutputAudioParameters.GetHashCode());
        }

        public static bool operator ==(MediaProcessingOptions left, MediaProcessingOptions right) => left.Equals(right);
        public static bool operator !=(MediaProcessingOptions left, MediaProcessingOptions right) => !left.Equals(right);
    }
}