using FistVR;
using HarmonyLib;

namespace CustomSosigReplacer
{
    // H3VR's bleeding event also owns blood-loss bookkeeping. Hide only its
    // mustard particle renderer; never skip the event or its damage update.
    [HarmonyPatch(typeof(Sosig.BleedingEvent), MethodType.Constructor,
        new[] { typeof(UnityEngine.GameObject), typeof(SosigLink), typeof(float),
                typeof(UnityEngine.Vector3), typeof(UnityEngine.Vector3), typeof(float), typeof(float) })]
    internal static class SosigBleedingVfxPatch
    {
        private static void Postfix(Sosig.BleedingEvent __instance)
        {
            if (__instance == null || __instance.l == null || __instance.l.S == null) return;
            SosigVisualProxy proxy = __instance.l.S.GetComponent<SosigVisualProxy>();
            if (proxy != null && proxy.UsesBloodImpacts) proxy.SuppressNativeBleed(__instance.m_system);
        }
    }
}
