using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Story
{
    /// <summary>
    /// Kitchen wall telephone story encounter source.
    /// Periodically rings with a spatial 3D retro telephone ring loop.
    /// Unanswered calls after 25s increment missedStoryCounter.
    /// Answering zooms the camera, freezes crawler movement, enables CRT post-processing,
    /// and presents the Tinder/Reigns swipe card UI.
    /// </summary>
    public class KitchenPhone : MonoBehaviour
    {
        public static KitchenPhone Instance { get; private set; }

        [Header("Ring Interval Settings (Seconds)")]
        [Tooltip("Minimum time between calls during normal gameplay.")]
        public float minRingInterval = 120f;
        [Tooltip("Maximum time between calls during normal gameplay.")]
        public float maxRingInterval = 240f;

        [Header("Endgame Ring Interval (Post-Max DVD & Discs)")]
        [Tooltip("Minimum time between calls during endgame pressure.")]
        public float endgameMinRingInterval = 45f;
        [Tooltip("Maximum time between calls during endgame pressure.")]
        public float endgameMaxRingInterval = 90f;

        [Header("Call Timeout")]
        [Tooltip("Time before an unanswered ring expires and counts as a missed call.")]
        public float ringTimeoutSeconds = 25f;

        [Header("Audio")]
        public AudioSource phoneAudio;
        public AudioClip ringLoopClip;
        public AudioClip pickupClip;
        public AudioClip hangupClip;

        [Header("Visual Feedback")]
        public bool shakeWhileRinging = true;
        public float ringShakeIntensity = 0.008f;

        [Header("Screen Effects & Camera (Match Death Sequence)")]
        [Tooltip("If true, switches CRT shader to duringpaused.mat. If false, keeps duringgameplay.mat just like the death sequence.")]
        public bool usePausedCrtOnCall = false;
        [Tooltip("If true, zooms camera into the kitchen phone model. If false, keeps normal player camera view just like the death sequence.")]
        public bool zoomCameraOnCall = false;

        private InteractableObject interactable;
        private Collider phoneCollider;
        private InteractVisualizer visualizer;
        private bool isRinging = false;
        private bool isInCall = false;
        public bool IsInCall => isInCall;
        private float ringTimer = 0f;
        private float nextRingCountdown = 0f;
        private Vector3 originalLocalPos;
        private SmoothCameraFollow cachedCameraFollow;

        // Auto-bootstrap in 01Day scene
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InitSceneLoadedListener()
        {
            SceneManager.sceneLoaded -= OnAnySceneLoaded;
            SceneManager.sceneLoaded += OnAnySceneLoaded;

            if (SceneManager.GetActiveScene().name == "01Day")
            {
                EnsureInstanceInScene();
            }
        }

        private static void OnAnySceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "01Day")
            {
                EnsureInstanceInScene();
            }
        }

        public static KitchenPhone EnsureInstanceInScene()
        {
            if (Instance != null) return Instance;

            KitchenPhone existing = FindObjectOfType<KitchenPhone>(true);
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }

            // Search active scene objects even if deeply nested in prefab hierarchies
            Transform[] allTransforms = GameObject.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Transform phoneTransform = null;
            string activeSceneName = SceneManager.GetActiveScene().name;

            foreach (Transform t in allTransforms)
            {
                if (t == null) continue;
                if (!string.IsNullOrEmpty(activeSceneName) && t.gameObject.scene.name != activeSceneName) continue;
                if (string.Equals(t.name, "Phone", StringComparison.OrdinalIgnoreCase) ||
                    t.name.IndexOf("KitchenPhone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.name.EndsWith("Phone", StringComparison.OrdinalIgnoreCase))
                {
                    phoneTransform = t;
                    break;
                }
            }

            if (phoneTransform != null)
            {
                existing = phoneTransform.gameObject.GetComponent<KitchenPhone>();
                if (existing == null) existing = phoneTransform.gameObject.AddComponent<KitchenPhone>();
                Instance = existing;
                return existing;
            }

            // Fallback: spawn KitchenPhoneAnchor at known kitchen wall coordinates
            GameObject anchor = new GameObject("KitchenPhoneAnchor");
            anchor.transform.position = new Vector3(-5.298f, 2.277f, -1.953f);
            existing = anchor.AddComponent<KitchenPhone>();
            Instance = existing;
            return existing;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            originalLocalPos = transform.localPosition;
            SetupComponents();
        }

        private void Start()
        {
            cachedCameraFollow = FindObjectOfType<SmoothCameraFollow>();
            EnsureStoryCardCanvas();

            // Do not ring during secret Good Day
            if (RunSaveManager.HasInstance && RunSaveManager.Instance.isGoodDayActive)
            {
                enabled = false;
                if (interactable != null) interactable.enabled = false;
                return;
            }

            ScheduleNextRing(isInitialDelay: true);
        }

        private void Update()
        {
            if (RunSaveManager.HasInstance && RunSaveManager.Instance.isGoodDayActive)
            {
                if (isRinging) StopRinging();
                enabled = false;
                return;
            }

            // Guard: While a call is in progress, absolutely no timers, rings, or editor key triggers
            if (isInCall) return;

#if UNITY_EDITOR
            // In-Editor testing: Press 'P' to ring the phone (walk up to it to answer)
            if (WasEditorPKeyPressed())
            {
                Debug.Log("[KitchenPhone] Editor debug: Telephone started ringing (P pressed). Walk up to the phone in the kitchen to answer it.");
                ForceRingPhoneInEditor();
                return;
            }
#endif

            if (isRinging)
            {
                ringTimer -= Time.deltaTime;

                if (shakeWhileRinging)
                {
                    float shake = Mathf.Sin(Time.time * 65f) * ringShakeIntensity;
                    transform.localPosition = originalLocalPos + new Vector3(shake, 0f, 0f);
                }

                if (ringTimer <= 0f)
                {
                    OnCallMissed();
                }
            }
            else
            {
                if (nextRingCountdown > 0f)
                {
                    nextRingCountdown -= Time.deltaTime;
                    if (nextRingCountdown <= 0f)
                    {
                        StartRinging();
                    }
                }
            }
        }

        private void SetupComponents()
        {
            // Visualizer (Swirl outline when ringing)
            if (visualizer == null)
            {
                visualizer = GetComponent<InteractVisualizer>();
                if (visualizer == null) visualizer = GetComponentInChildren<InteractVisualizer>(true);
                if (visualizer == null) visualizer = gameObject.AddComponent<InteractVisualizer>();
            }
            if (visualizer != null)
            {
                visualizer.glowColor = new Color(1f, 0.82f, 0.25f, 1f);
                visualizer.thickness = 0.035f;
                visualizer.ResetGlow(keepPeriodicGlow: false);
            }

            // Collider
            phoneCollider = GetComponent<Collider>();
            if (phoneCollider == null)
            {
                BoxCollider box = gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(0.5f, 0.7f, 0.4f);
                box.center = Vector3.zero;
                phoneCollider = box;
            }

            // Interactable
            interactable = GetComponent<InteractableObject>();
            if (interactable == null)
            {
                interactable = gameObject.AddComponent<InteractableObject>();
            }
            if (interactable.onClick == null)
            {
                interactable.onClick = new UnityEngine.Events.UnityEvent();
            }
            interactable.hoverText = "Telephone (Idle)";
            interactable.onClick.RemoveListener(OnPhoneInteracted);
            interactable.onClick.AddListener(OnPhoneInteracted);
            interactable.enabled = false; // only interactable while ringing

            // AudioSource (3D spatial audio)
            if (phoneAudio == null)
            {
                phoneAudio = GetComponent<AudioSource>();
                if (phoneAudio == null) phoneAudio = gameObject.AddComponent<AudioSource>();
            }
            phoneAudio.playOnAwake = false;
            phoneAudio.spatialBlend = 1.0f; // 3D sound
            phoneAudio.minDistance = 1.5f;
            phoneAudio.maxDistance = 16f;
            phoneAudio.rolloffMode = AudioRolloffMode.Linear;

            if (ringLoopClip == null) ringLoopClip = GenerateProceduralRingClip();
            if (pickupClip == null) pickupClip = GenerateProceduralClickClip(880f, 0.05f);
            if (hangupClip == null) hangupClip = GenerateProceduralClickClip(440f, 0.07f);
        }

        private void EnsureStoryCardCanvas()
        {
            StoryCardController.EnsureInstance();
            StoryManager.EnsureInstanceInScene();
        }

        private void ScheduleNextRing(bool isInitialDelay = false)
        {
            bool isEndgame = UpgradeManager.Instance != null && UpgradeManager.Instance.IsEndgameMaxed;
            float min = isEndgame ? endgameMinRingInterval : minRingInterval;
            float max = isEndgame ? endgameMaxRingInterval : maxRingInterval;

            if (isInitialDelay)
            {
                // Give player some breathing room right after scene load
                min = isEndgame ? 25f : 60f;
                max = isEndgame ? 45f : 120f;
            }

            nextRingCountdown = UnityEngine.Random.Range(min, max);
        }

        public void StartRinging()
        {
            if (isRinging || isInCall) return;
            isRinging = true;
            ringTimer = ringTimeoutSeconds;

            if (interactable != null)
            {
                interactable.enabled = true;
                interactable.hoverText = "Answer Phone";
            }

            // Trigger interact visualizer swirl effect while ringing
            if (visualizer == null)
            {
                visualizer = GetComponent<InteractVisualizer>();
                if (visualizer == null) visualizer = GetComponentInChildren<InteractVisualizer>(true);
                if (visualizer == null) visualizer = gameObject.AddComponent<InteractVisualizer>();
            }
            if (visualizer != null)
            {
                visualizer.glowColor = new Color(1f, 0.82f, 0.25f, 1f);
                visualizer.enablePeriodicGlow = true;
                visualizer.SetIntensity(1f);
            }

            if (phoneAudio != null && ringLoopClip != null)
            {
                phoneAudio.clip = ringLoopClip;
                phoneAudio.loop = true;
                phoneAudio.Play();
            }
        }

        public void StopRinging()
        {
            if (!isRinging) return;
            isRinging = false;
            transform.localPosition = originalLocalPos;

            // Turn off interact visualizer effect when ringing ceases
            if (visualizer != null)
            {
                visualizer.ResetGlow(keepPeriodicGlow: false);
            }

            if (phoneAudio != null && phoneAudio.isPlaying)
            {
                phoneAudio.Stop();
            }

            if (interactable != null)
            {
                interactable.enabled = false;
                interactable.hoverText = "Telephone (Idle)";
            }
        }

        private void OnCallMissed()
        {
            StopRinging();

            if (phoneAudio != null && hangupClip != null)
            {
                phoneAudio.PlayOneShot(hangupClip, 0.6f);
            }

            if (RunSaveManager.HasInstance)
            {
                RunSaveManager.Instance.missedStoryCounter += 0.5f;
            }

            ScheduleNextRing();
        }

        public void OnPhoneInteracted()
        {
            if (!isRinging || isInCall) return;
            isInCall = true;
            StopRinging();

            if (phoneAudio != null && pickupClip != null)
            {
                phoneAudio.PlayOneShot(pickupClip, 0.8f);
            }

            // Lock movement and mouse look
            GridCrawlerController.InputFrozen = true;

            // Screen effects: match the death sequence (subtle gameplay CRT) unless paused CRT is explicitly requested
            if (usePausedCrtOnCall)
            {
                CrtPostProcessManager.SetPausedCrtActive(true);
            }
            else
            {
                CrtPostProcessManager.SetGameplayCrtActive();
            }

            // Optional zoom camera smoothly into the phone
            if (zoomCameraOnCall)
            {
                if (cachedCameraFollow == null) cachedCameraFollow = FindObjectOfType<SmoothCameraFollow>();
                if (cachedCameraFollow != null)
                {
                    Vector3 phoneCenter = transform.position + Vector3.up * 0.1f;
                    Camera mainCam = Camera.main;
                    Vector3 viewDir = mainCam != null ? (mainCam.transform.position - phoneCenter).normalized : transform.forward;
                    Vector3 focusPos = phoneCenter + viewDir * 0.68f + Vector3.up * 0.06f;
                    Quaternion focusRot = Quaternion.LookRotation((phoneCenter - focusPos).normalized, Vector3.up);

                    cachedCameraFollow.EnterFocusMode(focusPos, focusRot, 36f);
                }
            }

            // Unlock and reveal cursor for UI interaction
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            StoryCardController cardController = StoryCardController.EnsureInstance();
            if (cardController != null)
            {
                cardController.ShowNextEncounter(OnEncounterFinished);
            }
        }

        private void OnEncounterFinished()
        {
            isInCall = false;

            if (phoneAudio != null && hangupClip != null)
            {
                phoneAudio.PlayOneShot(hangupClip, 0.8f);
            }

            // Restore camera FOV and position if zoomed
            if (cachedCameraFollow != null && zoomCameraOnCall)
            {
                cachedCameraFollow.ExitFocusMode();
            }

            // Unlock player input
            GridCrawlerController.InputFrozen = false;

            // Ensure normal gameplay CRT is active
            CrtPostProcessManager.SetGameplayCrtActive();

            // Schedule the next encounter call
            ScheduleNextRing();
        }

        // =========================================================================
        // PROCEDURAL RETRO TELEPHONE AUDIO SYNTHESIS
        // =========================================================================

        private AudioClip GenerateProceduralRingClip()
        {
            const int sampleRate = 44100;
            const float totalLength = 5.0f; // 2s ring, 3s silence cadence
            int totalSamples = (int)(sampleRate * totalLength);
            float[] data = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;

                // 2-second ringing burst
                if (t < 2.0f)
                {
                    // Vintage bell clapper modulation (20Hz square pulse)
                    float clapper = Mathf.Sin(2f * Mathf.PI * 20f * t) > 0f ? 1.0f : 0.05f;

                    // Classic dual-tone frequencies (440Hz + 480Hz)
                    float tone1 = Mathf.Sin(2f * Mathf.PI * 440f * t);
                    float tone2 = Mathf.Sin(2f * Mathf.PI * 480f * t);

                    // Fade in / fade out envelope to avoid clicks
                    float env = 1f;
                    if (t < 0.05f) env = t / 0.05f;
                    else if (t > 1.95f) env = (2.0f - t) / 0.05f;

                    data[i] = (tone1 * 0.5f + tone2 * 0.5f) * clapper * env * 0.35f;
                }
                else
                {
                    data[i] = 0f;
                }
            }

            AudioClip clip = AudioClip.Create("VintageTelephoneRing", totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateProceduralClickClip(float freq, float duration)
        {
            const int sampleRate = 44100;
            int totalSamples = (int)(sampleRate * duration);
            float[] data = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                float env = 1f - (t / duration);
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env * 0.3f;
            }

            AudioClip clip = AudioClip.Create("PhoneClick", totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

#if UNITY_EDITOR
        [ContextMenu("Force Start Phone Sequence")]
        public void ForceStartPhoneSequenceInEditor()
        {
            if (isInCall) return;
            isInCall = true;

            SetupComponents();

            StopRinging();

            if (phoneAudio != null && pickupClip != null)
            {
                phoneAudio.PlayOneShot(pickupClip, 0.8f);
            }

            // Lock movement and mouse look
            GridCrawlerController.InputFrozen = true;

            // Screen effects: match the death sequence (subtle gameplay CRT) unless paused CRT is explicitly requested
            if (usePausedCrtOnCall)
            {
                CrtPostProcessManager.SetPausedCrtActive(true);
            }
            else
            {
                CrtPostProcessManager.SetGameplayCrtActive();
            }

            // Optional zoom camera smoothly into the phone
            if (zoomCameraOnCall)
            {
                if (cachedCameraFollow == null) cachedCameraFollow = FindObjectOfType<SmoothCameraFollow>();
                if (cachedCameraFollow != null)
                {
                    Vector3 phoneCenter = transform.position + Vector3.up * 0.1f;
                    Camera mainCam = Camera.main != null ? Camera.main : cachedCameraFollow.GetComponent<Camera>();
                    Vector3 viewDir = mainCam != null ? (mainCam.transform.position - phoneCenter).normalized : transform.forward;
                    Vector3 focusPos = phoneCenter + viewDir * 0.68f + Vector3.up * 0.06f;
                    Quaternion focusRot = Quaternion.LookRotation((phoneCenter - focusPos).normalized, Vector3.up);

                    cachedCameraFollow.EnterFocusMode(focusPos, focusRot, 36f);
                }
            }

            // Unlock and reveal cursor for UI interaction
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            StoryCardController cardController = StoryCardController.EnsureInstance();
            if (cardController != null)
            {
                cardController.ShowNextEncounter(OnEncounterFinished);
            }
            else
            {
                Debug.LogError("[KitchenPhone] StoryCardController could not be obtained!");
            }
        }

        [ContextMenu("Force Ring Phone")]
        public void ForceRingPhoneInEditor()
        {
            if (isInCall) return;
            if (isRinging)
            {
                ringTimer = ringTimeoutSeconds;
                return;
            }
            StartRinging();
        }

        private bool WasEditorPKeyPressed()
        {
            try
            {
                if (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame)
                {
                    return true;
                }
            }
            catch {}

            try
            {
                if (Input.GetKeyDown(KeyCode.P))
                {
                    return true;
                }
            }
            catch {}

            return false;
        }
#endif
    }
}
