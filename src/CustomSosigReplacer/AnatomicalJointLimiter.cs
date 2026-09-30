using UnityEngine;

namespace CustomSosigReplacer
{
    internal static class AnatomicalJointLimiter
    {
        public static Quaternion ClampRelativeToBind(Quaternion current, Quaternion bind, Vector3 twistAxis, float maxSwingDegrees, float maxTwistDegrees)
        {
            if (twistAxis.sqrMagnitude < 0.000001f) return current;
            twistAxis.Normalize();

            Quaternion relative = Quaternion.Inverse(bind) * current;
            if (relative.w < 0f) relative = Negate(relative);

            Vector3 vector = new Vector3(relative.x, relative.y, relative.z);
            Vector3 projected = twistAxis * Vector3.Dot(vector, twistAxis);
            Quaternion twist = NormalizeSafe(new Quaternion(projected.x, projected.y, projected.z, relative.w));
            Quaternion swing = relative * Quaternion.Inverse(twist);

            float swingAngle;
            Vector3 swingAxis;
            swing.ToAngleAxis(out swingAngle, out swingAxis);
            if (swingAngle > 180f)
            {
                swingAngle = 360f - swingAngle;
                swingAxis = -swingAxis;
            }
            if (swingAxis.sqrMagnitude < 0.000001f) swingAxis = Vector3.right;
            Quaternion clampedSwing = Quaternion.AngleAxis(Mathf.Min(swingAngle, Mathf.Max(0f, maxSwingDegrees)), swingAxis.normalized);

            float twistAngle;
            Vector3 measuredTwistAxis;
            twist.ToAngleAxis(out twistAngle, out measuredTwistAxis);
            if (twistAngle > 180f) twistAngle -= 360f;
            if (Vector3.Dot(measuredTwistAxis, twistAxis) < 0f) twistAngle = -twistAngle;
            twistAngle = Mathf.Clamp(twistAngle, -Mathf.Max(0f, maxTwistDegrees), Mathf.Max(0f, maxTwistDegrees));
            Quaternion clampedTwist = Quaternion.AngleAxis(twistAngle, twistAxis);
            return bind * (clampedSwing * clampedTwist);
        }

        private static Quaternion NormalizeSafe(Quaternion value)
        {
            float magnitude = Mathf.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w);
            if (magnitude < 0.000001f) return Quaternion.identity;
            float inverse = 1f / magnitude;
            return new Quaternion(value.x * inverse, value.y * inverse, value.z * inverse, value.w * inverse);
        }

        private static Quaternion Negate(Quaternion value)
        {
            return new Quaternion(-value.x, -value.y, -value.z, -value.w);
        }
    }
}
