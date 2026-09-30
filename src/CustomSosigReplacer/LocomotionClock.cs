using System;

namespace CustomSosigReplacer
{
    internal sealed class LocomotionClock
    {
        public bool Walking { get; private set; }
        public float Weight { get; private set; }
        public float Phase { get; private set; }

        public void Advance(float speed, float deltaTime, bool controlled, bool crouched)
        {
            if (deltaTime <= 0f || float.IsNaN(speed) || float.IsInfinity(speed)) return;
            speed = Math.Max(0f, speed);
            if (!controlled || speed < 0.08f) Walking = false;
            else if (speed > 0.18f) Walking = true;
            float target = Walking ? 1f : 0f;
            float step = deltaTime * 5f;
            Weight += Math.Max(-step, Math.Min(step, target - Weight));
            if (controlled && Walking) Phase += deltaTime * speed / (crouched ? 0.85f : 1.4f);
        }
    }
}
