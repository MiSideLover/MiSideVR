using System;
using UnityEngine;
using Valve.VR;

namespace MiSideVR.Core
{
    public class ControllerVisuals : MonoBehaviour
    {
        public ControllerVisuals(IntPtr ptr) : base(ptr) { }

        public ETrackedControllerRole Role;

        private Transform _muzzle;
        private LineRenderer _line;

        public Camera EventCamera { get; private set; }

        // CONFIRMED by your tuner log:
        // original was (-0.02, -0.04, 0.08)
        // corrected to centered X:
        private static readonly Vector3 LaserMuzzleOffset = new Vector3(0f, -0.04f, 0.08f);

        private static bool IsValid(UnityEngine.Object obj)
        {
            if (System.Object.ReferenceEquals(obj, null)) return false;
            try { return obj.GetInstanceID() != 0; }
            catch { return false; }
        }

        private void Awake()
        {
            try { gameObject.AddComponent<SteamVR_RenderModel>(); } catch { }

            GameObject modelRoot = new GameObject("PhysicalModel");
            modelRoot.transform.SetParent(transform, false);

            var grip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            grip.transform.SetParent(modelRoot.transform, false);
            grip.transform.localScale = new Vector3(0.04f, 0.04f, 0.12f);
            grip.transform.localPosition = new Vector3(0f, -0.02f, 0.02f);
            grip.transform.localRotation = Quaternion.Euler(35f, 0f, 0f);
            UnityEngine.Object.Destroy(grip.GetComponent<Collider>());

            var gripRend = grip.GetComponent<Renderer>();
            if (IsValid(gripRend)) gripRend.material.color = new Color(0.2f, 0.2f, 0.2f);

            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.transform.SetParent(modelRoot.transform, false);
            ring.transform.localScale = new Vector3(0.08f, 0.01f, 0.08f);
            ring.transform.localPosition = new Vector3(0f, 0.03f, 0.05f);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            UnityEngine.Object.Destroy(ring.GetComponent<Collider>());

            var ringRend = ring.GetComponent<Renderer>();
            if (IsValid(ringRend)) ringRend.material.color = new Color(0.1f, 0.1f, 0.1f);

            GameObject muzzleObj = new GameObject("LaserMuzzle");
            muzzleObj.transform.SetParent(transform, false);
            muzzleObj.transform.localPosition = LaserMuzzleOffset;
            muzzleObj.transform.localRotation = Quaternion.Euler(35f, 0f, 0f);
            _muzzle = muzzleObj.transform;

            GameObject eventCamObj = new GameObject("EventCamera");
            eventCamObj.transform.SetParent(_muzzle, false);
            eventCamObj.transform.localPosition = Vector3.zero;
            eventCamObj.transform.localRotation = Quaternion.identity;

            EventCamera = eventCamObj.AddComponent<Camera>();
            EventCamera.enabled = true;
            EventCamera.cullingMask = 0;
            EventCamera.clearFlags = CameraClearFlags.Nothing;
            EventCamera.nearClipPlane = 0.01f;
            EventCamera.farClipPlane = 100f;
            EventCamera.fieldOfView = 60f;
            EventCamera.stereoTargetEye = StereoTargetEyeMask.None;
            EventCamera.depth = -100f;

            _line = muzzleObj.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.startWidth = 0.005f;
            _line.endWidth = 0.005f;
            _line.positionCount = 2;

            Shader shader = Resources.GetBuiltinResource<Shader>("Sprites-Default.shader");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Hidden/Internal-Colored");

            if (shader != null)
            {
                _line.material = new Material(shader);
                _line.startColor = Color.red;
                _line.endColor = Color.red;
            }
            else
            {
                _line.enabled = false;
            }
        }

        private void LateUpdate()
        {
            if (!IsValid(this) || !IsValid(_muzzle) || !IsValid(_line)) return;

            Vector3 startPos = _muzzle.position;
            Vector3 direction = _muzzle.forward;
            Vector3 endPos = startPos + (direction * 50f);

            if (Physics.Raycast(startPos, direction, out RaycastHit hit, 50f))
                endPos = hit.point;

            _line.SetPosition(0, startPos);
            _line.SetPosition(1, endPos);
        }

        public Ray GetAimRay()
        {
            if (!IsValid(_muzzle)) return new Ray(Vector3.zero, Vector3.forward);
            return new Ray(_muzzle.position, _muzzle.forward);
        }
    }
}