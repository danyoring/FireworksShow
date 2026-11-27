using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// 메뉴: Fireworks > 거리 배경 만들기
// 사진처럼 "도심 길가에서 가드레일 너머로 불꽃을 올려다보는" 배경을 기본 도형으로 조립해요.
// - 다시 누르면 기존 StreetEnvironment를 지우고 새로 만들어요. (Ctrl/Cmd+Z로 되돌리기 가능)
// - AllSky 데모 지형(Levels)은 씬에서 삭제하고, 화면 UI(UIsRoot)는 비활성화만 해요.
// - 숫자를 바꾸고 싶으면 아래 상수/좌표를 고친 뒤 메뉴를 다시 누르면 돼요.
public static class StreetSceneBuilder
{
    const string ROOT_NAME = "StreetEnvironment";
    const string MAT_DIR = "Assets/_Street/Materials";
    const string KOREAN_FONT = "Assets/TextMesh Pro/Resources/SpoqaHanSansNeo-Bold SDF.asset";

    // 관람자(카메라) 위치. 불꽃은 원점(0,0,0)에서 쏘아 올려져요.
    const float CAM_Z = -75f;       // 불꽃 발사 지점(강 위)에서 75m 떨어진 강변 둑길
    const float EYE_Y = 4f;         // 강변 전망 데크 위 눈높이
    const float WATER_Y = -9f;      // 한강 수면 (둑길보다 9m 아래)
    const float BRIDGE_X = 45f;     // 한강대교 중심선 (오른쪽)
    const float BANNER_YAW = 90f;   // 가로등 현수막 방향 (도로를 따라 옆으로)
    const float SIGNAL_YAW = 90f;   // 신호등 기둥 방향 (불빛 면이 카메라 반대쪽을 보게)
    const float CAM_PITCH = 17f;    // 위로 올려다보는 각도

    static readonly Dictionary<string, Material> _mats = new Dictionary<string, Material>();

    [MenuItem("Fireworks/거리 배경 만들기")]
    public static void Build()
    {
        _mats.Clear();

        GameObject old = FindInScene(ROOT_NAME);
        if (old != null) Undo.DestroyObjectImmediate(old);

        GameObject root = new GameObject(ROOT_NAME);
        Undo.RegisterCreatedObjectUndo(root, "Build Street Environment");
        Transform r = root.transform;

        // 강물 반사(실시간 리플렉션 프로브)는 품질 설정에서 켜져 있어야 그려져요.
        QualitySettings.realtimeReflectionProbes = true;

        HideDemoEnvironment();
        SetupCamera();
        SetupLighting();

        BuildGround(r);
        BuildGuardrail(r);
        BuildPowerLines(r);
        BuildTrafficSignal(r);
        BuildBanner(r);
        BuildSigns(r);
        BuildStreetLights(r);
        BuildTrees(r);
        BuildRiver(r);
        BuildLaunchBarge(r);
        BuildBridge(r);
        BuildNodeulIsland(r);
        BuildSkyline(r);

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[StreetSceneBuilder] 거리 배경 완성! Cmd+S로 씬을 저장하세요.");
    }

    // 메뉴: Fireworks > 추천 연출 시간표 적용
    // (05·06번처럼 발사대 높이에서 바로 터지는 불꽃은 빼고, 위로 솟아 터지는 불꽃만 사용)
    [MenuItem("Fireworks/추천 연출 시간표 적용")]
    public static void ApplyTimeline()
    {
        FireworksController ctrl = Object.FindFirstObjectByType<FireworksController>();
        if (ctrl == null) { Debug.LogWarning("FireworksController를 못 찾았어요."); return; }

        (float t, string[] fx)[] phases =
        {
            (2.0f,   new[] { "01" }),
            (20.5f,  new[] { "03", "02" }),
            (40.0f,  new[] { "05_1" }),
            (72.0f,  new[] { "08" }),
            (95.6f,  new[] { "03", "07", "04" }),
            (120.0f, new[] { "02" }),
            (164.2f, new[] { "04_1", "08" }),
            (175.5f, new[] { "01" }),
            (193.4f, new[] { "03", "04", "08" }),
            (233.0f, new[] { "07", "08" }),
            (248.0f, new[] { "01" }),
            (259.0f, new string[0]),
        };

        SerializedObject so = new SerializedObject(ctrl);
        SerializedProperty arr = so.FindProperty("_phases");
        arr.arraySize = phases.Length;
        for (int i = 0; i < phases.Length; i++)
        {
            SerializedProperty el = arr.GetArrayElementAtIndex(i);
            el.FindPropertyRelative("_fStartTime").floatValue = phases[i].t;
            SerializedProperty list = el.FindPropertyRelative("_pFireworks");
            list.arraySize = phases[i].fx.Length;
            for (int j = 0; j < phases[i].fx.Length; j++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/FX/FX_Fireworks_{phases[i].fx[j]}.prefab");
                list.GetArrayElementAtIndex(j).objectReferenceValue = prefab != null ? prefab.GetComponent<ParticleSystem>() : null;
            }
        }
        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(ctrl.gameObject.scene);
        Debug.Log("[StreetSceneBuilder] 추천 연출 시간표 적용 완료! Cmd+S로 저장하세요.");
    }

