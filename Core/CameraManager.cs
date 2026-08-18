using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.EventSystems;
using Valve.VR;
using Mathf = UnityEngine.Mathf;

namespace MiSideVR.Core
{
    public class CameraManager : MonoBehaviour
    {
        public CameraManager(IntPtr ptr) : base(ptr) { }

        public static CameraManager Instance { get; private set; }

        private enum CinematicType
        {
            None,
            Soft,
            Strong
        }

        private Camera _mainCamera;
        private Camera _lastPostProcessCamera;
        private PlayerMove _playerMove;

        private Camera _leftCamera;
        private Camera _rightCamera;

        private RenderTexture _leftRT;
        private RenderTexture _rightRT;

        private Transform _head;

        private bool _initialized = false;
        private bool _postProcessAttempted = false;
        private bool _eventSystemSetup = false;

        private int _lastCameraInstanceId = 0;
        private int _frameCount = 0;
        private int _lastCameraScanFrame = -100;
        private int _lastPeriodicLogFrame = -1000;

        private int _lastLoggedCamId = 0;
        private bool _lastLoggedPassive = false;

        private bool _wasPassive = false;

        private bool _cinematicActive = false;
        private Vector3 _cinematicLastMainPos = Vector3.zero;
        private Quaternion _cinematicLastMainRot = Quaternion.identity;

        private string _lastSceneName = "";
        private int _menuSpawnCamId = 0;

        private const float IPD = 0.064f;
        private const float ClipStart = 0.015f;
        private const float ClipEnd = 240000f;

        private readonly TrackedDevicePose_t[] _renderPoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        private readonly TrackedDevicePose_t[] _gamePoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];

        private static readonly HashSet<string> ExcludedScenes = new HashSet<string>
        {
            "SceneAihasto",
            "SceneLoading",
            "MinigameShooter"
        };

        internal static bool IsValid(UnityEngine.Object obj)
        {
            if (System.Object.ReferenceEquals(obj, null)) return false;
            try { return obj.GetInstanceID() != 0; }
            catch { return false; }
        }

        private void Awake()
        {
            Instance = this;
        }

        private void LateUpdate()
        {
            try
            {
                _frameCount++;

                if (!IsValid(VRManager.Instance) || !IsValid(VRManager.Instance.VRRig)) return;

                FindReferences();

                if (IsValid(_mainCamera) && _mainCamera.gameObject.activeInHierarchy)
                {
                    int currentId = _mainCamera.GetInstanceID();
                    if (_initialized && currentId != _lastCameraInstanceId) ResetInit();

                    if (!_initialized) Initialize();
                    if (_initialized) HandleFrame();
                }
                else if (_initialized)
                {
                    ResetInit();
                }
            }
            catch { }
        }

        private void FindReferences()
        {
            bool needScan =
                !IsValid(_mainCamera) ||
                !_mainCamera.gameObject.activeInHierarchy ||
                (_frameCount - _lastCameraScanFrame >= 10);

            if (needScan)
            {
                _lastCameraScanFrame = _frameCount;

                Camera main = Camera.main;
                Camera desired = (IsValid(main) && main.gameObject.activeInHierarchy && !IsVRCamera(main)) ? main : null;

                Camera cinematic = FindBestCinematicCamera(desired);
                if (IsValid(cinematic))
                    desired = cinematic;

                if (!IsValid(desired))
                    desired = FindBestActiveCamera();

                if (IsValid(desired) && desired != _mainCamera)
                    _mainCamera = desired;
            }

            if (!IsValid(_mainCamera))
            {
                var obj = GameObject.Find("MainCamera");
                if (IsValid(obj)) _mainCamera = obj.GetComponent<Camera>();
            }

            if (!IsValid(_playerMove))
            {
                var obj = GameObject.Find("Player");
                if (IsValid(obj)) _playerMove = obj.GetComponent<PlayerMove>();
            }

            if (IsValid(VRManager.Instance) && !VRManager.Instance.HeightCalibrated && IsValid(_playerMove) && IsValid(_playerMove.head))
                VRManager.Instance.CalibrateHeight(_playerMove.head.localPosition.y);
        }

        private Camera FindBestActiveCamera()
        {
            Camera cam = ScanCameras(false);
            if (!IsValid(cam)) cam = ScanCameras(true);
            if (!IsValid(cam)) cam = Camera.main;

            if (!IsValid(cam))
            {
                var obj = GameObject.Find("MainCamera");
                if (IsValid(obj)) cam = obj.GetComponent<Camera>();
            }

            return cam;
        }

