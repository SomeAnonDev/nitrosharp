﻿//using NitroSharp.Graphics;
//using NitroSharp.Content;
//using NitroSharp.NsScript;
//using NitroSharp.NsScript.Primitives;
//using NitroSharp.Primitives;
//using System;
//using System.Numerics;
//using NitroSharp.Experimental;
//using NitroSharp.Animation;
//using NitroSharp.Interactivity;
//using NitroSharp.Text;
//using static NitroSharp.Game;

//#nullable enable

//namespace NitroSharp
//{
//    internal sealed partial class NsBuiltins
//    {
//        public override void CreateTextBlock(
//           string name, int priority,
//           NsCoordinate x, NsCoordinate y,
//           NsDimension width, NsDimension height,
//           string pxmlText)
//        {
//            var entityName = new EntityName(name);
//            var parentBounds = new SizeF(float.MaxValue, float.MaxValue);
//            if (entityName.Parent != null)
//            {
//                Entity parent = _world.GetEntity(new EntityName(entityName.Parent));
//                var storage = _world.GetStorage<RenderItem2DStorage>(parent);
//                if (storage != null)
//                {
//                    parentBounds = storage.LocalBounds[parent];
//                }
//            }

//            uint getDimension(NsDimension dim, bool width)
//            {
//                return dim.Variant switch
//                {
//                    NsDimensionVariant.Auto => uint.MaxValue,
//                    NsDimensionVariant.Value => (uint)dim.Value!.Value,
//                    NsDimensionVariant.Inherit
//                        => width ? (uint)parentBounds.Width : (uint)parentBounds.Height
//                };
//            }

//            var textBuffer = TextBuffer.FromPXmlString(pxmlText, _fontConfig, new PtFontSize(18));
//            TextSegment? textSegment = textBuffer.AssertSingleTextSegment();
//            if (textSegment != null)
//            {
//                var bounds = new Size(getDimension(width, true), getDimension(height, false));
//                var layout = new TextLayout(GlyphRasterizer, textSegment.TextRuns.AsSpan(), bounds);
//                (Entity entity, _) = _world.TextBlocks.Uninitialized.New(entityName, layout, priority);
//                SetPosition(entity, x, y);
//            }
//        }

//        public override void CreateChoice(string name)
//        {
//            _world.Choices.Uninitialized.New(new EntityName(name));
//        }

//        public override bool IsPressed(string choiceName)
//        {
//            Entity choice = _world.GetEntity(new EntityName(choiceName));
//            ChoiceStorage? storage;
//            if (_world.Exists(choice) &&
//                (storage = _world.GetStorage<ChoiceStorage>(choice)) != null)
//            {
//                return storage.MouseState[choice] == MouseState.Pressed;
//            }
//            return false;
//        }

//        public override void CreateAlphaMask(
//            string name, int priority,
//            NsCoordinate x, NsCoordinate y,
//            string path,
//            bool unk)
//        {
//            var imageHandle = new AssetId(path);
//            if (Content.RequestTexture(imageHandle, out Size texSize))
//            {
//                (Entity entity, _) = _world.AlphaMasks.Uninitialized.New(
//                    new EntityName(name),
//                    new SizeF(1280, 720),
//                    priority,
//                    imageHandle
//                );
//                SetPosition(entity, x, y);
//            }
//        }

//        public override void CreateEffect(
//            string entityName,
//            int priority,
//            NsCoordinate x, NsCoordinate y,
//            int width, int height,
//            string effectName)
//        {
//            var distortionMap = new AssetId("testcg/lens.png");
//            if (Content.RequestTexture(distortionMap, out _))
//            {
//                (Entity entity, _)  =_world.Quads.Uninitialized.New(
//                    new EntityName(entityName),
//                    new SizeF(width, height),
//                    priority,
//                    Material.Lens(distortionMap)
//                );
//                SetPosition(entity, x, y);
//            }
//        }

//        public override void BoxBlur(string entityQuery, uint nbPasses)
//        {
//            foreach ((Entity entity, _) in _world.Query(entityQuery))
//            {
//                var storage = _world.GetStorage<QuadStorage>(entity);
//                storage.Materials[entity].Effects
//                    .Add(new EffectDescription(EffectKind.BoxBlur, nbPasses));
//            }
//        }

