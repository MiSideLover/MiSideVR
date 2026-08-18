using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MiSideVR.Core
{
    public class VRPointerInput : BaseInput
    {
        public VRPointerInput(IntPtr ptr) : base(ptr) { }
        public Camera eventCamera;

        private static bool IsValid(UnityEngine.Object obj)
        {
            if (System.Object.ReferenceEquals(obj, null)) return false;
            try { return obj.GetInstanceID() != 0; }
            catch { return false; }
        }

        public override bool GetMouseButton(int button)
        {
            if (button == 0 && IsValid(VirtualPeripheral.Instance)) return VirtualPeripheral.Instance.RightTriggerHeld;
            return Input.GetMouseButton(button);
        }

        public override bool GetMouseButtonDown(int button)
        {
            if (button == 0 && IsValid(VirtualPeripheral.Instance)) return VirtualPeripheral.Instance.RightTriggerDown;
            return Input.GetMouseButtonDown(button);
        }

        public override bool GetMouseButtonUp(int button)
        {
            if (button == 0 && IsValid(VirtualPeripheral.Instance)) return VirtualPeripheral.Instance.RightTriggerUp;
            return Input.GetMouseButtonUp(button);
        }

        public override Vector2 mousePosition
        {
            get
            {
                if (IsValid(eventCamera))
                    return new Vector2(eventCamera.pixelWidth / 2f, eventCamera.pixelHeight / 2f);
                return Input.mousePosition;
            }
        }
    }
}