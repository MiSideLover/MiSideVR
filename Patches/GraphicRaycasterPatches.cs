using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using MiSideVR.Core;

namespace MiSideVR.Patches
{
    // This is the real raycast fix.
    // We force GraphicRaycaster to use the controller EventCamera for converted VR UI canvases.
    [HarmonyPatch(typeof(GraphicRaycaster), "get_eventCamera")]
    public static class GraphicRaycasterPatches
    {
        private static bool IsValid(UnityEngine.Object obj)
        {
            if (System.Object.ReferenceEquals(obj, null)) return false;
            try { return obj.GetInstanceID() != 0; }
            catch { return false; }
        }

        private static bool Prefix(GraphicRaycaster __instance, ref Camera __result)
        {
            try
            {
                if (!IsValid(__instance)) return true;

                var canvas = __instance.GetComponent<Canvas>();
                if (!IsValid(canvas)) return true;

                if (HasVRUIFollow(canvas))
                {
                    Camera preferred = GetPreferredUiCamera();
                    if (IsValid(preferred))
                    {
                        __result = preferred;
                        return false;
                    }
                }
            }
            catch { }

            return true;
        }

        private static bool HasVRUIFollow(Canvas canvas)
        {
            if (!IsValid(canvas)) return false;

            if (IsValid(canvas.GetComponent<UIFollowCamera>()))
                return true;

            var root = canvas.rootCanvas;
            if (IsValid(root) && IsValid(root.GetComponent<UIFollowCamera>()))
                return true;

            return false;
        }

        private static Camera GetPreferredUiCamera()
        {
            // Use controller EventCamera when it is actually usable.
            if (IsValid(VRManager.Instance) && IsValid(VRManager.Instance.RightController))
            {
                var visuals = VRManager.Instance.RightController.GetComponent<ControllerVisuals>();
                if (IsValid(visuals) && IsValid(visuals.EventCamera))
                {
                    var cam = visuals.EventCamera;
                    if (cam.enabled && cam.gameObject.activeInHierarchy)
                        return cam;
                }
            }

            // Fallback for early init / hidden controllers.
            return Camera.main;
        }
    }
}