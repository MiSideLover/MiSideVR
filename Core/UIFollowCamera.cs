using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Mathf = UnityEngine.Mathf;

namespace MiSideVR.Core
{
    public class UIFollowCamera : MonoBehaviour
    {
        public UIFollowCamera(IntPtr ptr) : base(ptr) { }

        private Camera _vrCamera;
        private int _cachedCamId = 0;

        private Canvas _canvas;
        private float _instanceBias;

        private PlayerMove _player;
        private int _lastPlayerScanFrame = -1000;
        private int _lastPointerScanFrame = -1000;

        private bool _placed = false;
        private bool _hadPlayer = false;

        private Vector3 _anchorOffset = Vector3.zero;
        private Quaternion _baseRotation = Quaternion.identity;

        private bool _pointerRootChecked = false;
        private bool _isPointerRoot = false;

        private readonly HashSet<int> _hiddenLogged = new HashSet<int>();

        private static bool _dumpRequested = false;
        private static int _lastDumpFrame = -1000;
        private static bool _cursorHidden = false;

        private static bool IsValid(UnityEngine.Object obj)
        {
            if (System.Object.ReferenceEquals(obj, null)) return false;
            try { return obj.GetInstanceID() != 0; }
            catch { return false; }
        }

        private void Awake()
        {
            _canvas = GetComponent<Canvas>();

            int id = GetInstanceID();
            _instanceBias = -0.004f * (System.Math.Abs(id % 8));
        }

        private void OnEnable()
        {
            _cachedCamId = 0;
            _placed = false;
        }

        private void LateUpdate()
        {
            try
            {
                if (!IsValid(this) || !IsValid(gameObject)) return;

                if (Input.GetKeyDown(KeyCode.F9))
                    _dumpRequested = true;

                if (_dumpRequested && Time.frameCount != _lastDumpFrame)
                {
                    _lastDumpFrame = Time.frameCount;
                    _dumpRequested = false;
                    DumpAllUI();
                }

                UpdateWorldCamera();

                if (HandlePointerCanvasRoot())
                    return;

                EnsureVRCamera();
                HideNativePointer();
                HideSystemCursorIfNeeded();
                UpdateFollow();
            }
            catch { }
        }

        private void EnsureVRCamera()
        {
            Camera main = Camera.main;
            if (!IsValid(main)) return;

            int id = main.GetInstanceID();
            if (!IsValid(_vrCamera) || _cachedCamId != id)
            {
                _vrCamera = main;
                _cachedCamId = id;
                _placed = false;
            }
        }

        private void UpdateWorldCamera()
        {
            if (!IsValid(_canvas))
                _canvas = GetComponent<Canvas>();

            if (!IsValid(_canvas)) return;

            Camera preferred = GetPreferredUiCamera();
            if (IsValid(preferred) && _canvas.worldCamera != preferred)
                _canvas.worldCamera = preferred;
        }

        private static Camera GetPreferredUiCamera()
        {
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

            return Camera.main;
        }

        private void UpdateFollow()
        {
            if (!IsValid(_vrCamera)) return;

            bool hasPlayer = TryGetPlayerAnchor(out Vector3 playerPos);
            if (_hadPlayer != hasPlayer)
                _placed = false;

            _hadPlayer = hasPlayer;

            Vector3 anchor = hasPlayer ? playerPos : _vrCamera.transform.position;

            if (!_placed)
            {
                PlaceInitial(anchor);
                return;
            }

            Vector3 desiredPos = anchor + _anchorOffset;

            // Safety: if UI gets stranded too far away, bring it back in front,
            // but preserve the fixed orientation.
            float distToCam = Vector3.Distance(transform.position, _vrCamera.transform.position);
            if (distToCam > 4.5f)
            {
                Vector3 camPos = _vrCamera.transform.position;

                Vector3 camForward = _vrCamera.transform.forward;
                camForward.y = 0f;
                if (camForward.sqrMagnitude < 0.01f) camForward = Vector3.forward;
                camForward.Normalize();

                float distance = GetCurrentDistance();
                Vector3 newPos = camPos + (camForward * distance);
                newPos.y = camPos.y - 0.3f;

                _anchorOffset = newPos - anchor;
                desiredPos = newPos;
            }

            // POSITION follows.
            transform.position = Vector3.Lerp(transform.position, desiredPos, Time.unscaledDeltaTime * 4f);

            // ORIENTATION remains the same.
            transform.rotation = _baseRotation;
        }