    // ───────────────────────── 씬 정리 ─────────────────────────

    static void HideDemoEnvironment()
    {
        // 기존 AllSky 산 지형·조명은 씬에서 완전히 삭제
        GameObject levels = FindInScene("Levels");
        if (levels != null) Undo.DestroyObjectImmediate(levels);

        foreach (string name in new[] { "UIsRoot", "FPSCounter" })
        {
            GameObject go = FindInScene(name);
            if (go == null || !go.activeSelf) continue;
            Undo.RecordObject(go, "Hide " + name);
            go.SetActive(false);
        }
    }

    static void SetupCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            GameObject go = FindInScene("Camera");
            if (go != null) cam = go.GetComponent<Camera>();
        }
        if (cam == null) { Debug.LogWarning("카메라를 못 찾았어요."); return; }

        Undo.RecordObject(cam.transform, "Move Camera");
        Undo.RecordObject(cam, "Camera Settings");
        cam.transform.SetPositionAndRotation(new Vector3(0f, EYE_Y, CAM_Z), Quaternion.Euler(-CAM_PITCH, 0f, 0f));
        cam.fieldOfView = 60f;
        cam.farClipPlane = 2000f;

        // 강물에 비친 불꽃(가짜 반사) 컴포넌트 연결
        Shader sh = Shader.Find("Hidden/FireworksReflection");
        if (sh == null) { Debug.LogWarning("FireworksReflection 셰이더를 못 찾았어요."); return; }
        Material reflMat = GetOrCreateMat("FireworksReflection", "Hidden/FireworksReflection");
        FireworksReflection fr = cam.GetComponent<FireworksReflection>();
        if (fr == null) fr = Undo.AddComponent<FireworksReflection>(cam.gameObject);
        Undo.RecordObject(fr, "Fireworks Reflection");
        fr.reflectionMaterial = reflMat;
        EditorUtility.SetDirty(fr);
    }

    static void SetupLighting()
    {
        foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (l.type != LightType.Directional) continue;
            Undo.RecordObject(l, "Moon Light");
            Undo.RecordObject(l.transform, "Moon Light");
            l.intensity = 0.35f;
            l.color = new Color(0.6f, 0.68f, 0.85f);
            l.shadows = LightShadows.None;
            l.transform.rotation = Quaternion.Euler(35f, -150f, 0f);
        }
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.14f, 0.15f, 0.19f);
    }

    // ───────────────────────── 지면·도로 ─────────────────────────

    static void BuildGround(Transform root)
    {
        Transform g = Group(root, "Ground");
        Material asphalt = Mat("Asphalt", new Color(0.05f, 0.05f, 0.055f), 0.35f);
        Material concrete = Mat("Sidewalk", new Color(0.2f, 0.2f, 0.2f), 0.1f);
        Material paint = Mat("RoadPaint", new Color(0.7f, 0.7f, 0.65f), 0.2f, new Color(0.15f, 0.15f, 0.13f));

        // 둑길 (카메라 뒤 10m ~ 앞 26m)
        Cube("Asphalt", g, new Vector3(0f, -0.1f, CAM_Z + 2f), new Vector3(600f, 0.2f, 24f), asphalt);
        // 강으로 떨어지는 옹벽
        Cube("Embankment", g, new Vector3(0f, WATER_Y / 2f, CAM_Z + 14.5f), new Vector3(600f, -WATER_Y, 1f), concrete);
        Cube("Sidewalk", g, new Vector3(0f, 0.06f, CAM_Z - 2f), new Vector3(80f, 0.12f, 8f), concrete);
        Cube("FarCurb", g, new Vector3(0f, 0.08f, CAM_Z + 12f), new Vector3(300f, 0.16f, 0.4f), concrete);

        for (float x = -90f; x <= 90f; x += 9f)
            Cube("Lane", g, new Vector3(x, 0.01f, CAM_Z + 6f), new Vector3(4f, 0.02f, 0.18f), paint);
    }

    static void BuildGuardrail(Transform root)
    {
        Transform g = Group(root, "Guardrail");
        Material metal = Mat("RailMetal", new Color(0.8f, 0.82f, 0.85f), 0.75f, null, 0.7f);
        float z = CAM_Z + 13.5f; // 강가 난간

        for (float x = -40f; x <= 40f; x += 2.5f)
            Pillar("Post", g, new Vector3(x, 0f, z), 1.15f, 0.045f, metal);

        foreach (float y in new[] { 1.15f, 0.8f, 0.45f })
            Bar("Rail", g, new Vector3(-41f, y, z), new Vector3(41f, y, z), 0.05f, metal);
    }

    // ───────────────────────── 전봇대·전선 ─────────────────────────

    static void BuildPowerLines(Transform root)
    {
        Transform g = Group(root, "PowerLines");
        Material pole = Mat("ConcretePole", new Color(0.38f, 0.37f, 0.35f), 0.1f);
        Material dark = Mat("DarkMetal", new Color(0.08f, 0.08f, 0.09f), 0.3f, null, 0.5f);
        Material wire = Unlit("Wire", new Color(0.02f, 0.02f, 0.025f));

        // A줄: 시야를 가로지르는 전봇대 (불꽃 바로 앞을 전선이 지나가게)
        float zA = CAM_Z + 10f;
        float[] xs = { -28f, -15f, 15f, 28f }; // 가운데(불꽃 자리)는 비워둠
        foreach (float x in xs)
        {
            Pillar("Pole", g, new Vector3(x, 0f, zA), 12.5f, 0.17f, pole);
            Cube("CrossArm", g, new Vector3(x, 11.2f, zA), new Vector3(0.12f, 0.12f, 2.6f), dark);
        }
        Pillar("Transformer", g, new Vector3(-14.55f, 8.0f, zA), 1.2f, 0.35f, dark);

        for (int i = 0; i < xs.Length - 1; i++)
        {
            float a = xs[i], b = xs[i + 1];
            float sag = (b - a) > 20f ? 1.6f : 0.5f; // 가운데 긴 구간은 더 처지게
            Wire(g, new Vector3(a, 11.35f, zA - 1.1f), new Vector3(b, 11.35f, zA - 1.1f), sag, 0.05f, wire);
            Wire(g, new Vector3(a, 11.35f, zA + 1.1f), new Vector3(b, 11.35f, zA + 1.1f), sag, 0.05f, wire);
            Wire(g, new Vector3(a, 12.05f, zA), new Vector3(b, 12.05f, zA), sag * 0.9f, 0.05f, wire);
            Wire(g, new Vector3(a, 8.0f, zA - 0.25f), new Vector3(b, 8.0f, zA - 0.25f), sag * 1.5f, 0.08f, wire);
        }

        // B줄: 오른쪽에서 불꽃 쪽으로 멀어지는 전봇대 (전선이 사선으로 보이게)
        float xB = 5f;
        float[] zs = { CAM_Z + 7f, CAM_Z + 12.5f };
        foreach (float z in zs)
        {
            Pillar("Pole", g, new Vector3(xB, 0f, z), 12f, 0.17f, pole);
            Cube("CrossArm", g, new Vector3(xB, 11.2f, z), new Vector3(2.6f, 0.12f, 0.12f), dark);
        }
        for (int i = 0; i < zs.Length - 1; i++)
        {
            float a = zs[i], b = zs[i + 1];
            Wire(g, new Vector3(xB - 1.1f, 11.35f, a), new Vector3(xB - 1.1f, 11.35f, b), 0.5f, 0.05f, wire);
            Wire(g, new Vector3(xB + 1.1f, 11.35f, a), new Vector3(xB + 1.1f, 11.35f, b), 0.5f, 0.05f, wire);
            Wire(g, new Vector3(xB, 12.05f, a), new Vector3(xB, 12.05f, b), 0.45f, 0.05f, wire);
            Wire(g, new Vector3(xB - 0.25f, 8.0f, a), new Vector3(xB - 0.25f, 8.0f, b), 0.8f, 0.08f, wire);
        }

        // 가까운 전봇대에서 A줄로 비스듬히 넘어가는 전선
        Wire(g, new Vector3(xB + 1.1f, 11.35f, zs[0]), new Vector3(15f, 11.35f, zA - 1.1f), 0.5f, 0.05f, wire);
        Wire(g, new Vector3(xB, 12.05f, zs[0]), new Vector3(15f, 12.05f, zA), 0.5f, 0.05f, wire);
    }

    static void BuildTrafficSignal(Transform root)
    {
        Transform g = Group(root, "TrafficSignal");
        Material poleMat = Mat("SignalPole", new Color(0.2f, 0.2f, 0.21f), 0.4f, null, 0.4f);
        Material housing = Mat("SignalHousing", new Color(0.02f, 0.02f, 0.02f), 0.3f);
        Material off = Mat("SignalOff", new Color(0.05f, 0.05f, 0.05f), 0.6f);
        Material red = Mat("SignalRed", new Color(0.6f, 0.05f, 0.03f), 0.6f, new Color(1f, 0.08f, 0.05f) * 3f);

        // 기둥 밑동을 원점으로 조립한 뒤, 통째로 옮기고 돌려요.
        Transform a = Group(g, "Assembly");
        Pillar("Pole", a, Vector3.zero, 7f, 0.14f, poleMat);
        Bar("Arm", a, new Vector3(0f, 6.6f, 0f), new Vector3(3.5f, 6.6f, 0f), 0.08f, poleMat);
        Cube("Housing", a, new Vector3(2.8f, 6.1f, 0f), new Vector3(1.35f, 0.45f, 0.35f), housing);

        float[] lampX = { 2.38f, 2.8f, 3.22f };
        for (int i = 0; i < lampX.Length; i++)
            Prim(PrimitiveType.Sphere, "Lamp", a, new Vector3(lampX[i], 6.1f, -0.18f),
                new Vector3(0.28f, 0.28f, 0.06f), i == 0 ? red : off);

        a.position = new Vector3(-6f, 0f, CAM_Z + 9f);
        a.rotation = Quaternion.Euler(0f, SIGNAL_YAW, 0f);
    }

    // 가로등 기둥 양쪽에 거는 명지대학교 현수막 (사진 텍스처)
    static void BuildBanner(Transform root)
    {
        Transform g = Group(root, "Banner");
        Material poleMat = Mat("BannerPole", new Color(0.18f, 0.19f, 0.2f), 0.5f, null, 0.6f);
        Material banner = BannerMat();

        // 기둥 밑동을 원점으로 조립한 뒤, 신호등처럼 통째로 옮기고 돌려요.
        Transform a = Group(g, "Assembly");
        Pillar("Pole", a, Vector3.zero, 9f, 0.09f, poleMat);
        foreach (float y in new[] { 7.75f, 4.55f })
            Bar("Bracket", a, new Vector3(-1.5f, y, 0f), new Vector3(1.5f, y, 0f), 0.03f, poleMat);
        // 큐브 앞면 UV가 뒤집혀 있어서 Z축으로 180도 돌려 바로 세움
        foreach (float side in new[] { -1f, 1f })
            Cube("MJU_Banner", a, new Vector3(side * 0.75f, 6.15f, -0.05f), new Vector3(1.3f, 3.2f, 0.02f), banner,
                new Vector3(0f, 0f, 180f));

        a.position = new Vector3(3f, 0f, CAM_Z + 11.5f);
        a.rotation = Quaternion.Euler(0f, BANNER_YAW, 0f);
    }

    static Material BannerMat() => TexMat("MJU_Banner", "Assets/_Street/Textures/MJU_Banner.png", 0.7f);

    // 사진 텍스처를 쓰는 발광 재질 (밤에도 그림이 보이게)
    static Material TexMat(string name, string texPath, float glow)
    {
        Material m = GetOrCreateMat(name, "Universal Render Pipeline/Lit");
        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Smoothness", 0.35f);
        m.SetFloat("_Metallic", 0f);
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        m.SetTexture("_EmissionMap", tex);
        m.SetColor("_EmissionColor", Color.white * glow);
        EditorUtility.SetDirty(m);
        return m;
    }

    // 가까운 전봇대에 붙은 명지대 현수막 두 장
    static void BuildSigns(Transform root)
    {
        Transform g = Group(root, "Signs");
        Material frame = Mat("SignFrame", new Color(0.12f, 0.12f, 0.13f), 0.4f, null, 0.5f);
        Material mju = BannerMat();

        float zf = CAM_Z + 7f - 0.25f; // 가까운 전봇대 앞면
        const float w = 0.8f, h = 2.0f, y = 5.6f;
        float[] xs = { 4.25f, 5.75f };
        Material[] mats = { mju, mju };
        for (int i = 0; i < 2; i++)
        {
            Cube("SignFrame", g, new Vector3(xs[i], y, zf + 0.02f), new Vector3(w + 0.12f, h + 0.12f, 0.05f), frame);
            // 큐브 앞면 UV가 뒤집혀 있어서 Z축으로 180도 돌려 바로 세움
            Cube("SignImage", g, new Vector3(xs[i], y, zf - 0.02f), new Vector3(w, h, 0.02f), mats[i],
                new Vector3(0f, 0f, 180f));
        }
        foreach (float by in new[] { y + 0.7f, y - 0.7f })
            Bar("SignBracket", g, new Vector3(xs[0], by, zf + 0.05f), new Vector3(xs[1], by, zf + 0.05f), 0.03f, frame);
    }

    static void BuildStreetLights(Transform root)
    {
        Transform g = Group(root, "StreetLights");
        Material poleMat = Mat("SignalPole", new Color(0.2f, 0.2f, 0.21f), 0.4f, null, 0.4f);
        Material lamp = Mat("LampHead", new Color(1f, 0.9f, 0.75f), 0.5f, new Color(1f, 0.85f, 0.6f) * 8f);

        // (기둥 x, z, 팔 방향)
        Vector3[] spots =
        {
            new Vector3(10f, CAM_Z + 5f, -1f),
            new Vector3(-12f, CAM_Z + 4f, 1f),
            new Vector3(-9f, CAM_Z + 12f, 1f),
            new Vector3(15f, CAM_Z + 12f, -1f),
        };
        foreach (Vector3 s in spots)
        {
            float x = s.x, z = s.y, dir = s.z;
            Pillar("Pole", g, new Vector3(x, 0f, z), 9f, 0.12f, poleMat);
            Bar("Arm", g, new Vector3(x, 8.9f, z), new Vector3(x + dir * 2.5f, 8.9f, z), 0.06f, poleMat);
            Cube("Lamp", g, new Vector3(x + dir * 2.7f, 8.8f, z), new Vector3(0.9f, 0.18f, 0.4f), lamp);

            GameObject lightGo = new GameObject("LampLight");
            lightGo.transform.SetParent(g, false);
            lightGo.transform.position = new Vector3(x + dir * 2.7f, 8.4f, z);
            Light l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 24f;
            l.intensity = 15f;
            l.color = new Color(1f, 0.82f, 0.6f);
            l.shadows = LightShadows.None;
        }
    }

    static void BuildTrees(Transform root)
    {
        Transform g = Group(root, "Trees");
        Material trunk = Mat("TreeTrunk", new Color(0.06f, 0.045f, 0.035f), 0f);
        Material leaves = Mat("TreeLeaves", new Color(0.025f, 0.05f, 0.03f), 0f);
        System.Random rnd = new System.Random(7);

        Vector3[] trees =
        {
            new Vector3(-11f, 9f, CAM_Z + 9f),
            new Vector3(-8f, 10f, CAM_Z + 11.5f),
            new Vector3(-15f, 8.5f, CAM_Z + 11f),
            new Vector3(-19f, 9.5f, CAM_Z + 8f),
        };
        foreach (Vector3 t in trees)
        {
            float trunkH = t.y * 0.45f;
            Pillar("Trunk", g, new Vector3(t.x, 0f, t.z), trunkH, 0.22f, trunk);
            int blobs = 5 + rnd.Next(3);
            for (int i = 0; i < blobs; i++)
            {
                float rad = 1.8f + (float)rnd.NextDouble() * 1.2f;
                Vector3 p = new Vector3(
                    t.x + Rand(rnd, -1.6f, 1.6f),
                    trunkH + rad * 0.7f + Rand(rnd, -0.6f, 1.6f),
                    t.z + Rand(rnd, -1.6f, 1.6f));
                Prim(PrimitiveType.Sphere, "Leaves", g, p, Vector3.one * rad * 2f, leaves);
            }
        }
    }

    // ───────────────────────── 한강·노들섬 ─────────────────────────

    static void BuildRiver(Transform root)
    {
        Transform g = Group(root, "HanRiver");
        Material water = Mat("RiverWater", new Color(0.03f, 0.05f, 0.08f), 0.97f, null, 0.3f);
        GameObject w = Cube("Water", g, new Vector3(0f, WATER_Y - 0.1f, CAM_Z + 700f), new Vector3(3000f, 0.2f, 1400f), water);
        const int WATER_LAYER = 4; // Unity 기본 "Water" 레이어
        w.layer = WATER_LAYER;

        // 물에 비친 모습: 카메라를 수면 기준으로 뒤집은 위치에서 실시간 반사를 찍어요.
        // (불꽃·다리 조명·건물 불빛이 강물에 비쳐 보여요)
        GameObject probeGo = new GameObject("RiverReflectionProbe");
        probeGo.transform.SetParent(g, false);
        probeGo.transform.position = new Vector3(0f, 2f * WATER_Y - EYE_Y, CAM_Z);
        ReflectionProbe probe = probeGo.AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.EveryFrame;
        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
        probe.size = new Vector3(3000f, 400f, 3000f);
        probe.center = Vector3.zero;
        probe.resolution = 512;
        probe.hdr = true;
        probe.farClipPlane = 2000f;
        probe.cullingMask = ~(1 << WATER_LAYER);
        probe.importance = 10;

        // 불빛이 물에 길게 비친 빛줄기 (강 건너 도로등 · 다리 가로등)
        Material streak = Mat("WaterStreak", new Color(0.4f, 0.32f, 0.2f), 0.9f, new Color(1f, 0.75f, 0.45f) * 0.5f);
        float bankZ = CAM_Z + 580f;
        for (float x = -400f; x <= 400f; x += 25f)
            Cube("Streak", g, new Vector3(x, WATER_Y + 0.02f, bankZ - 46f), new Vector3(1.2f, 0.02f, 70f), streak);
        for (float z = CAM_Z + 36f; z < CAM_Z + 560f; z += 35f)
            Cube("Streak", g, new Vector3(BRIDGE_X - 10.5f, WATER_Y + 0.02f, z), new Vector3(0.8f, 0.02f, 14f), streak);
    }

    // 불꽃 발사 바지선: 모든 불꽃이 원점(이 바지선 위)에서 올라가요.
    static void BuildLaunchBarge(Transform root)
    {
        Transform g = Group(root, "LaunchBarge");
        Material hull = Mat("BargeHull", new Color(0.05f, 0.05f, 0.06f), 0.3f);
        Material redLamp = Mat("BargeLamp", new Color(0.6f, 0.05f, 0.03f), 0.5f, new Color(1f, 0.1f, 0.05f) * 2f);
        Cube("Barge", g, new Vector3(0f, WATER_Y / 2f, 0f), new Vector3(26f, -WATER_Y, 12f), hull);
        for (float x = -12f; x <= 12f; x += 4f)
            Prim(PrimitiveType.Sphere, "BargeLamp", g, new Vector3(x, 0.2f, -6.1f), Vector3.one * 0.4f, redLamp);
    }

    // 한강대교: 오른쪽 앞 둑에서 강 건너편까지 뻗는 다리 + 아치 5개
    static void BuildBridge(Transform root)
    {
        Transform g = Group(root, "HangangBridge");
        Material deckMat = Mat("BridgeDeck", new Color(0.12f, 0.12f, 0.13f), 0.2f);
        Material pierMat = Mat("BridgePier", new Color(0.2f, 0.2f, 0.2f), 0.1f);
        Material archMat = Mat("BridgeArch", new Color(0.6f, 0.62f, 0.65f), 0.5f, new Color(0.3f, 0.33f, 0.38f), 0.6f);
        Material lamp = Mat("BridgeLamp", new Color(1f, 0.9f, 0.7f), 0.5f, new Color(1f, 0.8f, 0.5f) * 6f);

        const float deckY = 1f;
        float zStart = CAM_Z + 26f, zEnd = CAM_Z + 560f;
        Cube("Deck", g, new Vector3(BRIDGE_X, deckY - 0.75f, (zStart + zEnd) / 2f),
            new Vector3(20f, 1.5f, zEnd - zStart), deckMat);

        // 아치 구간
        const int spans = 5;
        const float spanLen = 70f, rise = 16f, ribOffset = 9f;
        float archStart = CAM_Z + 40f;
        for (int sIdx = 0; sIdx < spans; sIdx++)
        {
            float za = archStart + sIdx * spanLen, zb = za + spanLen;
            Cube("Pier", g, new Vector3(BRIDGE_X, (WATER_Y + deckY) / 2f - 0.5f, za),
                new Vector3(16f, deckY - WATER_Y, 4f), pierMat);

            foreach (float side in new[] { -1f, 1f })
            {
                float x = BRIDGE_X + side * ribOffset;
                const int seg = 14;
                Vector3 prev = new Vector3(x, deckY, za);
                for (int k = 1; k <= seg; k++)
                {
                    float t = k / (float)seg;
                    Vector3 p = new Vector3(x, deckY + rise * Mathf.Sin(Mathf.PI * t), Mathf.Lerp(za, zb, t));
                    Bar("ArchRib", g, prev, p, 0.55f, archMat);
                    prev = p;
                }
                for (int k = 1; k < 10; k++)
                {
                    float t = k / 10f;
                    float z = Mathf.Lerp(za, zb, t);
                    Bar("Hanger", g, new Vector3(x, deckY, z),
                        new Vector3(x, deckY + rise * Mathf.Sin(Mathf.PI * t), z), 0.08f, archMat);
                }
            }
            foreach (float t in new[] { 0.3f, 0.5f, 0.7f })
            {
                float y = deckY + rise * Mathf.Sin(Mathf.PI * t);
                float z = Mathf.Lerp(za, zb, t);
                Bar("Brace", g, new Vector3(BRIDGE_X - ribOffset, y, z), new Vector3(BRIDGE_X + ribOffset, y, z), 0.3f, archMat);
            }
        }
        // 아치 없는 나머지 구간 교각
        for (float z = archStart + spans * spanLen; z < zEnd; z += 60f)
            Cube("Pier", g, new Vector3(BRIDGE_X, (WATER_Y + deckY) / 2f - 0.5f, z),
                new Vector3(16f, deckY - WATER_Y, 4f), pierMat);

        // 다리 가로등 (발광체만, 실제 조명은 생략)
        for (float z = zStart + 10f; z < zEnd; z += 35f)
            foreach (float side in new[] { -1f, 1f })
            {
                float x = BRIDGE_X + side * 10.5f;
                Pillar("LampPole", g, new Vector3(x, deckY, z), 6f, 0.08f, deckMat);
                Prim(PrimitiveType.Sphere, "LampHead", g, new Vector3(x, deckY + 6.2f, z), Vector3.one * 0.6f, lamp);
            }
    }

    // 노들섬: 다리 아래 섬 + 노들섬 공연장 건물 + 나무
    static void BuildNodeulIsland(Transform root)
    {
        Transform g = Group(root, "NodeulIsland");
        Material land = Mat("IslandLand", new Color(0.04f, 0.06f, 0.04f), 0.05f);
        Material white = Mat("NodeulBuilding", new Color(0.7f, 0.7f, 0.68f), 0.3f, new Color(0.08f, 0.08f, 0.08f));
        Material warm = Mat("WindowWarm", new Color(0.9f, 0.75f, 0.5f), 0.5f, new Color(1f, 0.8f, 0.55f) * 1.5f);
        Material leaves = Mat("TreeLeaves", new Color(0.025f, 0.05f, 0.03f), 0f);
        System.Random rnd = new System.Random(11);

        float zc = CAM_Z + 220f, top = WATER_Y + 5f;
        Cube("Island", g, new Vector3(60f, top - 2.5f, zc), new Vector3(180f, 5f, 80f), land);

        // 공연장: 계단식 흰 건물 세 덩어리
        Vector4[] blocks =
        {
            new Vector4(75f, 12f, 40f, 24f),   // x, 높이, 폭, 깊이
            new Vector4(108f, 8f, 26f, 20f),
            new Vector4(130f, 5f, 18f, 16f),
        };
        foreach (Vector4 b in blocks)
        {
            float front = zc - 18f;
            Cube("NodeulHall", g, new Vector3(b.x, top + b.y / 2f, front + b.w / 2f), new Vector3(b.z, b.y, b.w), white);
            for (float wy = top + 1.5f; wy < top + b.y - 1f; wy += 3f)
                Cube("WindowStrip", g, new Vector3(b.x, wy, front - 0.03f), new Vector3(b.z * 0.85f, 0.9f, 0.05f), warm);
        }
        for (int i = 0; i < 26; i++)
        {
            float rad = Rand(rnd, 2f, 3.5f);
            Prim(PrimitiveType.Sphere, "IslandTree", g,
                new Vector3(Rand(rnd, -20f, 50f), top + rad * 0.8f, zc + Rand(rnd, -30f, 30f)), Vector3.one * rad * 2f, leaves);
        }
    }

    // 강 건너편 여의도 스카이라인
    static void BuildSkyline(Transform root)
    {
        Transform g = Group(root, "Skyline");
        Material wall = Mat("BuildingWall", new Color(0.04f, 0.045f, 0.06f), 0.1f);
        Material warm = Mat("WindowWarm", new Color(0.9f, 0.75f, 0.5f), 0.5f, new Color(1f, 0.8f, 0.55f) * 1.5f);
        Material cool = Mat("WindowCool", new Color(0.7f, 0.8f, 0.9f), 0.5f, new Color(0.7f, 0.85f, 1f) * 1.2f);
        Material gold = Mat("Gold63", new Color(0.2f, 0.16f, 0.07f), 0.8f, new Color(0.3f, 0.22f, 0.07f) * 0.5f, 0.8f);
        Material lamp = Mat("BridgeLamp", new Color(1f, 0.9f, 0.7f), 0.5f, new Color(1f, 0.8f, 0.5f) * 6f);
        System.Random rnd = new System.Random(42);

        float bankZ = CAM_Z + 580f;
        const float BANK_H = 8f;   // 강 건너 둑 높이 (수면 위 8m)
        float bankTop = WATER_Y + BANK_H;
        Material bankWall = Mat("FarBankWall", new Color(0.16f, 0.16f, 0.17f), 0.1f);
        Cube("FarBank", g, new Vector3(0f, WATER_Y + BANK_H / 2f, bankZ + 140f), new Vector3(2000f, BANK_H, 300f), bankWall);
        for (float x = -900f; x <= 900f; x += 25f)
        {
            Pillar("RoadLightPole", g, new Vector3(x, bankTop, bankZ + 3f), 5f, 0.15f, wall);
            Prim(PrimitiveType.Sphere, "RoadLight", g, new Vector3(x, bankTop + 5.3f, bankZ + 3f), Vector3.one * 1.2f, lamp);
        }

        for (int i = 0; i < 46; i++)
        {
            float w = Rand(rnd, 20f, 45f), d = Rand(rnd, 20f, 35f), h = Rand(rnd, 20f, 90f);
            float x = -720f + i * 32f + Rand(rnd, -8f, 8f);
            float z = bankZ + Rand(rnd, 40f, 200f);
            Cube("Tower", g, new Vector3(x, bankTop + h / 2f, z), new Vector3(w, h, d), wall);

            float front = z - d / 2f - 0.05f;
            for (float wy = bankTop + 4f; wy < bankTop + h - 2f; wy += 4f)
                for (float wx = x - w / 2f + 3f; wx < x + w / 2f - 2f; wx += 5f)
                {
                    if (rnd.NextDouble() > 0.15) continue;
                    Cube("Window", g, new Vector3(wx, wy, front), new Vector3(3f, 2f, 0.1f),
                        rnd.NextDouble() < 0.7 ? warm : cool);
                }
        }

        // 63빌딩 느낌의 금색 고층 타워 (왼쪽 멀리)
        float tz = bankZ + 120f;
        Cube("GoldTower", g, new Vector3(-320f, bankTop + 115f, tz), new Vector3(40f, 230f, 28f), gold);
        Cube("GoldTowerTop", g, new Vector3(-320f, bankTop + 240f, tz), new Vector3(30f, 20f, 22f), gold);
        for (float wy = bankTop + 8f; wy < bankTop + 225f; wy += 7f)
            Cube("GoldWindows", g, new Vector3(-320f, wy, tz - 14.05f), new Vector3(34f, 1.2f, 0.1f), warm);
    }

    // ───────────────────────── 도우미 ─────────────────────────

    static GameObject FindInScene(string name)
    {
        Scene scene = SceneManager.GetActiveScene();
        foreach (GameObject rootGo in scene.GetRootGameObjects())
            foreach (Transform t in rootGo.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.gameObject;
        return null;
    }

    static Transform Group(Transform parent, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.localScale = scale;
        Collider c = go.GetComponent<Collider>();
        if (c != null) Object.DestroyImmediate(c);
        Renderer rend = go.GetComponent<Renderer>();
        rend.sharedMaterial = mat;
        rend.shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }

    static GameObject Cube(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, Vector3? euler = null)
    {
        GameObject go = Prim(PrimitiveType.Cube, name, parent, pos, scale, mat);
        if (euler.HasValue) go.transform.eulerAngles = euler.Value;
        return go;
    }

    // 바닥(bottom)에서 위로 height만큼 세운 원기둥
    static GameObject Pillar(string name, Transform parent, Vector3 bottom, float height, float radius, Material mat)
    {
        return Prim(PrimitiveType.Cylinder, name, parent, bottom + Vector3.up * height / 2f,
            new Vector3(radius * 2f, height / 2f, radius * 2f), mat);
    }

    // a에서 b까지 이어지는 원기둥 막대
    static GameObject Bar(string name, Transform parent, Vector3 a, Vector3 b, float radius, Material mat)
    {
        Vector3 d = b - a;
        GameObject go = Prim(PrimitiveType.Cylinder, name, parent, (a + b) / 2f,
            new Vector3(radius * 2f, d.magnitude / 2f, radius * 2f), mat);
        go.transform.rotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
        return go;
    }

    // 축 늘어진 전선 (LineRenderer)
    static void Wire(Transform parent, Vector3 a, Vector3 b, float sag, float width, Material mat)
    {
        GameObject go = new GameObject("Wire");
        go.transform.SetParent(parent, false);
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        const int seg = 20;
        lr.positionCount = seg + 1;
        for (int i = 0; i <= seg; i++)
        {
            float t = i / (float)seg;
            Vector3 p = Vector3.Lerp(a, b, t);
            p.y -= sag * 4f * t * (1f - t);
            lr.SetPosition(i, p);
        }
        lr.startWidth = lr.endWidth = width;
        lr.sharedMaterial = mat;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;
    }

    static float Rand(System.Random rnd, float min, float max) => min + (float)rnd.NextDouble() * (max - min);

    static Material Mat(string name, Color baseColor, float smoothness, Color? emission = null, float metallic = 0f)
    {
        Material m = GetOrCreateMat(name, "Universal Render Pipeline/Lit");
        m.SetColor("_BaseColor", baseColor);
        m.SetFloat("_Smoothness", smoothness);
        m.SetFloat("_Metallic", metallic);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.SetColor("_EmissionColor", emission.Value);
        }
        else
        {
            m.DisableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Unlit(string name, Color color)
    {
        Material m = GetOrCreateMat(name, "Universal Render Pipeline/Unlit");
        m.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material GetOrCreateMat(string name, string shaderName)
    {
        if (_mats.TryGetValue(name, out Material cached)) return cached;

        if (!AssetDatabase.IsValidFolder("Assets/_Street")) AssetDatabase.CreateFolder("Assets", "_Street");
        if (!AssetDatabase.IsValidFolder(MAT_DIR)) AssetDatabase.CreateFolder("Assets/_Street", "Materials");

        string path = $"{MAT_DIR}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = Shader.Find(shaderName);
        if (m == null)
        {
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        else if (m.shader != shader)
        {
            m.shader = shader;
        }
        _mats[name] = m;
        return m;
    }
}
