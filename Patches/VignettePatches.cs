using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace MiSideVR.Patches
{
    // Integrated into MiSideVR via the existing harmony.PatchAll() call in MiSideVRCore.
    // This suppresses the broken menu vignette images that cause the light/dark menu issue.
    [HarmonyPatch(typeof(Graphic), "OnEnable")]
    public static class VignetteSuppressPatches
    {
        [HarmonyPostfix]
        public static void Postfix(Graphic __instance)
        {
            try
            {
                if (!IsValid(__instance)) return;

                var go = __instance.gameObject;
                if (!IsValid(go)) return;

                string name = go.name;

                // The game has both "Vignette" and a typo'd "Viggnete".
                bool isVignette =
                    name.IndexOf("vignette", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("viggnete", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!isVignette) return;

                bool inMenu = false;

                var root = go.transform.root;
                if (IsValid(root) && root.name == "MenuGame")
                    inMenu = true;

                if (!inMenu)
                {
                    try
                    {
                        if (go.scene.name == "SceneMenu")
                            inMenu = true;
                    }
                    catch { }
                }

                if (inMenu && __instance.enabled)
                    __instance.enabled = false;
            }
            catch { }
        }

        private static bool IsValid(UnityEngine.Object obj)
        {
            if (System.Object.ReferenceEquals(obj, null)) return false;
            try { return obj.GetInstanceID() != 0; }
            catch { return false; }
        }
    }
}