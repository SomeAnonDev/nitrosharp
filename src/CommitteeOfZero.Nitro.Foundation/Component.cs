﻿namespace CommitteeOfZero.Nitro.Foundation
{
    public abstract class Component
    {
        public Entity Entity { get; private set; }
        public bool IsEnabled { get; set; } = true;

        internal void AttachToEntity(Entity entity)
        {
            Entity = entity;
            OnAttached();
        }

        public virtual void OnAttached()
        {
        }

        public virtual void OnRemoved()
        {
        }
    }
}