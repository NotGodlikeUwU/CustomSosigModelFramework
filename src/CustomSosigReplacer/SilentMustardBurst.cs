using FistVR;
using HarmonyLib;
using UnityEngine;

namespace CustomSosigReplacer
{
    // H3VR expects Instantiate to return a GameObject for every mustard burst.
    // These visually empty clones self-clean; the plugin-owned template remains.
    internal sealed class SilentMustardBurst : MonoBehaviour
    {
        private void Start()
        {
            if (!Plugin.IsSilentMustardBurstTemplate(gameObject)) Destroy(gameObject, 2f);
        }
    }

    [HarmonyPatch(typeof(Sosig), "RequestHitDecal",
        new[] { typeof(Vector3), typeof(Vector3), typeof(float), typeof(SosigLink) })]
    internal static class SosigHitDecalPatch
    {
        internal static bool ShouldShowNativeDecal(Sosig sosig)
        {
            SosigVisualProxy proxy = sosig == null ? null : sosig.GetComponent<SosigVisualProxy>();
            return proxy == null || !proxy.UsesBloodImpacts;
        }

        private static bool Prefix(Sosig __instance) { return ShouldShowNativeDecal(__instance); }
    }

    [HarmonyPatch(typeof(Sosig), "RequestHitDecal",
        new[] { typeof(Vector3), typeof(Vector3), typeof(Vector3), typeof(float), typeof(SosigLink) })]
    internal static class SosigDirectionalHitDecalPatch
    {
        private static bool Prefix(Sosig __instance) { return SosigHitDecalPatch.ShouldShowNativeDecal(__instance); }
    }
}
