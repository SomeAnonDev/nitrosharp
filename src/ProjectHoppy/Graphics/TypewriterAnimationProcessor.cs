﻿using SciAdvNet.MediaLayer.Graphics;

namespace ProjectHoppy.Graphics
{
    public class TypewriterAnimationProcessor : EntityProcessingSystem
    {
        public TypewriterAnimationProcessor(RenderContext renderContext)
            : base(typeof(TextComponent))
        {
            
        }

        public override void Process(Entity entity, float deltaMilliseconds)
        {
            var text = entity.GetComponent<TextComponent>();
            if (text.CurrentGlyphIndex >= text.Text.Length)
            {
                return;
            }

            text.CurrentGlyphOpacity += 1.0f * (deltaMilliseconds / (float)100);

            if (text.CurrentGlyphOpacity >= 1.0f || text.Text[text.CurrentGlyphIndex] == ' ')
            {
                text.CurrentGlyphOpacity = 0.0f;
                text.CurrentGlyphIndex++;
            }
        }
    }
}