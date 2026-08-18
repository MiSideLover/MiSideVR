using System;
using UnityEngine;
using Valve.VR;
using Application = UnityEngine.Application;

namespace MiSideVR.Core
{
    public class VRManager : MonoBehaviour
    {
        public VRManager(IntPtr ptr) : base(ptr) { }
        public static VRManager Instance;
        public GameObject VRRig;
        public GameObject HMD;
        public GameObject LeftController;
        public GameObject RightController;

        public float TrackingScale = 1f;
        public bool HeightCalibrated = false;

        private bool _openVRInitialized = false;
        private int _initFrame = 0;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject); // CRITICAL FIX: Prevent VRManager from dying on scene load
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 90;
        }

        private void Update()
        {
            if (!_openVRInitialized)
            {
                _initFrame++;
                if (_initFrame > 100) { InitializeOpenVR(); _openVRInitialized = true; }
            }
        }

        private void InitializeOpenVR()
        {
            if (OpenVR.System == null)
            {
                var error = EVRInitError.None;
                OpenVR.Init(ref error, EVRApplicationType.VRApplication_Scene);
                if (error != EVRInitError.None) return;
            }
            CreateRig();
        }

        private void CreateRig()
        {
            if (VRRig != null) return;
            VRRig = new GameObject("VRRig");
            UnityEngine.Object.DontDestroyOnLoad(VRRig);
            VRRig.hideFlags = HideFlags.HideAndDontSave;
            VRRig.AddComponent<VirtualPeripheral>();

            HMD = new GameObject("HMD");
            HMD.transform.parent = VRRig.transform;
            HMD.AddComponent<CameraManager>();

            LeftController = new GameObject("LeftController");
            LeftController.transform.parent = VRRig.transform;
            var leftVisuals = LeftController.AddComponent<ControllerVisuals>();
            leftVisuals.Role = ETrackedControllerRole.LeftHand;

            RightController = new GameObject("RightController");
            RightController.transform.parent = VRRig.transform;
            var rightVisuals = RightController.AddComponent<ControllerVisuals>();
            rightVisuals.Role = ETrackedControllerRole.RightHand;
        }

        public void CalibrateHeight(float gameEyeHeight)
        {
            if (!HeightCalibrated && HMD != null)
            {
                float physicalEyeHeight = HMD.transform.localPosition.y;
                if (physicalEyeHeight > 0.5f)
                {
                    TrackingScale = gameEyeHeight / physicalEyeHeight;
                    HeightCalibrated = true;
                }
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}