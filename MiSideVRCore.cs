using System;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using BepInEx.Logging;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using HarmonyLib;
using Object = UnityEngine.Object;

namespace MiSideVR.Core
{
    [BepInPlugin("com.misidevr.core", "MiSideVR", "1.0.0-pre1")]
    public class MiSideVRCore : BasePlugin
    {
        internal static new ManualLogSource Log = null!;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("MiSide VR Core Loading Phase 2...");

            // CRITICAL: Register ALL custom MonoBehaviours so IL2CPP allows AddComponent
            ClassInjector.RegisterTypeInIl2Cpp<VRManager>();
            ClassInjector.RegisterTypeInIl2Cpp<CameraManager>();
            ClassInjector.RegisterTypeInIl2Cpp<VirtualPeripheral>();
            ClassInjector.RegisterTypeInIl2Cpp<ControllerVisuals>();
            ClassInjector.RegisterTypeInIl2Cpp<UIFollowCamera>();    // NEW UI
            ClassInjector.RegisterTypeInIl2Cpp<VRPointerInput>();    // NEW UI

            // Initialize Harmony Patches (CanvasPatches, PlayerMovePatches)
            var harmony = new Harmony("com.misidevr.patches");
            harmony.PatchAll();

            // Spawn the master manager
            var managerObj = new GameObject("MiSideVR_Manager");
            Object.DontDestroyOnLoad(managerObj);
            managerObj.hideFlags = HideFlags.HideAndDontSave;
            managerObj.AddComponent<VRManager>();
        }
    }
}