        private void PlaceInitial(Vector3 anchor)
        {
            Vector3 camPos = _vrCamera.transform.position;

            Vector3 camForward = _vrCamera.transform.forward;
            camForward.y = 0f;
            if (camForward.sqrMagnitude < 0.01f) camForward = Vector3.forward;
            camForward.Normalize();

            float distance = GetCurrentDistance();

            Vector3 spawnPos = camPos + (camForward * distance);
            spawnPos.y = camPos.y - 0.3f;

            _anchorOffset = spawnPos - anchor;
            _baseRotation = ComputeFacingRotation(spawnPos, camPos);

            transform.position = spawnPos;
            transform.rotation = _baseRotation;

            _placed = true;
        }

        private float GetCurrentDistance()
        {
            if (!IsValid(_canvas))
                _canvas = GetComponent<Canvas>();

            int sortingOrder = IsValid(_canvas) ? _canvas.sortingOrder : 0;
            float sortingBias = -sortingOrder * 0.00002f;
            sortingBias = Mathf.Clamp(sortingBias, -0.25f, 0.25f);

            float distance = 1.5f + sortingBias + _instanceBias;
            if (distance < 0.75f) distance = 0.75f;

            return distance;
        }

        private Quaternion ComputeFacingRotation(Vector3 uiPos, Vector3 camPos)
        {
            Vector3 flatCam = new Vector3(camPos.x, uiPos.y, camPos.z);
            Vector3 dir = uiPos - flatCam;

            if (dir.sqrMagnitude < 0.0001f)
                dir = Vector3.forward;

            // Equivalent to:
            // LookAt(camera), Rotate(0,180,0), Rotate(-10,0,0)
            return Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(-10f, 0f, 0f);
        }

        private bool TryGetPlayerAnchor(out Vector3 pos)
        {
            pos = Vector3.zero;

            if (Time.frameCount - _lastPlayerScanFrame > 60)
            {
                _lastPlayerScanFrame = Time.frameCount;

                if (!IsValid(_player))
                {
                    var obj = GameObject.Find("Player");
                    if (IsValid(obj))
                        _player = obj.GetComponent<PlayerMove>();
                }
            }

            if (!IsValid(_player) || !IsValid(_player.gameObject) || !_player.gameObject.activeInHierarchy)
                return false;

            if (IsCinematicCamera(_vrCamera))
                return false;

            pos = _player.transform.position;
            return true;
        }

        private static bool IsCinematicCamera(Camera cam)
        {
            if (!IsValid(cam)) return false;

            string path = GetPath(cam.transform).ToLowerInvariant();

            return path.Contains("cutscenes")
                || path.Contains("cutscene")
                || path.Contains("cinema")
                || path.Contains("timeline")
                || path.Contains("fixhead");
        }

        private void HideSystemCursorIfNeeded()
        {
            try
            {
                if (!_cursorHidden || Time.frameCount % 120 == 0)
                {
                    Cursor.visible = false;
                    _cursorHidden = true;
                }
            }
            catch { }
        }

        private bool HandlePointerCanvasRoot()
        {
            if (!_pointerRootChecked)
            {
                _pointerRootChecked = true;

                string path = GetPath(transform).ToLowerInvariant();

                _isPointerRoot = IsValid(_canvas) &&
                    (
                        path.EndsWith("/mouse") ||
                        path.Contains("/mouse/") ||
                        path.EndsWith("/cursor") ||
                        path.EndsWith("/pointer")
                    );
            }

            if (_isPointerRoot)
            {
                if (IsValid(_canvas) && _canvas.enabled)
                    _canvas.enabled = false;

                return true;
            }

            return false;
        }

        private void HideNativePointer()
        {
            if (Time.frameCount - _lastPointerScanFrame < 30)
                return;

            _lastPointerScanFrame = Time.frameCount;

            int count = 0;
            ScanAndHidePointer(transform, ref count);
        }

        private void ScanAndHidePointer(Transform parent, ref int count)
        {
            if (!IsValid(parent) || count > 512) return;

            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (!IsValid(child)) continue;

                count++;

                if (ShouldHidePointerObject(child))
                {
                    if (child.gameObject.activeSelf)
                    {
                        child.gameObject.SetActive(false);

                        int id = child.GetInstanceID();
                        if (_hiddenLogged.Add(id))
                        {
                            try
                            {
                                if (MiSideVRCore.Log != null)
                                    MiSideVRCore.Log.LogInfo($"[VR] Hid native pointer object: {GetPath(child)}");
                            }
                            catch { }
                        }
                    }
                }

                ScanAndHidePointer(child, ref count);
            }
        }