        private Camera ScanCameras(bool includeRenderTextureCameras)
        {
            Camera best = null;

            try
            {
                var cams = UnityEngine.Object.FindObjectsOfType<Camera>();

                foreach (var cam in cams)
                {
                    if (!IsValid(cam)) continue;
                    if (!cam.enabled) continue;
                    if (!cam.gameObject.activeInHierarchy) continue;
                    if (IsVRCamera(cam)) continue;
                    if (cam.cullingMask == 0) continue;

                    bool hasRT = IsValid(cam.targetTexture);
                    if (!includeRenderTextureCameras && hasRT) continue;

                    if (best == null)
                    {
                        best = cam;
                        continue;
                    }

                    if (cam.depth > best.depth)
                    {
                        best = cam;
                    }
                    else if (cam.depth == best.depth)
                    {
                        if (IsMainCameraTag(cam) && !IsMainCameraTag(best))
                            best = cam;
                    }
                }
            }
            catch { }

            return best;
        }

        private Camera FindBestCinematicCamera(Camera currentMain)
        {
            Camera cam = ScanCinematicCameras(currentMain, false);
            if (!IsValid(cam)) cam = ScanCinematicCameras(currentMain, true);
            return cam;
        }

        private Camera ScanCinematicCameras(Camera currentMain, bool includeRenderTextureCameras)
        {
            Camera best = null;
            int bestScore = int.MinValue;
            float bestDepth = 0f;

            try
            {
                var cams = UnityEngine.Object.FindObjectsOfType<Camera>();

                foreach (var cam in cams)
                {
                    if (!IsValid(cam)) continue;
                    if (!cam.enabled) continue;
                    if (!cam.gameObject.activeInHierarchy) continue;
                    if (IsVRCamera(cam)) continue;
                    if (cam.cullingMask == 0) continue;

                    bool hasRT = IsValid(cam.targetTexture);
                    if (!includeRenderTextureCameras && hasRT) continue;

                    if (IsValid(currentMain) && cam != currentMain && IsChildOf(cam.transform, currentMain.transform))
                        continue;

                    CinematicType ctype = GetCinematicType(cam);
                    if (ctype == CinematicType.None) continue;

                    if (IsOverlayCameraName(cam.name)) continue;

                    bool mainLike = ContainsInvariant(cam.name, "MainCamera") || IsMainCameraTag(cam);

                    int score = 0;
                    if (ctype == CinematicType.Strong) score += 1000;
                    else if (ctype == CinematicType.Soft) score += 500;

                    if (mainLike) score += 100;

                    if (best == null || score > bestScore || (score == bestScore && cam.depth < bestDepth))
                    {
                        best = cam;
                        bestScore = score;
                        bestDepth = cam.depth;
                    }
                }
            }
            catch { }

            return best;
        }

        private static bool IsOverlayCameraName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            string n = name.ToLowerInvariant();

