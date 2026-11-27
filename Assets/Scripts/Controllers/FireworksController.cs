using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 불꽃놀이 연출 총괄.
// - 씬을 바로 실행해도 BGM이 나오도록, 이 오브젝트의 AudioSource로 직접 재생해요.
// - 각 Phase는 "음악의 재생 시간"을 기준으로 터져요. (프레임이 밀려도 음악과 어긋나지 않음)
// - 작업 편의 기능:
//     * _debugStartTime : 그 초부터 바로 시작 (뒷부분 연출 확인할 때)
//     * 실행 중 Space   : 현재 음악 시간을 Console에 찍어줌 (박자·구간 타이밍 따기용)
public class FireworksController : MonoBehaviour
{
    [Header("BGM")]
    [SerializeField] AudioClip _bgmClip;
    [SerializeField, Range(0f, 1f)] float _bgmVolume = 1f;

    [Header("연출 단계 (시간 순서대로)")]
    [SerializeField] Phase[] _phases;

    [Header("작업용")]
    [Tooltip("0보다 크면 음악과 연출을 이 시간(초)부터 시작해요. 제출 전엔 0으로!")]
    [SerializeField] float _debugStartTime = 0f;
    [Tooltip("실행 중 이 키를 누르면 현재 음악 시간을 Console에 출력해요.")]
    [SerializeField] KeyCode _markKey = KeyCode.Space;

    AudioSource _audioSource;
    float _fallbackStartTime;
    readonly List<GameObject> _liParticleList = new List<GameObject>();

    // 지금 음악이 몇 초째인지. 음악이 없으면 씬 시작부터 흐른 시간.
    float CurrentTime
    {
        get
        {
            if (_audioSource != null && _audioSource.clip != null && _audioSource.isPlaying)
                return _audioSource.time;
            return Time.time - _fallbackStartTime;
        }
    }

    void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
            _audioSource = gameObject.AddComponent<AudioSource>();

        _audioSource.playOnAwake = false;
        _audioSource.loop = false;
        _audioSource.spatialBlend = 0f; // 2D 사운드
    }

    void Start()
    {
        float startTime = Mathf.Max(0f, _debugStartTime);
        _fallbackStartTime = Time.time - startTime;

        if (_bgmClip != null)
        {
            _audioSource.clip = _bgmClip;
            _audioSource.volume = _bgmVolume;
            _audioSource.time = Mathf.Min(startTime, _bgmClip.length - 0.01f);
            _audioSource.Play();
        }
        else
        {
            Debug.LogWarning("[FireworksController] BGM Clip이 비어 있어요. 시간만 흐르게 할게요.");
        }

        StartCoroutine(PlayPhases(startTime));
    }

    void Update()
    {
        if (Input.GetKeyDown(_markKey))
            Debug.Log($"[Fireworks] ♪ {CurrentTime:F2}초");
    }

    IEnumerator PlayPhases(float startTime)
    {
        // 시작 시간보다 이미 지난 단계는 건너뛰되, 직전 단계 하나는 바로 틀어줘요.
        Phase lastSkipped = null;
        int index = 0;
        for (; index < _phases.Length; index++)
        {
            if (_phases[index]._fStartTime > startTime) break;
            lastSkipped = _phases[index];
        }
        if (lastSkipped != null && startTime > 0f)
            PlayCurrentParticleSystems(lastSkipped);

        for (; index < _phases.Length; index++)
        {
            Phase phase = _phases[index];
            while (CurrentTime < phase._fStartTime)
                yield return null;

            PlayCurrentParticleSystems(phase);
        }
    }

    void PlayCurrentParticleSystems(Phase currentPhase)
    {
        // 앞 단계 불꽃은 새로 쏘는 것만 멈추고, 이미 터진 건 자연스럽게 사라지게 둬요.
        foreach (GameObject obj in _liParticleList)
        {
            if (obj == null) continue;
            ParticleSystem ps = obj.GetComponent<ParticleSystem>();
            if (ps != null) ps.Stop();
            Destroy(obj, 15f);
        }
        _liParticleList.Clear();

        if (currentPhase._pFireworks == null) return;

        foreach (ParticleSystem particleSystem in currentPhase._pFireworks)
        {
            if (particleSystem == null) continue;
            GameObject particleSystemObj = Instantiate(particleSystem.gameObject, Vector3.zero, Quaternion.identity);
            _liParticleList.Add(particleSystemObj);
            // 강물 가짜 반사용 레이어로 지정
            FireworksReflection.SetLayerRecursively(particleSystemObj, FireworksReflection.FIREWORKS_LAYER);
            particleSystemObj.GetComponent<ParticleSystem>().Play();
        }
    }
}

[Serializable]
public class Phase
{
    [Tooltip("음악 기준 시작 시간(초)")]
    public float _fStartTime;
    [Tooltip("이 단계에서 쏠 불꽃 프리팹들")]
    public ParticleSystem[] _pFireworks;
}