        private static bool ShouldHidePointerObject(Transform child)
        {
            if (!IsValid(child)) return false;

            string name = child.name.ToLowerInvariant();
            if (string.IsNullOrEmpty(name)) return false;

            // Never hide our own VR pointer infrastructure.
            if (name.Contains("vr") || name.Contains("laser") || name.Contains("eventcamera") || name.Contains("diag"))
                return false;

            // Do not hide actual buttons.
            var button = child.GetComponent<Button>();
            if (IsValid(button))
                return false;

            string path = GetPath(child).ToLowerInvariant();

            // Exact known native pointer objects.
            if (name == "mouse" ||
                name == "mousefill" ||
                name == "cursor" ||
                name == "pointer" ||
                name == "crosshair")
            {
                return true;
            }

            // Exact known pointer hierarchy segments.
            if (path.Contains("/mouse/") ||
                path.EndsWith("/mouse") ||
                path.Contains("/mousefill/") ||
                path.EndsWith("/mousefill") ||
                path.Contains("/cursor/") ||
                path.EndsWith("/cursor"))
            {
                return true;
            }

            // Intentionally DO NOT match loose substrings like:
            // - mouselerp
            // - changetarget
            // - icontarget
            return false;
        }

        private static void DumpAllUI()
        {
            try
            {
                if (MiSideVRCore.Log != null)
                    MiSideVRCore.Log.LogInfo("[UI-DUMP] Beginning converted UI dump...");

                var canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
                int dumped = 0;

                foreach (var canvas in canvases)
                {
                    if (!IsValid(canvas)) continue;
                    if (!HasUIFollow(canvas)) continue;

                    DumpCanvasHierarchy(canvas);
                    dumped++;

                    if (dumped >= 20) break;
                }

                if (MiSideVRCore.Log != null)
                    MiSideVRCore.Log.LogInfo($"[UI-DUMP] Completed. Dumped {dumped} converted UI canvas(es).");
            }
            catch (Exception ex)
            {
                if (MiSideVRCore.Log != null)
                    MiSideVRCore.Log.LogInfo($"[UI-DUMP] Error: {ex.Message}");
            }
        }

        private static bool HasUIFollow(Canvas canvas)
        {
            if (!IsValid(canvas)) return false;

            if (IsValid(canvas.GetComponent<UIFollowCamera>()))
                return true;

            var root = canvas.rootCanvas;
            if (IsValid(root) && IsValid(root.GetComponent<UIFollowCamera>()))
                return true;

            return false;
        }

        private static void DumpCanvasHierarchy(Canvas canvas)
        {
            try
            {
                var sb = new StringBuilder();

                string worldCam = IsValid(canvas.worldCamera) ? canvas.worldCamera.name : "null";

                sb.AppendLine($"[UI-DUMP] CANVAS {GetPath(canvas.transform)}");
                sb.AppendLine($"  renderMode={canvas.renderMode} worldCamera={worldCam} sortingOrder={canvas.sortingOrder} active={canvas.gameObject.activeInHierarchy}");

                int count = 0;
                AppendTransform(canvas.transform, 0, sb, ref count);

                if (MiSideVRCore.Log != null)
                    MiSideVRCore.Log.LogInfo(sb.ToString());
            }
            catch (Exception ex)
            {
                if (MiSideVRCore.Log != null)
                    MiSideVRCore.Log.LogInfo($"[UI-DUMP] Canvas dump error: {ex.Message}");
            }
        }

        private static void AppendTransform(Transform t, int depth, StringBuilder sb, ref int count)
        {
            if (!IsValid(t) || count >= 400) return;

            count++;

            string indent = new string(' ', depth * 2);

            var rt = t as RectTransform;
            var graphic = t.GetComponent<Graphic>();

            string graphicInfo = IsValid(graphic)
                ? $"graphic={GetTypeName(graphic)} raycast={graphic.raycastTarget}"
                : "graphic=null";

            string rectInfo = rt != null
                ? $"rect={rt.rect.width:F0}x{rt.rect.height:F0} anchored={rt.anchoredPosition}"
                : "";

            sb.AppendLine($"{indent}{t.name} active={t.gameObject.activeSelf} {graphicInfo} {rectInfo}");

            for (int i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                AppendTransform(child, depth + 1, sb, ref count);

                if (count >= 400)
                {
                    sb.AppendLine($"{indent}... truncated ...");
                    return;
                }
            }
        }

        private static string GetTypeName(object obj)
        {
            try
            {
                if (obj == null) return "null";
                return obj.GetType().Name;
            }
            catch
            {
                return "object";
            }
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