//        public override void Grayscale(string entityQuery)
//        {
//            foreach ((Entity entity, _) in _world.Query(entityQuery))
//            {
//                var storage = _world.GetStorage<QuadStorage>(entity);
//                storage.Materials[entity].Effects
//                    .Add(new EffectDescription(EffectKind.Grayscale));
//            }
//        }

//        public override void CreateRectangle(
//            string name, int priority,
//            NsCoordinate x, NsCoordinate y,
//            int width, int height, NsColor color)
//        {
//            (Entity e, _) = _world.Quads.Uninitialized.New(
//                new EntityName(name),
//                new SizeF(width, height),
//                priority,
//                Material.SolidColor(color.ToRgbaFloat())
//            );

//            SetPosition(e, x, y);
//        }

//        public override void LoadImage(string entityName, string fileName)
//        {
//            var textureId = new AssetId(fileName);
//            if (Content.RequestTexture(textureId, out _, incrementRefCount: false))
//            {
//                _world.Images.Uninitialized.New(
//                    new EntityName(entityName),
//                    new ImageSource(textureId, sourceRectangle: default)
//                );
//            }
//        }

//        public override void CreateSprite(
//            string name, int priority,
//            NsCoordinate x, NsCoordinate y,
//            string fileOrExistingEntityName)
//        {
//            if (fileOrExistingEntityName.Equals("SCREEN", StringComparison.OrdinalIgnoreCase))
//            {
//                CaptureFramebuffer(name, x, y, priority);
//            }
//            else
//            {
//                CreateSpriteCore(name, fileOrExistingEntityName, x, y, priority);
//            }
//        }

//        private void CaptureFramebuffer(
//            string entityName,
//            NsCoordinate x, NsCoordinate y,
//            int priority)
//        {
//            (Entity e, _) = _world.Quads.Uninitialized.New(
//                new EntityName(entityName),
//                new SizeF(1280, 720),
//                priority,
//                Material.Screenshot()
//            );

//            SetPosition(e, x, y);
//            VM.SuspendThread(MainThread!);
//            _messageQueue.Enqueue(new SimpleMessage(MessageKind.CaptureFramebuffer));
//        }

//        public override void CreateSpriteEx(
//            string name, int priority,
//            NsCoordinate dstX, NsCoordinate dstY,
//            NsCoordinate srcX, NsCoordinate srcY,
//            int width, int height, string srcEntityName)
//        {
//            var srcRectangle = new RectangleF(srcX.Value, srcY.Value, width, height);
//            CreateSpriteCore(name, srcEntityName, dstX, dstY, priority, srcRectangle);
//        }

//        private void CreateSpriteCore(
//            string name, string fileOrExistingEntityName,
//            NsCoordinate x, NsCoordinate y,
//            int priority, RectangleF? srcRect = null)
//        {
//            _logger.LogInformation($"Loading sprite: {fileOrExistingEntityName}");

//            string source = fileOrExistingEntityName;
//            if (source.ToUpperInvariant().Contains("COLOR")) { return; }
//            if (_world.TryGetEntity(new EntityName(source), out Entity existingEnitity))
//            {
//                var storage = _world.GetStorage<AbstractImageStorage>(existingEnitity);
//                source = storage.ImageSources[existingEnitity].Handle.NormalizedPath;
//            }

//            var textureId = new AssetId(source);
//            if (!Content.RequestTexture(textureId, out Size texSize))
//            {
//                return;
//            }

//            var sourceRectangle = srcRect ?? new RectangleF(Vector2.Zero, texSize);
//            var localBounds = new SizeF(sourceRectangle.Width, sourceRectangle.Height);
//            (Entity entity, _) = _world.Quads.Uninitialized.New(
//                new EntityName(name),
//                localBounds,
//                priority,
//                Material.Texture(textureId, texSize, sourceRectangle)
//            );
//            SetPosition(entity, x, y);
//        }

//        public override void CreateCube(
//            string name, int priority,
//            string front, string back,
//            string right, string left,
//            string top, string bottom)
//        {
//        }

