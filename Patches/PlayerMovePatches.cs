using HarmonyLib;
using UnityEngine;

namespace MiSideVR.Patches
{
    public static class PlayerMovePatches
    {
        [HarmonyPatch(typeof(PlayerMove), "Update")]
        [HarmonyPrefix]
        public static bool UpdatePrefix(PlayerMove __instance)
        {
            if (ReferenceEquals(__instance, null)) return true;
            try { if (__instance.GetInstanceID() == 0) return true; } catch { return true; }

            __instance.stopMouseMove = true;
            __instance.intensityMouse = 0f;
            return true;
        }

        [HarmonyPatch(typeof(PlayerMove), "Look")]
        [HarmonyPrefix]
        public static bool LookPrefix(PlayerMove __instance)
        {
            if (ReferenceEquals(__instance, null)) return true;
            try { if (__instance.GetInstanceID() == 0) return true; } catch { return true; }
            return false;
        }
    }
}