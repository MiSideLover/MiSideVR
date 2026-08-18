using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using MiSideVR.Core;

namespace MiSideVR.Patches
{
    // Freezes menu UI in world-space while SceneMenu is active.
    // Prevents the menu from being thrown behind you when entering
    // Characters / Outfits / other menu sub-screens.
    [HarmonyPatch(typeof(UIFollowCamera), "LateUpdate")]
    public static class MenuStaticUIPatches
    {
        private class StaticPose
        {
            public Vector3 Pos;
            public Quaternion Rot;
            public Vector3 Scale;
        }

        private static readonly Dictionary<int, StaticPose> _savedPoses =
            new Dictionary<int, StaticPose>();

        private static StaticPose _anchorPose;
        private static string _lastScene = "";
        private static bool _menuContext;

        private static void HandleSceneChange(string scene)
        {
            if (scene == _lastScene) return;

            _lastScene = scene;
            _savedPoses.Clear();
            _anchorPose = null;

            // Main menu context.
            // If your outfit/character screens use a different scene name,
            // we can expand this condition later.
            _menuContext = scene == "SceneMenu";
        }

        [HarmonyPrefix]
        public static bool Prefix(UIFollowCamera __instance)
        {
            try
            {
                string scene = SceneManager.GetActiveScene().name;
                HandleSceneChange(scene);

                if (!_menuContext) return true;
                if (!IsValid(__instance)) return true;

                var t = __instance.transform;
                if (!IsValid(t)) return true;

                int id = __instance.GetInstanceID();

                // Already locked: force it and skip the normal UIFollowCamera logic.
                if (_savedPoses.TryGetValue(id, out var saved))
                {
                    Apply(t, saved);
                    return false;
                }

                // If we already have a stable menu anchor, force new menu panels
                // to that same stable pose instead of letting them get thrown.
                if (_anchorPose != null)
                {
                    var pose = Clone(_anchorPose);
                    Apply(t, pose);
                    _savedPoses[id] = pose;
                    return false;
                }
            }
            catch { }

            // First-time placement: allow normal UIFollowCamera to place it once.
            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(UIFollowCamera __instance)
        {
            try
            {
                string scene = SceneManager.GetActiveScene().name;
                HandleSceneChange(scene);

                if (!_menuContext) return;
                if (!IsValid(__instance)) return;

                var t = __instance.transform;
                if (!IsValid(t)) return;

                int id = __instance.GetInstanceID();

                if (_savedPoses.ContainsKey(id)) return;

                // Ignore obviously unplaced canvases.
                if (t.position.sqrMagnitude < 0.01f) return;

                var pose = new StaticPose
                {
                    Pos = t.position,
                    Rot = t.rotation,
                    Scale = t.localScale
                };

                _savedPoses[id] = pose;

                string path = GetPath(t);

                // Prefer anchoring to the main menu canvas rather than a loading/black-screen canvas.
                bool preferred =
                    path.Contains("MenuGame/Canvas") ||
                    path.StartsWith("Game/Interface", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("GameController/Interface", StringComparison.OrdinalIgnoreCase);

                if (_anchorPose == null || preferred)
                {
                    _anchorPose = Clone(pose);

                    // If we found a better anchor later, snap already-saved menu panels to it.
                    foreach (var kv in _savedPoses)
                        kv.Value.Pos = _anchorPose.Pos;
                }
            }
            catch { }
        }

        private static void Apply(Transform t, StaticPose pose)
        {
            if (!IsValid(t) || pose == null) return;

            t.position = pose.Pos;
            t.rotation = pose.Rot;
            t.localScale = pose.Scale;
        }

        private static StaticPose Clone(StaticPose pose)
        {
            return new StaticPose
            {
                Pos = pose.Pos,
                Rot = pose.Rot,
                Scale = pose.Scale
            };
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

        private static bool IsValid(UnityEngine.Object obj)
        {
            if (System.Object.ReferenceEquals(obj, null)) return false;
            try { return obj.GetInstanceID() != 0; }
            catch { return false; }
        }
    }
}