//        public override void DrawTransition(
//            string entityQuery,
//            TimeSpan duration,
//            NsRational initialFadeAmount,
//            NsRational finalFadeAmount,
//            NsRational feather,
//            NsEasingFunction easingFunction,
//            string maskFileName,
//            TimeSpan delay)
//        {
//            var maskHandle = new AssetId(maskFileName);
//            if (Content.RequestTexture(maskHandle, out _))
//            {
//                foreach ((Entity entity, _) in _world.Query(entityQuery))
//                {
//                    var storage = _world.GetStorage<QuadStorage>(entity);
//                    storage.Materials[entity]
//                        .TransitionParameters.MaskHandle = maskHandle;

//                    var animation = new TransitionAnimation(entity, duration, easingFunction)
//                    {
//                        InitialFadeAmount = initialFadeAmount.Rebase(1.0f),
//                        FinalFadeAmount = finalFadeAmount.Rebase(1.0f),
//                        IsBlocking = CurrentThread == MainThread
//                    };
//                    animation.WaitingThread = CurrentThread;
//                    _world.ActivateAnimation(animation);
//                }

//                VM.SuspendThread(CurrentThread);
//            }
//        }

//        public override int GetWidth(string entityName)
//        {
//            if (_world.TryGetEntity(new EntityName(entityName), out Entity entity))
//            {
//                var storage = _world.GetStorage<RenderItem2DStorage>(entity);
//                return (int)storage.LocalBounds[entity].Width;
//            }

//            return 0;
//        }

//        public override int GetHeight(string entityName)
//        {
//            if (_world.TryGetEntity(new EntityName(entityName), out Entity entity))
//            {
//                var storage = _world.GetStorage<RenderItem2DStorage>(entity);
//                return (int)storage.LocalBounds[entity].Height;
//            }

//            return 0;
//        }

//        internal Vector2 GetAbsolutePosition(Entity entity, NsCoordinate x, NsCoordinate y)
//        {
//            var parentBounds = new SizeF(1280, 720);

//            var storage = _world.GetStorage<RenderItem2DStorage>(entity);
//            ref TransformComponents transform = ref storage.TransformComponents[entity];
//            SizeF bounds = storage.LocalBounds[entity];

//            Entity parent = _world.GetParent(entity);
//            if (parent.IsValid)
//            {
//                if (_world.GetStorage<RenderItem2DStorage>(parent) is AlphaMaskStorage maskStorage)
//                {

//                }
//                else if (_world.GetStorage<RenderItem2DStorage>(parent)
//                    is RenderItem2DStorage renderItems)
//                {
//                    parentBounds = renderItems.LocalBounds[parent];
//                }
//            }

//            var value = new Vector2(
//                x.Origin == NsCoordinateOrigin.CurrentValue
//                    ? transform.Position.X + x.Value
//                    : x.Value,
//                y.Origin == NsCoordinateOrigin.CurrentValue
//                    ? transform.Position.Y + y.Value
//                    : y.Value
//            );

//            var anchorPoint = new Vector2(x.AnchorPoint, y.AnchorPoint);
//            Vector2 translateOrigin;
//            translateOrigin.X = x.Origin switch
//            {
//                NsCoordinateOrigin.Left => 0.0f,
//                NsCoordinateOrigin.Center => 0.5f,
//                NsCoordinateOrigin.Right => 1.0f,
//                _ => 0.0f
//            };
//            translateOrigin.Y = y.Origin switch
//            {
//                NsCoordinateOrigin.Top => 0.0f,
//                NsCoordinateOrigin.Center => 0.5f,
//                NsCoordinateOrigin.Bottom => 1.0f,
//                _ => 0.0f
//            };

//            Vector2 position = translateOrigin * parentBounds.ToVector();
//            position -= anchorPoint * bounds.ToVector();
//            position += value;
//            return position;
//        }

//        internal void SetPosition(Entity entity, NsCoordinate x, NsCoordinate y)
//        {
//            Vector2 pos = GetAbsolutePosition(entity, x, y);
//            var storage = _world.GetStorage<RenderItem2DStorage>(entity);
//            storage.TransformComponents[entity]
//                .Position = new Vector3(pos.X, pos.Y, 0);
//        }
//    }
//}