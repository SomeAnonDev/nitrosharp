﻿using System.IO;
using System.Runtime.CompilerServices;
using FFmpeg.AutoGen;
using NitroSharp.Graphics;
using NitroSharp.Media;
using NitroSharp.Primitives;
using NitroSharp.Utilities;
using Veldrid;

namespace NitroSharp.Content
{
    internal sealed class FFmpegTextureLoader : ContentLoader
    {
        private readonly FrameConverter _frameConverter;
        private readonly DecoderCollection _decoderCollection;
        private unsafe AVInputFormat* _inputFormat;

        public FFmpegTextureLoader(ContentManager content, DecoderCollection decoderCollection)
            : base(content)
        {
            _frameConverter = new FrameConverter();

            _decoderCollection = decoderCollection;
            decoderCollection.Preload(AVCodecID.AV_CODEC_ID_MJPEG);
            decoderCollection.Preload(AVCodecID.AV_CODEC_ID_PNG);
            unsafe
            {
                _inputFormat = ffmpeg.av_find_input_format("image2pipe");
            }
        }

        public override object Load(Stream stream)
        {
            unsafe
            {
                using (var container = new MediaContainer(stream, _inputFormat, leaveOpen: false))
                using (var decodingSession = new DecodingSession(container, container.VideoStreamId.Value, _decoderCollection))
                {
                    var packet = new AVPacket();
                    // Note: av_frame_unref should NOT be called here. The DecodingSession is responsible for doing that.
                    var frame = new AVFrame();
                    bool succ = container.ReadFrame(&packet);
                    succ = decodingSession.TryDecodeFrame(&packet, out frame);

                    var device = Content.GraphicsDevice;
                    var texture = CreateDeviceTexture(device, device.ResourceFactory, &frame);

                    ffmpeg.av_packet_unref(&packet);
                    return new BindableTexture(device.ResourceFactory, texture);
                }
            }
        }

        public override void Dispose()
        {
            _frameConverter.Dispose();
            unsafe
            {
                _inputFormat = null;
            }
        }

        private unsafe Texture CreateDeviceTexture(GraphicsDevice gd, ResourceFactory factory, AVFrame* frame)
        {
            uint width = (uint)frame->width;
            uint height = (uint)frame->height;
            var size = new Size(width, height);

            Texture staging = factory.CreateTexture(
                TextureDescription.Texture2D(width, height, 1, 1, Veldrid.PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Staging));

            Texture result = factory.CreateTexture(
                TextureDescription.Texture2D(width, height, 1, 1, Veldrid.PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

            CommandList cl = gd.ResourceFactory.CreateCommandList();
            cl.Begin();

            uint level = 0;
            MappedResource map = gd.Map(staging, MapMode.Write, level);
            uint srcRowWidth = width * 4;
            if (srcRowWidth == map.RowPitch)
            {
                _frameConverter.ConvertToRgba(frame, size, (byte*)map.Data);
            }
            else
            {
                using (var buffer = NativeMemory.Allocate(width * height * 4))
                {
                    _frameConverter.ConvertToRgba(frame, size, (byte*)buffer.Pointer);
                    byte* src = (byte*)buffer.Pointer;
                    byte* dst = (byte*)map.Data;
                    for (uint y = 0; y < height; y++)
                    {
                        Unsafe.CopyBlock(dst, src, srcRowWidth);
                        src += srcRowWidth;
                        dst += map.RowPitch;
                    }
                }
            }

            gd.Unmap(staging, level);
            cl.CopyTexture(
                staging, 0, 0, 0, level, 0,
                result, 0, 0, 0, level, 0,
                width, height, 1, 1);
            cl.End();

            gd.SubmitCommands(cl);
            gd.DisposeWhenIdle(staging);
            gd.DisposeWhenIdle(cl);

            return result;
        }
    }
}