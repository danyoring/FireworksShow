using UnityEngine;
using UnityEngine.UI;

// 강물에 비친 불꽃 (가짜 반사)
// 실제 반사는 불꽃 높이만큼 발밑에 생겨서 화면에 안 잡히기 때문에,
// 불꽃만 따로 찍은 화면을 위아래로 뒤집고 납작하게 눌러 수평선 아래 강물 위에 겹쳐 그려요.
// - 불꽃은 FireworksController가 TransparentFX 레이어로 바꿔서 생성해요.
// - 앞쪽 난간·전봇대 같은 가까운 물체는 반사 위에 그대로 보여요. (canvasDistance보다 가까운 물체)
[RequireComponent(typeof(Camera))]
public class FireworksReflection : MonoBehaviour
{
    public const int FIREWORKS_LAYER = 1; // Unity 기본 "TransparentFX" 레이어

    public Material reflectionMaterial;

    [Tooltip("화면에서 수평선 위 몇 %까지를 물에 비출지")]
    [SerializeField, Range(0.1f, 1f)] float sourceHeight = 0.6f;
    [Tooltip("반사 밝기")]
    [SerializeField, Range(0f, 2f)] float intensity = 0.55f;
    [Tooltip("이 거리보다 가까운 물체는 반사 앞에 그려져요")]
    [SerializeField] float canvasDistance = 40f;
    [SerializeField, Range(0.25f, 1f)] float resolutionScale = 0.5f;

    Camera _main, _reflCam;
    RenderTexture _rt;
    RawImage _image;
    RectTransform _rect;
    Material _mat;
    GameObject _canvasGo;

    void Start()
    {
        _main = GetComponent<Camera>();
        if (reflectionMaterial == null)
        {
            Debug.LogWarning("[FireworksReflection] 반사 재질이 비어 있어요.");
            enabled = false;
            return;
        }
        _mat = new Material(reflectionMaterial);
        _main.cullingMask |= 1 << FIREWORKS_LAYER;

        CreateRenderTexture();

        GameObject camGo = new GameObject("FireworksReflectionCamera");
        _reflCam = camGo.AddComponent<Camera>();
        _reflCam.CopyFrom(_main);
        _reflCam.cullingMask = 1 << FIREWORKS_LAYER;
        _reflCam.clearFlags = CameraClearFlags.SolidColor;
        _reflCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        _reflCam.targetTexture = _rt;
        _reflCam.depth = _main.depth - 1;

        _canvasGo = new GameObject("FireworksReflectionCanvas");
        Canvas canvas = _canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = _main;
        canvas.planeDistance = canvasDistance;
        canvas.sortingOrder = -100;

        GameObject imgGo = new GameObject("Reflection");
        imgGo.transform.SetParent(_canvasGo.transform, false);
        _image = imgGo.AddComponent<RawImage>();
        _image.texture = _rt;
        _image.material = _mat;
        _image.raycastTarget = false;
        _rect = _image.rectTransform;
        _rect.offsetMin = _rect.offsetMax = Vector2.zero;
    }

    void CreateRenderTexture()
    {
        int w = Mathf.Max(64, (int)(Screen.width * resolutionScale));
        int h = Mathf.Max(64, (int)(Screen.height * resolutionScale));
        RenderTextureFormat fmt = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
            ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.Default;
        if (_rt != null) _rt.Release();
        _rt = new RenderTexture(w, h, 16, fmt) { name = "FireworksReflectionRT" };
        _rt.Create();
        if (_reflCam != null) _reflCam.targetTexture = _rt;
        if (_image != null) _image.texture = _rt;
    }

    void LateUpdate()
    {
        if (_reflCam == null) return;

        // 화면 크기가 바뀌면 렌더 텍스처도 맞춰서 다시 만듦
        int w = Mathf.Max(64, (int)(Screen.width * resolutionScale));
        int h = Mathf.Max(64, (int)(Screen.height * resolutionScale));
        if (_rt.width != w || _rt.height != h) CreateRenderTexture();

        _reflCam.transform.SetPositionAndRotation(_main.transform.position, _main.transform.rotation);
        _reflCam.fieldOfView = _main.fieldOfView;
        _reflCam.aspect = _main.aspect;

        // 수평선이 화면 아래에서 몇 % 높이에 있는지
        Vector3 flat = _main.transform.forward;
        flat.y = 0f;
        if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
        float horizon = Mathf.Clamp01(_main.WorldToViewportPoint(_main.transform.position + flat.normalized * 100000f).y);

        // 수평선 아래 영역에, 수평선 위 화면을 뒤집어서 눌러 담기
        _rect.anchorMin = Vector2.zero;
        _rect.anchorMax = new Vector2(1f, horizon);
        float src = Mathf.Min(sourceHeight, 1f - horizon);
        _image.uvRect = new Rect(0f, horizon + src, 1f, -src);

        _mat.SetFloat("_Horizon", horizon);
        _mat.SetFloat("_Range", src);
        _mat.SetFloat("_Intensity", intensity);
    }

    void OnDestroy()
    {
        if (_reflCam != null) Destroy(_reflCam.gameObject);
        if (_canvasGo != null) Destroy(_canvasGo);
        if (_rt != null) _rt.Release();
        if (_mat != null) Destroy(_mat);
    }

    public static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursively(child.gameObject, layer);
    }
}
