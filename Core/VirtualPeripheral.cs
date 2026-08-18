using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Valve.VR;
using Mathf = UnityEngine.Mathf;

namespace MiSideVR.Core
{
    public class VirtualPeripheral : MonoBehaviour
    {
        public VirtualPeripheral(IntPtr ptr) : base(ptr) { }

        public static VirtualPeripheral Instance { get; private set; }

        public bool RightTriggerHeld;
        public bool RightTriggerDown;
        public bool RightTriggerUp;

        private bool _prevTrigger;
        private bool _prevMenu;
        private bool _prevHeightCalibrate;

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private const byte VK_W = 0x57, VK_A = 0x41, VK_S = 0x53, VK_D = 0x44;
        private const byte VK_SHIFT = 0xA0, VK_E = 0x45, VK_SPACE = 0x20, VK_ESCAPE = 0x1B;

        // OpenVR k_EButton_A.
        // On Oculus Touch, this is the right-hand A button.
        // On most other OpenVR controllers, this maps to the equivalent A / primary face button.
        private const ulong A_BUTTON_MASK = 1UL << 7;

        private bool _wHeld, _aHeld, _sHeld, _dHeld, _shiftHeld, _eHeld;

        private static bool IsValid(UnityEngine.Object obj)
        {
            if (ReferenceEquals(obj, null)) return false;
            try { return obj.GetInstanceID() != 0; }
            catch { return false; }
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Update()
        {
            if (OpenVR.System == null) return;
            ReadControllers();
        }

        private void ReadControllers()
        {
            float axisX = 0, axisY = 0, trigger = 0;
            bool grip = false, menu = false;
            bool heightCalibrateButton = false;

            for (uint i = 0; i < OpenVR.k_unMaxTrackedDeviceCount; i++)
            {
                if (OpenVR.System.GetTrackedDeviceClass(i) == ETrackedDeviceClass.Controller)
                {
                    VRControllerState_t state = new VRControllerState_t();

                    if (OpenVR.System.GetControllerState(i, ref state, (uint)Marshal.SizeOf(typeof(VRControllerState_t))))
                    {
                        var role = OpenVR.System.GetControllerRoleForTrackedDeviceIndex(i);

                        if (role == ETrackedControllerRole.LeftHand)
                        {
                            // Left stick / touchpad movement
                            axisX = state.rAxis0.x;
                            axisY = state.rAxis0.y;
                        }
                        else if (role == ETrackedControllerRole.RightHand)
                        {
                            // Right trigger
                            trigger = state.rAxis1.x;

                            // Grip = shift
                            grip =
                                (state.ulButtonPressed & (1UL << ((int)EVRButtonId.k_EButton_Grip))) != 0 ||
                                state.rAxis2.x > 0.5f;

                            // Application menu button = Escape
                            menu =
                                (state.ulButtonPressed & (1UL << ((int)EVRButtonId.k_EButton_ApplicationMenu))) != 0;

                            // A button = height recalibration
                            heightCalibrateButton =
                                (state.ulButtonPressed & A_BUTTON_MASK) != 0;
                        }
                    }
                }
            }

            // Movement
            SimulateKey(VK_W, axisY > 0.5f, ref _wHeld);
            SimulateKey(VK_S, axisY < -0.5f, ref _sHeld);
            SimulateKey(VK_A, axisX < -0.5f, ref _aHeld);
            SimulateKey(VK_D, axisX > 0.5f, ref _dHeld);

            // Sprint
            SimulateKey(VK_SHIFT, grip, ref _shiftHeld);

            // Interact
            bool interact = trigger > 0.5f;
            SimulateKey(VK_E, interact, ref _eHeld);

            RightTriggerDown = interact && !_prevTrigger;
            RightTriggerUp = !interact && _prevTrigger;
            RightTriggerHeld = interact;
            _prevTrigger = interact;

            // Some game actions expect Space on trigger press
            if (RightTriggerDown)
            {
                keybd_event(VK_SPACE, 0, KEYEVENTF_KEYDOWN, 0);
                keybd_event(VK_SPACE, 0, KEYEVENTF_KEYUP, 0);
            }

            // Pause / menu
            if (menu && !_prevMenu)
            {
                keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYDOWN, 0);
                keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, 0);
            }

            _prevMenu = menu;

            // Right A button: recalibrate VR height.
            if (heightCalibrateButton && !_prevHeightCalibrate)
            {
                TryRecalibrateHeight();
            }

            _prevHeightCalibrate = heightCalibrateButton;
        }

        private void SimulateKey(byte vk, bool shouldPress, ref bool isHeld)
        {
            if (shouldPress && !isHeld)
            {
                keybd_event(vk, 0, KEYEVENTF_KEYDOWN, 0);
                isHeld = true;
            }
            else if (!shouldPress && isHeld)
            {
                keybd_event(vk, 0, KEYEVENTF_KEYUP, 0);
                isHeld = false;
            }
        }

        private void TryRecalibrateHeight()
        {
            try
            {
                if (!IsValid(VRManager.Instance)) return;
                if (!IsValid(VRManager.Instance.HMD)) return;
                if (!IsValid(VRManager.Instance.VRRig)) return;

                var playerObj = GameObject.Find("Player");
                if (!IsValid(playerObj)) return;

                var playerMove = playerObj.GetComponent<PlayerMove>();
                if (!IsValid(playerMove)) return;
                if (!IsValid(playerMove.head)) return;

                // Only allow recalibration when the VR rig is actually aligned with the player.
                // This prevents accidental recalibration during menus/cutscenes.
                var capsule = playerObj.GetComponent<CapsuleCollider>();
                float feetY = playerObj.transform.position.y;

                if (IsValid(capsule))
                    feetY += capsule.center.y - (capsule.height * 0.5f);

                float rigY = VRManager.Instance.VRRig.transform.position.y;
                if (Mathf.Abs(rigY - feetY) > 0.75f)
                    return;

                float scale = VRManager.Instance.TrackingScale;
                if (scale < 0.001f) scale = 1f;

                // Current scaled HMD eye height above the rig origin.
                float scaledEyeHeight = VRManager.Instance.HMD.transform.localPosition.y;

                // Recover the raw physical eye height before our scale was applied.
                float rawPhysicalEyeHeight = scaledEyeHeight / scale;

                // Ignore invalid tracking heights.
                if (rawPhysicalEyeHeight < 0.35f) return;

                float gameEyeHeight = playerMove.head.localPosition.y;
                if (gameEyeHeight < 0.05f) return;

                float newScale = gameEyeHeight / rawPhysicalEyeHeight;
                newScale = Mathf.Clamp(newScale, 0.4f, 3.0f);

                VRManager.Instance.TrackingScale = newScale;
                VRManager.Instance.HeightCalibrated = true;
            }
            catch { }
        }
    }
}