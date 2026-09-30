using FistVR;
using HarmonyLib;

namespace CustomSosigReplacer
{
    [HarmonyPatch(typeof(Sosig), "Start")]
    internal static class SosigStartPatch
    {
        private static void Postfix(Sosig __instance)
        {
            if (__instance == null || Plugin.RuntimeSettings == null || !Plugin.RuntimeSettings.Enabled)
            {
                return;
            }

            Plugin.TryAttachProxy(__instance, "Sosig.Start");
        }
    }
}