            return n.Contains("persons")
                || n.Contains("overlay")
                || n.Contains("ui")
                || n.Contains("effect")
                || n.Contains("post")
                || n.Contains("shadow")
                || n.Contains("reflection");
        }

        private static bool IsChildOf(Transform t, Transform parent)
        {
            if (!IsValid(t) || !IsValid(parent)) return false;

            var p = t;
            int guard = 0;

            while (IsValid(p) && guard++ < 64)
            {
                if (p == parent) return true;
                p = p.parent;
            }

            return false;
        }

        private static bool IsMainCameraTag(Camera cam)
        {
            try { return IsValid(cam) && cam.CompareTag("MainCamera"); }
            catch { return false; }
        }

        private static bool IsVRCamera(Camera cam)
        {
            if (!IsValid(cam)) return false;

            string path = GetTransformPath(cam.transform);

            return ContainsInvariant(path, "VR_LeftEye")
                || ContainsInvariant(path, "VR_RightEye")
                || ContainsInvariant(path, "EventCamera")
                || ContainsInvariant(path, "MiSideVR");
        }

        private void ResetInit()
        {
            _initialized = false;
            _postProcessAttempted = false;
            _eventSystemSetup = false;
            _lastCameraInstanceId = 0;
            _lastLoggedCamId = 0;
            _lastLoggedPassive = false;

            _wasPassive = false;
            _cinematicActive = false;
            _cinematicLastMainPos = Vector3.zero;
            _cinematicLastMainRot = Quaternion.identity;

            _lastSceneName = "";
            _menuSpawnCamId = 0;
        }

        private void Initialize()
        {
            if (OpenVR.System == null || !IsValid(_mainCamera)) return;

            _head = VRManager.Instance.HMD.transform;
            if (_head == null) return;

            uint w = 0, h = 0;
            OpenVR.System.GetRecommendedRenderTargetSize(ref w, ref h);
            if (w == 0) w = 2208;
            if (h == 0) h = 2452;

            if (IsValid(_leftRT) && _leftRT.useMipMap)
            {
                if (IsValid(_leftCamera)) _leftCamera.targetTexture = null;
                _leftRT.Release();
                UnityEngine.Object.Destroy(_leftRT);
                _leftRT = null;
            }

            if (IsValid(_rightRT) && _rightRT.useMipMap)
            {
                if (IsValid(_rightCamera)) _rightCamera.targetTexture = null;
                _rightRT.Release();
                UnityEngine.Object.Destroy(_rightRT);
                _rightRT = null;
            }

            if (!IsValid(_leftRT))
                _leftRT = CreateEyeRT((int)w, (int)h);

            if (!IsValid(_rightRT))
                _rightRT = CreateEyeRT((int)w, (int)h);

            if (!IsValid(_leftCamera))
            {
                var leftObj = new GameObject("VR_LeftEye");
                leftObj.transform.parent = _head;
                leftObj.transform.localPosition = new Vector3(-IPD * 0.5f, 0, 0);
                leftObj.transform.localRotation = Quaternion.identity;

                _leftCamera = leftObj.AddComponent<Camera>();
                _leftCamera.enabled = true;
                _leftCamera.targetTexture = _leftRT;
                _leftCamera.stereoTargetEye = StereoTargetEyeMask.None;
            }
            else if (_leftCamera.targetTexture != _leftRT)
            {
                _leftCamera.targetTexture = _leftRT;
            }

            if (!IsValid(_rightCamera))
            {
                var rightObj = new GameObject("VR_RightEye");
                rightObj.transform.parent = _head;
                rightObj.transform.localPosition = new Vector3(IPD * 0.5f, 0, 0);
                rightObj.transform.localRotation = Quaternion.identity;

                _rightCamera = rightObj.AddComponent<Camera>();
                _rightCamera.enabled = true;
                _rightCamera.targetTexture = _rightRT;
                _rightCamera.stereoTargetEye = StereoTargetEyeMask.None;
            }
            else if (_rightCamera.targetTexture != _rightRT)
            {
                _rightCamera.targetTexture = _rightRT;
            }

            UpdateProjectionMatrices();

            _lastCameraInstanceId = _mainCamera.GetInstanceID();
            _initialized = true;
        }

        private static RenderTexture CreateEyeRT(int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 2;
            rt.useMipMap = false;
            rt.autoGenerateMips = false;
            rt.Create();
            return rt;
        }

        private void UpdateProjectionMatrices()
        {
            if (OpenVR.System == null) return;
            if (!IsValid(_leftCamera) || !IsValid(_rightCamera)) return;

            var leftProj = OpenVR.System.GetProjectionMatrix(EVREye.Eye_Left, ClipStart, ClipEnd);
            var rightProj = OpenVR.System.GetProjectionMatrix(EVREye.Eye_Right, ClipStart, ClipEnd);

            _leftCamera.projectionMatrix = ConvertMatrix(leftProj);
            _rightCamera.projectionMatrix = ConvertMatrix(rightProj);
        }

        private void SyncCameraSettings(Camera source, Camera target)
        {
            if (!IsValid(source) || !IsValid(target)) return;

            target.clearFlags = source.clearFlags;
            target.backgroundColor = source.backgroundColor;

            target.nearClipPlane = ClipStart;
            target.farClipPlane = ClipEnd;

            target.cullingMask = source.cullingMask;
            target.depth = source.depth;
            target.renderingPath = source.renderingPath;

            target.allowHDR = source.allowHDR;
            target.allowMSAA = source.allowMSAA;
            target.depthTextureMode = source.depthTextureMode;
            target.useOcclusionCulling = source.useOcclusionCulling;

            target.orthographic = source.orthographic;
            target.orthographicSize = source.orthographicSize;
            target.fieldOfView = source.fieldOfView;

            int mirrorLayer = LayerMask.NameToLayer("ForMirror");
            int playerLayer = LayerMask.NameToLayer("Player");
            int uiLayer = LayerMask.NameToLayer("UI");

            if (mirrorLayer != -1) target.cullingMask &= ~(1 << mirrorLayer);
            if (playerLayer != -1) target.cullingMask |= (1 << playerLayer);
            if (uiLayer != -1) target.cullingMask |= (1 << uiLayer);
        }

        private void EnsureUsableClearFlags(Camera target)
        {
            if (!IsValid(target)) return;

            if (target.clearFlags == CameraClearFlags.Depth || target.clearFlags == CameraClearFlags.Nothing)
            {
                target.clearFlags = CameraClearFlags.SolidColor;

                Color bg = target.backgroundColor;
                if (bg.a < 0.01f)
                    target.backgroundColor = Color.black;
            }
        }

        private void TryCopyPostProcessing()
        {
            if (!IsValid(_mainCamera)) return;

            if (_postProcessAttempted && _lastPostProcessCamera == _mainCamera)
                return;

            _postProcessAttempted = true;
            _lastPostProcessCamera = _mainCamera;

            try
            {
                var sourcePP = _mainCamera.GetComponent<PostProcessLayer>();

                if (!IsValid(sourcePP))
                {
                    DisableEyePostProcessing();
                    return;
                }

                foreach (var cam in new[] { _leftCamera, _rightCamera })
                {
                    if (!IsValid(cam)) continue;

                    var targetPP = cam.GetComponent<PostProcessLayer>();
                    if (!IsValid(targetPP))
                        targetPP = cam.gameObject.AddComponent<PostProcessLayer>();

                    targetPP.enabled = false;
                    CopyPostProcess(sourcePP, targetPP);
                    targetPP.volumeTrigger = cam.transform;
                    targetPP.enabled = sourcePP.enabled;
                }
            }
            catch { }
        }

        private void DisableEyePostProcessing()
        {
            foreach (var cam in new[] { _leftCamera, _rightCamera })
            {
                if (!IsValid(cam)) continue;

                var pp = cam.GetComponent<PostProcessLayer>();
                if (IsValid(pp) && pp.enabled)
                    pp.enabled = false;
            }
        }

        private static void CopyPostProcess(PostProcessLayer src, PostProcessLayer dst)
        {
            if (!IsValid(src) || !IsValid(dst)) return;

            dst.volumeLayer = src.volumeLayer;
            dst.antialiasingMode = src.antialiasingMode;
            dst.stopNaNPropagation = src.stopNaNPropagation;
            dst.finalBlitToCameraTarget = src.finalBlitToCameraTarget;

            CopyMember(src, dst, "profile");
            CopyMember(src, dst, "m_Resources");
            CopyMember(src, dst, "m_DefaultProfile");
            CopyMember(src, dst, "m_ShowToolkit");
            CopyMember(src, dst, "m_ShowCustomSorter");
            CopyMember(src, dst, "breakBeforeColorGrading");

            object res = GetMember(src, "m_Resources");
            if (res != null)
                TryInvoke(dst, "Init", res);
        }

        private static void CopyMember(object src, object dst, string name)
        {
            try
            {
                if (src == null || dst == null || string.IsNullOrEmpty(name)) return;

                Type type = src.GetType();
                while (type != null)
                {
                    var f = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null)
                    {
                        f.SetValue(dst, f.GetValue(src));
                        return;
                    }

                    var p = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null && p.CanRead && p.CanWrite)
                    {
                        p.SetValue(dst, p.GetValue(src));
                        return;
                    }

                    type = type.BaseType;
                }
            }
            catch { }
        }

        private static object GetMember(object target, string name)
        {
            try
            {
                if (target == null || string.IsNullOrEmpty(name)) return null;

                Type type = target.GetType();
                while (type != null)
                {
                    var f = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null) return f.GetValue(target);

                    var p = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null && p.CanRead) return p.GetValue(target);

                    type = type.BaseType;
                }
            }
            catch { }

            return null;
        }

        private static void TryInvoke(object target, string methodName, object arg)
        {
            try
            {
                if (target == null || arg == null || string.IsNullOrEmpty(methodName)) return;

                Type argType = arg.GetType();
                Type type = target.GetType();

                while (type != null)
                {
                    foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        if (m.Name != methodName) continue;

                        var ps = m.GetParameters();
                        if (ps.Length != 1) continue;

                        if (ps[0].ParameterType.IsAssignableFrom(argType))
                        {
                            m.Invoke(target, new object[] { arg });
                            return;
                        }
                    }

                    type = type.BaseType;
                }
            }
            catch { }
        }

        private void SetupEventSystem()
        {
            try
            {
                var es = EventSystem.current;
                if (!IsValid(es)) return;

                var inputModule = es.GetComponent<StandaloneInputModule>();
                if (!IsValid(inputModule)) return;

                var vrPointer = es.gameObject.GetComponent<VRPointerInput>();
                if (!IsValid(vrPointer))
                    vrPointer = es.gameObject.AddComponent<VRPointerInput>();

                if (IsValid(VRManager.Instance) && IsValid(VRManager.Instance.RightController))
                {
                    var visuals = VRManager.Instance.RightController.GetComponent<ControllerVisuals>();
                    if (IsValid(visuals) && IsValid(visuals.EventCamera))
                    {
                        vrPointer.eventCamera = visuals.EventCamera;
                        inputModule.inputOverride = vrPointer;
                        _eventSystemSetup = true;
                    }
                }
            }
            catch { }
        }

        private void HandleFrame()
        {
            if (!IsValid(_mainCamera) || OpenVR.Compositor == null || _head == null) return;

            TryCopyPostProcessing();
            SetupEventSystem();

            try
            {
                if (!IsValid(_leftRT) || !IsValid(_rightRT)) return;

                IntPtr leftPtr = _leftRT.GetNativeTexturePtr();
                IntPtr rightPtr = _rightRT.GetNativeTexturePtr();

                if (leftPtr != IntPtr.Zero && rightPtr != IntPtr.Zero)
                {
                    ETextureType texType = ETextureType.DirectX;
                    try
                    {
                        if (SteamVR.instance != null)
                            texType = SteamVR.instance.textureType;
                    }
                    catch { }

                    var leftTex = new Texture_t
                    {
                        handle = leftPtr,
                        eType = texType,
                        eColorSpace = EColorSpace.Auto
                    };

                    var rightTex = new Texture_t
                    {
                        handle = rightPtr,
                        eType = texType,
                        eColorSpace = EColorSpace.Auto
                    };

                    var bounds = new VRTextureBounds_t
                    {
                        uMin = 0,
                        vMin = 1,
                        uMax = 1,
                        vMax = 0
                    };

                    OpenVR.Compositor.Submit(EVREye.Eye_Left, ref leftTex, ref bounds, EVRSubmitFlags.Submit_Default);
                    OpenVR.Compositor.Submit(EVREye.Eye_Right, ref rightTex, ref bounds, EVRSubmitFlags.Submit_Default);
                }
            }
            catch { }

            OpenVR.Compositor.WaitGetPoses(_renderPoses, _gamePoses);

            var hmdPose = _renderPoses[OpenVR.k_unTrackedDeviceIndex_Hmd];
            if (hmdPose.bPoseIsValid)
                ApplyPoseToTransform(_head, hmdPose.mDeviceToAbsoluteTracking);

            uint leftIndex = OpenVR.System.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.LeftHand);
            if (leftIndex != OpenVR.k_unTrackedDeviceIndexInvalid && IsValid(VRManager.Instance) && IsValid(VRManager.Instance.LeftController))
            {
                var cPose = _renderPoses[leftIndex];
                if (cPose.bPoseIsValid)
                    ApplyPoseToTransform(VRManager.Instance.LeftController.transform, cPose.mDeviceToAbsoluteTracking);
            }

            uint rightIndex = OpenVR.System.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand);
            if (rightIndex != OpenVR.k_unTrackedDeviceIndexInvalid && IsValid(VRManager.Instance) && IsValid(VRManager.Instance.RightController))
            {
                var cPose = _renderPoses[rightIndex];
                if (cPose.bPoseIsValid)
                    ApplyPoseToTransform(VRManager.Instance.RightController.transform, cPose.mDeviceToAbsoluteTracking);
            }

            SyncCameraSettings(_mainCamera, _leftCamera);
            SyncCameraSettings(_mainCamera, _rightCamera);

            EnsureUsableClearFlags(_leftCamera);
            EnsureUsableClearFlags(_rightCamera);

            UpdateProjectionMatrices();

            string sceneName = "";
            try { sceneName = _mainCamera.gameObject.scene.name; }
            catch { return; }

            if (sceneName != _lastSceneName)
            {
                _lastSceneName = sceneName;
                _menuSpawnCamId = 0;
            }

            bool isExcluded = ExcludedScenes.Contains(sceneName);
            CinematicType cinematicType = GetCinematicType(_mainCamera);

            bool passive = isExcluded || cinematicType != CinematicType.None;
            bool strongCinematic = cinematicType == CinematicType.Strong;

            bool hideControllers =
                (sceneName == "SceneAihasto" || sceneName == "SceneLoading") ||
                strongCinematic;

            if (IsValid(VRManager.Instance.LeftController))
                VRManager.Instance.LeftController.SetActive(!hideControllers);

            if (IsValid(VRManager.Instance.RightController))
                VRManager.Instance.RightController.SetActive(!hideControllers);

            bool forceLog = Input.GetKeyDown(KeyCode.F6) || (_frameCount - _lastPeriodicLogFrame >= 450);
            if (forceLog)
                _lastPeriodicLogFrame = _frameCount;

            LogCameraState(passive, isExcluded, cinematicType, forceLog);

            bool isMenu = sceneName == "SceneMenu";

            if (!passive)
            {
                if (_cinematicActive)
                    _cinematicActive = false;

                if (IsValid(VRManager.Instance) && IsValid(VRManager.Instance.VRRig))
                {
                    Transform rig = VRManager.Instance.VRRig.transform;

                    if (IsValid(_playerMove) && IsValid(_playerMove.gameObject) && _playerMove.gameObject.activeInHierarchy)
                    {
                        Transform playerRoot = _playerMove.transform;

                        CapsuleCollider capsule = _playerMove.GetComponent<CapsuleCollider>();
                        Vector3 feetPos = playerRoot.position;

                        if (IsValid(capsule))
                            feetPos.y += capsule.center.y - (capsule.height / 2f);

                        rig.position = feetPos;
                        rig.rotation = Quaternion.Euler(0f, playerRoot.eulerAngles.y, 0f);
                    }
                    else if (isMenu)
                    {
                        ApplyMenuSpawn();
                    }
                    else
                    {
                        if (_wasPassive)
                        {
                            rig.position = Vector3.zero;
                            rig.rotation = Quaternion.identity;
                        }
                        else
                        {
                            rig.rotation = Quaternion.Euler(0f, rig.eulerAngles.y, 0f);
                        }
                    }
                }

                // IMPORTANT MENU CHANGE:
                // Do NOT drive the game's menu camera with the headset.
                // This prevents menu lighting/background rigs from being dragged around by the HMD.
                if (!isMenu)
                {
                    _mainCamera.transform.position = _head.position;

                    if (sceneName == "SceneMenu")
                    {
                        _mainCamera.transform.rotation = _head.rotation;
                    }
                    else
                    {
                        var rightVisuals = (IsValid(VRManager.Instance) && IsValid(VRManager.Instance.RightController) && VRManager.Instance.RightController.activeInHierarchy)
                            ? VRManager.Instance.RightController.GetComponent<ControllerVisuals>()
                            : null;

                        if (IsValid(rightVisuals))
                        {
                            Ray laserRay = rightVisuals.GetAimRay();

                            if (Physics.Raycast(laserRay, out RaycastHit hit, 50f))
                                _mainCamera.transform.LookAt(hit.point);
                            else
                                _mainCamera.transform.rotation = Quaternion.LookRotation(laserRay.direction);
                        }
                        else
                        {
                            _mainCamera.transform.rotation = _head.rotation;
                        }
                    }
                }
            }
            else
            {
                if (IsValid(VRManager.Instance) && IsValid(VRManager.Instance.VRRig))
                {
                    if (!_cinematicActive)
                        EnterCinematicFollow();
                    else
                        UpdateCinematicFollow();
                }
                else
                {
                    _head.position = _mainCamera.transform.position;
                    _head.rotation = _mainCamera.transform.rotation;
                }
            }

            _wasPassive = passive;
        }

        private void ApplyMenuSpawn()
        {
            if (_lastSceneName != "SceneMenu")
            {
                _menuSpawnCamId = 0;
                return;
            }

            if (!IsValid(_mainCamera) || !IsValid(VRManager.Instance) || !IsValid(VRManager.Instance.VRRig) || _head == null)
                return;

            Transform rig = VRManager.Instance.VRRig.transform;
            int id = _mainCamera.GetInstanceID();

            if (_menuSpawnCamId != id)
            {
                _menuSpawnCamId = id;

                Vector3 spawnPos = _mainCamera.transform.position;
                Quaternion spawnRot = _mainCamera.transform.rotation;

                AlignRigToPose(spawnPos, spawnRot);

                if (MiSideVRCore.Log != null)
                {
                    MiSideVRCore.Log.LogInfo(
                        $"[VR] Menu spawn: pos={spawnPos} yaw={spawnRot.eulerAngles.y:F2}"
                    );
                }
            }
            else
            {
                rig.rotation = Quaternion.Euler(0f, rig.eulerAngles.y, 0f);
            }
        }

        private void AlignRigToPose(Vector3 pos, Quaternion rot)
        {
            if (!IsValid(VRManager.Instance) || !IsValid(VRManager.Instance.VRRig) || _head == null)
                return;

            Transform rig = VRManager.Instance.VRRig.transform;

            Vector3 headLocalPos = _head.localPosition;
            Quaternion headLocalRot = _head.localRotation;

            float targetYaw = rot.eulerAngles.y;
            float headLocalYaw = headLocalRot.eulerAngles.y;

            rig.rotation = Quaternion.Euler(0f, targetYaw - headLocalYaw, 0f);
            rig.position = pos - (rig.rotation * headLocalPos);
        }

        private void EnterCinematicFollow()
        {
            if (!IsValid(VRManager.Instance) || !IsValid(VRManager.Instance.VRRig) || _head == null || !IsValid(_mainCamera))
                return;

            Transform rig = VRManager.Instance.VRRig.transform;

            Vector3 headLocalPos = _head.localPosition;
            Quaternion headLocalRot = _head.localRotation;

            float mainYaw = _mainCamera.transform.rotation.eulerAngles.y;
            float headLocalYaw = headLocalRot.eulerAngles.y;

            rig.rotation = Quaternion.Euler(0f, mainYaw - headLocalYaw, 0f);
            rig.position = _mainCamera.transform.position - (rig.rotation * headLocalPos);

            _cinematicLastMainPos = _mainCamera.transform.position;
            _cinematicLastMainRot = _mainCamera.transform.rotation;
            _cinematicActive = true;
        }

        private void UpdateCinematicFollow()
        {
            if (!IsValid(VRManager.Instance) || !IsValid(VRManager.Instance.VRRig) || _head == null || !IsValid(_mainCamera))
                return;

            Transform rig = VRManager.Instance.VRRig.transform;

            Vector3 mainPos = _mainCamera.transform.position;
            Quaternion mainRot = _mainCamera.transform.rotation;

            Vector3 deltaPos = mainPos - _cinematicLastMainPos;
            float deltaYaw = Mathf.DeltaAngle(_cinematicLastMainRot.eulerAngles.y, mainRot.eulerAngles.y);

            float absYaw = Mathf.Abs(deltaYaw);

            if (deltaPos.sqrMagnitude > 25f || absYaw > 60f)
            {
                EnterCinematicFollow();
                return;
            }

            if (absYaw > 0.0001f)
                rig.RotateAround(_head.position, Vector3.up, deltaYaw);

            rig.position += deltaPos;

            _cinematicLastMainPos = mainPos;
            _cinematicLastMainRot = mainRot;
        }

        private void LogCameraState(bool passive, bool excluded, CinematicType cinematicType, bool force)
        {
            try
            {
                int id = _mainCamera.GetInstanceID();

                if (force || id != _lastLoggedCamId || passive != _lastLoggedPassive)
                {
                    if (MiSideVRCore.Log != null)
                    {
                        MiSideVRCore.Log.LogInfo(
                            $"[VR] MainCamera={GetTransformPath(_mainCamera.transform)} " +
                            $"id={id} depth={_mainCamera.depth} active={_mainCamera.gameObject.activeInHierarchy} " +
                            $"passive={passive} excluded={excluded} cinematic={cinematicType} " +
                            $"playerMoveUsable={IsPlayerMoveUsable()}"
                        );

                        LogActiveCameraSummary();
                    }

                    _lastLoggedCamId = id;
                    _lastLoggedPassive = passive;
                }
            }
            catch { }
        }

        private void LogActiveCameraSummary()
        {
            try
            {
                var cams = UnityEngine.Object.FindObjectsOfType<Camera>();
                var sb = new StringBuilder();
                sb.AppendLine("[VR] Active camera summary:");

                int count = 0;
                foreach (var cam in cams)
                {
                    if (!IsValid(cam)) continue;
                    if (!cam.enabled) continue;
                    if (!cam.gameObject.activeInHierarchy) continue;
                    if (IsVRCamera(cam)) continue;

                    CinematicType ctype = GetCinematicType(cam);
                    string tag = ctype == CinematicType.Strong ? "[S] " : ctype == CinematicType.Soft ? "[s] " : "";

                    sb.AppendLine(
                        $"  {tag}{GetTransformPath(cam.transform)} " +
                        $"depth={cam.depth} " +
                        $"rt={IsValid(cam.targetTexture)} " +
                        $"mask=0x{cam.cullingMask:X8}"
                    );

                    count++;
                    if (count >= 16) break;
                }

                if (MiSideVRCore.Log != null)
                    MiSideVRCore.Log.LogInfo(sb.ToString());
            }
            catch { }
        }

        private bool IsPlayerMoveUsable()
        {
            try
            {
                if (!IsValid(_playerMove)) return false;
                if (!IsValid(_playerMove.gameObject) || !_playerMove.gameObject.activeInHierarchy) return false;

                object enabledObj = GetMember(_playerMove, "enabled");
                if (enabledObj is bool enabledBool && !enabledBool)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static CinematicType GetCinematicType(Camera cam)
        {
            if (!IsValid(cam)) return CinematicType.None;

            string path = GetTransformPath(cam.transform);

            if (ContainsInvariant(path, "CutScenes")
                || ContainsInvariant(path, "CutScene")
                || ContainsInvariant(path, "Cinema")
                || ContainsInvariant(path, "Timeline"))
            {
                return CinematicType.Strong;
            }

            if (ContainsInvariant(path, "FixHead"))
            {
                return CinematicType.Soft;
            }

            return CinematicType.None;
        }

        private static string GetTransformPath(Transform t)
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

        private static bool ContainsInvariant(string source, string value)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value)) return false;
            return source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ApplyPoseToTransform(Transform t, HmdMatrix34_t m)
        {
            if (t == null) return;

            float scale = (IsValid(VRManager.Instance) && VRManager.Instance.HeightCalibrated) ? VRManager.Instance.TrackingScale : 1f;

            t.localPosition = new Vector3(m.m3, m.m7 * scale, -m.m11);

            float w = Mathf.Sqrt(Mathf.Max(0, 1 + m.m0 + m.m5 + m.m10)) * 0.5f;
            float x = Mathf.Sqrt(Mathf.Max(0, 1 + m.m0 - m.m5 - m.m10)) * 0.5f;
            float y = Mathf.Sqrt(Mathf.Max(0, 1 - m.m0 + m.m5 - m.m10)) * 0.5f;
            float z = Mathf.Sqrt(Mathf.Max(0, 1 - m.m0 - m.m5 + m.m10)) * 0.5f;

            if (m.m9 - m.m6 < 0) x = -x;
            if (m.m2 - m.m8 < 0) y = -y;
            if (m.m4 - m.m1 < 0) z = -z;

            t.localRotation = new Quaternion(-x, -y, z, w);
        }

        private Matrix4x4 ConvertMatrix(HmdMatrix44_t mat)
        {
            Matrix4x4 m = new Matrix4x4();

            m.m00 = mat.m0; m.m01 = mat.m1; m.m02 = mat.m2; m.m03 = mat.m3;
            m.m10 = mat.m4; m.m11 = mat.m5; m.m12 = mat.m6; m.m13 = mat.m7;
            m.m20 = mat.m8; m.m21 = mat.m9; m.m22 = mat.m10; m.m23 = mat.m11;
            m.m30 = mat.m12; m.m31 = mat.m13; m.m32 = mat.m14; m.m33 = mat.m15;

            return m;
        }
    }
}