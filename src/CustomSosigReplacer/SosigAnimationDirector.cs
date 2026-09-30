using FistVR;
using UnityEngine;

namespace CustomSosigReplacer
{
    /// <summary>
    /// Converts H3VR's native Sosig/weapon state into stable presentation state.
    /// The Sosig remains authoritative for AI, movement, aiming, firing and damage;
    /// this class only decides which authored humanoid animation layer to show.
    /// </summary>
    internal sealed class SosigAnimationDirector
    {
        internal enum WeaponFamily
        {
            None,
            Rifle,
            Handgun
        }

        private SosigWeapon _weapon;
        private float _lastTimeSinceFired;
        private bool _wasReloading;

        public WeaponFamily Family { get; private set; }
        public SosigWeapon Weapon { get { return _weapon; } }
        public float WeaponWeight { get; private set; }
        public float AimWeight { get; private set; }
        public float ReloadWeight { get; private set; }
        public float FireTime { get; private set; } = 999f;
        public float ReloadTime { get; private set; }
        public bool IsReloading { get; private set; }

        public void Update(Sosig sosig, float deltaTime)
        {
            SosigWeapon nextWeapon = null;
            bool aimed = false;
            if (sosig != null && sosig.Hands != null)
            {
                for (int index = 0; index < sosig.Hands.Count; index++)
                {
                    SosigHand hand = sosig.Hands[index];
                    if (hand == null || !hand.IsHoldingObject || hand.HeldObject == null) continue;
                    if (hand.HeldObject.Type != SosigWeapon.SosigWeaponType.Gun) continue;
                    if (nextWeapon == null || hand.Pose == SosigHand.SosigHandPose.Aimed) nextWeapon = hand.HeldObject;
                    if (hand.Pose == SosigHand.SosigHandPose.Aimed) aimed = true;
                }
            }

            bool weaponChanged = nextWeapon != _weapon;
            if (weaponChanged)
            {
                _weapon = nextWeapon;
                Family = Classify(nextWeapon);
                _lastTimeSinceFired = nextWeapon == null ? 999f : nextWeapon.TimeSinceFired();
                FireTime = 999f;
                ReloadTime = 0f;
                _wasReloading = false;
            }

            bool inControl = sosig != null && sosig.BodyState == Sosig.SosigBodyState.InControl;
            float weaponTarget = inControl && _weapon != null ? 1f : 0f;
            float aimTarget = weaponTarget > 0f && aimed ? 1f : 0f;
            WeaponWeight = Damp(WeaponWeight, weaponTarget, 12f, deltaTime);
            AimWeight = Damp(AimWeight, aimTarget, 16f, deltaTime);

            IsReloading = inControl && _weapon != null && _weapon.UsageState == SosigWeapon.SosigWeaponUsageState.Reloading;
            if (IsReloading)
            {
                if (!_wasReloading) ReloadTime = 0f;
                else ReloadTime += deltaTime;
            }
            else if (_wasReloading)
            {
                ReloadTime = 0f;
            }
            ReloadWeight = Damp(ReloadWeight, IsReloading ? 1f : 0f, IsReloading ? 20f : 10f, deltaTime);
            _wasReloading = IsReloading;

            if (_weapon != null)
            {
                float timeSinceFired = _weapon.TimeSinceFired();
                bool newShot = timeSinceFired < 0.075f && _lastTimeSinceFired > timeSinceFired + 0.0001f;
                if (newShot) FireTime = 0f;
                else FireTime += deltaTime;
                _lastTimeSinceFired = timeSinceFired;
            }
            else
            {
                FireTime += deltaTime;
                _lastTimeSinceFired = 999f;
            }
        }

        public float GetReloadClipTime(RuntimeBoneClip clip)
        {
            if (clip == null) return 0f;
            float sourceDuration = _weapon == null ? clip.Duration : Mathf.Max(0.1f, _weapon.ReloadTime);
            return Mathf.Clamp01(ReloadTime / sourceDuration) * clip.Duration;
        }

        private static WeaponFamily Classify(SosigWeapon weapon)
        {
            if (weapon == null) return WeaponFamily.None;
            switch (weapon.AmmoType)
            {
                case SosigWeapon.SosiggunAmmoType.PistolLight:
                case SosigWeapon.SosiggunAmmoType.PistolStrong:
                case SosigWeapon.SosiggunAmmoType.PistolExotic:
                    return WeaponFamily.Handgun;
                default:
                    return WeaponFamily.Rifle;
            }
        }

        private static float Damp(float current, float target, float sharpness, float deltaTime)
        {
            return Mathf.Lerp(current, target, 1f - Mathf.Exp(-sharpness * Mathf.Max(0f, deltaTime)));
        }
    }
}
