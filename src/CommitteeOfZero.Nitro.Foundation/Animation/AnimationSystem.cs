﻿using System;
using System.Collections.Generic;

namespace CommitteeOfZero.Nitro.Foundation.Animation
{
    public sealed class AnimationSystem : EntityProcessingSystem
    {
        protected override void DeclareInterests(ISet<Type> interests)
        {
            interests.Add(typeof(AnimationBase));
        }

        public override void Process(Entity entity, float deltaMilliseconds)
        {
            foreach (AnimationBase animation in entity.GetComponents<AnimationBase>())
            {
                if (animation.IsEnabled)
                {
                    animation.Advance(animation.Started ? deltaMilliseconds : 0.0f);
                }
            }
        }
    }
}