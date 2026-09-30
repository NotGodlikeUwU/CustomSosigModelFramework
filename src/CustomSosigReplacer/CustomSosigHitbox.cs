using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FistVR;
using HarmonyLib;
using UnityEngine;

namespace CustomSosigReplacer
{
    internal sealed class CustomSosigHitbox : MonoBehaviour, IFVRDamageable
    {
        private static bool _firstDamageLogged;
        private static readonly FieldInfo WearablesField = AccessTools.Field(typeof(SosigLink), "m_wearables");
        private SosigLink _target;
        private Rigidbody _body;
        private int _damageMultiplier = 1;
        private bool _armorEligible;
        private bool _bloodImpacts;
        private string _addonId;

        internal bool HasArmorProtection
        {
            get
            {
                return _armorEligible && FindProtectiveWearable() != null;
            }
        }

        private SosigWearable FindProtectiveWearable()
        {
            if (_target == null || !_target.HasWearables()) return null;
            List<SosigWearable> wearables = WearablesField != null
                ? WearablesField.GetValue(_target) as List<SosigWearable> : null;
            if (wearables != null)
            {
                for (int index = 0; index < wearables.Count; index++)
                    if (IsActiveArmor(wearables[index])) return wearables[index];
                return null;
            }
            SosigWearable fallback = _target.GetRandomWearable();
            return IsActiveArmor(fallback) ? fallback : null;
        }

        private static bool IsActiveArmor(SosigWearable armor)
        {
            if (armor == null || armor.Cols == null) return false;
            foreach (Collider collider in armor.Cols)
                if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy)
                    return true;
            return false;
        }

        public void Initialize(SosigLink target, Rigidbody body, int damageMultiplier, bool armorEligible, bool bloodImpacts, string addonId)
        {
            _target = target;
            _body = body;
            _damageMultiplier = Mathf.Max(1, damageMultiplier);
            _armorEligible = armorEligible;
            _bloodImpacts = bloodImpacts;
            _addonId = addonId;
        }

        public void Damage(Damage damage)
        {
            if (_target == null) return;
            if (_armorEligible && damage.Class == FistVR.Damage.DamageClass.Projectile && _target.HasWearables())
            {
                SosigWearable armor = null;
                Vector3 normal = damage.hitNormal.normalized;
                bool hitNativeArmor = normal.sqrMagnitude > 0.001f &&
                    _target.HitsWearable(damage.point + normal * 0.5f, -normal, 1.5f, out armor);
                if (!hitNativeArmor || !IsActiveArmor(armor)) armor = FindProtectiveWearable();
                if (armor != null)
                {
                    // The custom mesh hides the native armor geometry. Route the
                    // projectile through H3VR's wearable damage transmission;
                    // a direct SosigLink.Damage call bypasses armor entirely.
                    armor.Damage(new Damage(damage));
                    StartCoroutine(ApplyDeathImpulse(damage));
                    return;
                }
            }
            Damage routedDamage = new Damage(damage);
            if (_damageMultiplier > 1)
            {
                routedDamage.Dam_Blunt *= _damageMultiplier;
                routedDamage.Dam_Piercing *= _damageMultiplier;
                routedDamage.Dam_Cutting *= _damageMultiplier;
                routedDamage.Dam_TotalKinetic *= _damageMultiplier;
                routedDamage.Dam_Thermal *= _damageMultiplier;
                routedDamage.Dam_Chilling *= _damageMultiplier;
                routedDamage.Dam_EMP *= _damageMultiplier;
                routedDamage.Dam_TotalEnergetic *= _damageMultiplier;
                routedDamage.Dam_Stunning *= _damageMultiplier;
                routedDamage.Dam_Blinding *= _damageMultiplier;
            }
            if (!_firstDamageLogged)
            {
                _firstDamageLogged = true;
                Plugin.Log.LogInfo("First custom hitbox damage routed to Sosig body part " + _target.BodyPart + " with multiplier x" + _damageMultiplier + ".");
            }
            if (_bloodImpacts) Plugin.SpawnBloodImpact(_addonId, damage.point, damage.hitNormal);
            _target.Damage(routedDamage);
            StartCoroutine(ApplyDeathImpulse(damage));
        }

        private IEnumerator ApplyDeathImpulse(Damage damage)
        {
            yield return new WaitForSeconds(0.1f);
            if (_body == null || _body.isKinematic) yield break;
            float force = damage.Dam_TotalKinetic / 20f;
            if (force <= 0f) yield break;
            _body.AddForceAtPosition(-damage.hitNormal.normalized * force, damage.point, ForceMode.Impulse);
        }
    }
}
