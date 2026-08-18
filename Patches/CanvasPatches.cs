using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using MiSideVR.Core;
using System.Collections.Generic;

namespace MiSideVR.Patches
{
    [HarmonyPatch(typeof(CanvasScaler), "OnEnable")]
    public static class CanvasPatches
    {
        private static readonly HashSet<int> ProcessedCanvasIds = new HashSet<int>();
        private static readonly HashSet<int> SkippedCanvasIds = new HashSet<int>();

        internal static bool IsValid(UnityEngine.Object obj)
        {
            if (System.Object.ReferenceEquals(obj, null)) return false;
            try { return obj.GetInstanceID() != 0; }
            catch { return false; }
        }

        [HarmonyPostfix]
        public static void Postfix(CanvasScaler __instance)
        {
            try
            {
                if (!IsValid(__instance)) return;
                if (!IsValid(VRManager.Instance)) return;

                var canvas = __instance.GetComponent<Canvas>();
                if (!IsValid(canvas)) return;

                int canvasId = canvas.GetInstanceID();
                if (ProcessedCanvasIds.Contains(canvasId)) return;
                if (SkippedCanvasIds.Contains(canvasId)) return;

                // Only process root canvases.
                var root = canvas.rootCanvas;
                if (IsValid(root) && root.GetInstanceID() != canvasId)
                {
                    SkippedCanvasIds.Add(canvasId);
                    return;
                }

                RenderMode originalMode = canvas.renderMode;
                string path = GetPath(canvas.transform);

                // CONFIRMED FIX:
                // Do NOT move canvases that were already world-space.
                // Those are world/diegetic UI and should stay where they are.
                if (originalMode == RenderMode.WorldSpace)
                {
                    SkippedCanvasIds.Add(canvasId);
                    return;
                }

                // Avoid dragging third-party overlay/tool canvases into VR UI space.
                if (IsToolCanvas(path))
                {
                    SkippedCanvasIds.Add(canvasId);
                    return;
                }

                ProcessedCanvasIds.Add(canvasId);

                if (canvas.renderMode != RenderMode.WorldSpace)
                    canvas.renderMode = RenderMode.WorldSpace;

                var raycaster = canvas.GetComponent<GraphicRaycaster>();
                if (IsValid(raycaster))
                {
                    raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
                    raycaster.ignoreReversedGraphics = true;
                }

                canvas.transform.localScale = Vector3.one * 0.0008f;

                if (!IsValid(canvas.GetComponent<UIFollowCamera>()))
                    canvas.gameObject.AddComponent<UIFollowCamera>();

                Camera uiCam = GetUiCamera();
                if (IsValid(uiCam))
                    canvas.worldCamera = uiCam;
            }
            catch { }
        }

        private static Camera GetUiCamera()
        {
            // Prefer controller EventCamera if it exists.
            // Do NOT require activeInHierarchy here, because some canvases initialize early.
            if (IsValid(VRManager.Instance) && IsValid(VRManager.Instance.RightController))
            {
                var visuals = VRManager.Instance.RightController.GetComponent<ControllerVisuals>();
                if (IsValid(visuals) && IsValid(visuals.EventCamera))
                    return visuals.EventCamera;
            }

            return Camera.main;
        }

        private static bool IsToolCanvas(string path)
        {
            return path.Contains("UniverseLibCanvas")
                || path.Contains("MS_CustomModels_Root")
                || path.Contains("resizeCursor_Root");
        }

        private static string GetPath(Transform t)
        {
            if (!IsValid(t)) return "";

            string path = t.name;
            var p = t.parent;
            int guard = 0;

            while (IsValid(p) && guard++ < 64)
            {
                path = p.name + "/" + path;
                p = p.parent;
            }

            return path;
        }
    }
}