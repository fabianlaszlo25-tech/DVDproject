using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dynamic Music Manager that listens to <see cref="BarManager"/>, single consumable effects,
/// and all 2-substance / 3-substance combinations from <see cref="SubstanceCombinationManager"/>.
/// Also normalizes track volumes across all assigned clips and compensates for AudioListener SFX volume changes.
/// </summary>
public class DynamicMusicManager : MonoBehaviour
{
    public enum SubstanceMusicPriority
    {
        [Tooltip("Substance/Combo music overrides Base music, but High/Extreme Bar themes still take priority unless in 3-Effect Overdose.")]
        OverrideBaseMusicOnly,
        [Tooltip("Substance & Combo music overrides everything while active.")]
        OverrideEverything,
        [Tooltip("Keep playing the Bar/Base music and only apply pitch/tempo warping.")]
        PitchModulationOnly
    }

    [System.Serializable]
    public struct TrackNormalizationEntry
    {
        public string trackName;
        public AudioClip clip;
        [Tooltip("Measured active RMS loudness of the clip.")]
        public float measuredRms;
        [Tooltip("Measured peak amplitude of the clip.")]
        public float measuredPeak;
        [Tooltip("Calculated volume multiplier applied to normalize this track.")]
        [Range(0.05f, 4f)] public float volumeMultiplier;
    }

    [Header("Core References (Auto-filled if left empty)")]
    public BarManager barManager;

    [Header("1. Base Music Playlist (Calm State)")]
    [Tooltip("List of normal background tracks played when no bars are above the critical threshold.")]
    public List<AudioClip> baseMusicPlaylist = new List<AudioClip>();
    public bool shuffleBasePlaylist = false;

    [Header("2. Bar Threshold & Specific Bar Themes")]
    [Tooltip("Percentage (0..1) a bar must reach to trigger its theme (e.g., 0.70 = 70% full).")]
    [Range(0.1f, 0.99f)]
    public float barTriggerPercentage = 0.70f;
    [Tooltip("Hysteresis margin (e.g. 0.05) so music doesn't rapidly flicker if a bar hovers right at 70%.")]
    [Range(0.01f, 0.20f)]
    public float barReleaseMargin = 0.05f;

    public AudioClip hungerTheme;
    public AudioClip needsTheme;
    public AudioClip sleepTheme;
    public AudioClip dirtinessTheme;
    public AudioClip depressionTheme;

    [Header("3. Extreme Music (Multiple Bars Filled)")]
    [Tooltip("How many bars must be above barTriggerPercentage at the same time to trigger the Extreme music.")]
    [Min(2)]
    public int multipleBarsCountForExtreme = 2;
    [Tooltip("Played when multiple bars are critically high at once.")]
    public List<AudioClip> extremeMusicTracks = new List<AudioClip>();

    [Header("4. Single Consumable Music Tracks (Optional)")]
    public SubstanceMusicPriority substancePriority = SubstanceMusicPriority.OverrideBaseMusicOnly;
    public AudioClip drunkMusicTrack;
    public AudioClip caffeinatedMusicTrack;
    public AudioClip highMusicTrack;
    public AudioClip nicotinedMusicTrack;

    [Header("5. Combination & Overdose Music Tracks (Optional)")]
    [Tooltip("Nicotine + Caffeine (Hyperfocus) track.")]
    public AudioClip hyperfocusComboTrack;
    [Tooltip("Alcohol + Caffeine (Heart Attack) track.")]
    public AudioClip heartAttackComboTrack;
    [Tooltip("Alcohol + Weed (Hányinger / Nausea) track.")]
    public AudioClip hanyingerComboTrack;
    [Tooltip("Weed + Nicotine OR Weed + Caffeine (Anxiety Attack) track.")]
    public AudioClip anxietyAttackComboTrack;
    [Tooltip("Alcohol + Nicotine (Dizzy Buzz) track.")]
    public AudioClip dizzyBuzzComboTrack;
    [Tooltip("3 Substances at once (Super Overdose / Hallucination) track.")]
    public AudioClip threeEffectOverdoseTrack;

    [Header("Substance & Combo Pitch / Tempo Modulation")]
    public bool enableSubstancePitchModulation = true;
    public float caffeinePitchMultiplier = 1.12f;
    public float highPitchMultiplier = 0.88f;
    public float drunkBasePitch = 0.95f;
    public float drunkPitchWobbleAmount = 0.045f;
    public float nicotinePitchMultiplier = 1.04f;

    [Header("Crossfade & Volume Settings")]
    [Range(0f, 1f)] public float masterMusicVolume = 0.8f;
    public float crossfadeDuration = 2.0f;

    [Header("Automatic Track Volume Normalization")]
    [Tooltip("Automatically normalizes the volume of each track based on its RMS loudness so louder tracks are quieted and quieter tracks are boosted to match.")]
    public bool normalizeTrackVolumes = true;
    [Tooltip("Target RMS loudness that all tracks are normalized toward.")]
    [Range(0.03f, 0.40f)] public float targetNormalizedRms = 0.14f;
    [Tooltip("Minimum volume multiplier allowed for loud tracks.")]
    [Range(0.05f, 1.0f)] public float minNormalizationMultiplier = 0.25f;
    [Tooltip("Maximum volume multiplier allowed for quiet tracks.")]
    [Range(1.0f, 3.0f)] public float maxNormalizationMultiplier = 2.2f;
    [Tooltip("Per-track normalization data (auto-calculated in Editor and at runtime).")]
    public List<TrackNormalizationEntry> trackNormalizations = new List<TrackNormalizationEntry>();

    [Header("Settings Menu Sliders & Secret SFX Listener Offset")]
    [Tooltip("Controlled by the Music Slider in the Pause/Settings menu (0..1).")]
    [Range(0f, 1f)] public float musicSliderVolume = 1.0f;
    [Tooltip("Current AudioListener volume controlled by the SFX Slider in the Settings menu (0..1).")]
    [Range(0f, 1f)] public float currentSfxListenerVolume = 1.0f;
    [Tooltip("Secret compensating boost (1 / AudioListener.volume) applied to offset SFX AudioListener volume changes.")]
    public float sfxOffsetMusicBoost = 1.0f;

    private readonly Dictionary<AudioClip, float> runtimeNormalizationCache = new Dictionary<AudioClip, float>();

    private AudioSource activeSource;
    private AudioSource fadingOutSource;
    private float activeFadeWeight = 0f;
    private float fadingOutWeight = 0f;
    private int currentBasePlaylistIndex = -1;
    private bool isInBarCrisisState = false;
    private bool isPausedByMenu = false;

    public bool IsPausedByMenu => isPausedByMenu;

    private void OnValidate()
    {
        SyncOrCalculateTrackNormalizations(false);
        UpdateSfxOffsetBoostFactor();
    }

    [ContextMenu("Recalculate Track Normalizations Now")]
    public void RecalculateTrackNormalizations()
    {
        SyncOrCalculateTrackNormalizations(true);
    }

    private void Awake()
    {
        if (barManager == null) barManager = FindObjectOfType<BarManager>();

        GlobalAudioSettings.ApplyLoadedSettings();
        musicSliderVolume = GlobalAudioSettings.MusicVolume;
        currentSfxListenerVolume = GlobalAudioSettings.SfxVolume;

        UpdateSfxOffsetBoostFactor();
        SyncOrCalculateTrackNormalizations(false);

        activeSource = CreateMusicSource("MusicChannel_A");
        fadingOutSource = CreateMusicSource("MusicChannel_B");
    }

    private AudioSource CreateMusicSource(string sourceName)
    {
        GameObject child = new GameObject(sourceName);
        child.transform.SetParent(transform, false);
        AudioSource src = child.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = 0f;
        src.loop = true;
        src.volume = 0f;
        return src;
    }

    private void Start()
    {
        EvaluateAndSwitchMusic(true);
    }

    private void Update()
    {
        if (isPausedByMenu) return;

        // Track AudioListener.volume automatically in case it was modified externally
        if (Mathf.Abs(AudioListener.volume - currentSfxListenerVolume) > 0.0005f)
        {
            currentSfxListenerVolume = Mathf.Clamp01(AudioListener.volume);
            UpdateSfxOffsetBoostFactor();
        }

        EvaluateAndSwitchMusic(false);
        UpdateCrossfadeAndPitch();
    }

    // =========================================================================
    // SETTINGS SLIDER & PAUSE API
    // =========================================================================

    /// <summary>
    /// Called by the Music Slider in the Pause/Settings menu.
    /// </summary>
    public void SetMusicSliderVolume(float volume01)
    {
        musicSliderVolume = Mathf.Clamp01(volume01);
        if (!isPausedByMenu)
        {
            ApplyCurrentVolumesToSources();
        }
    }

    /// <summary>
    /// Called by the SFX ("Every Other Sound") Slider in the Pause/Settings menu.
    /// Edits AudioListener.volume and secretly boosts music to offset the listener change.
    /// </summary>
    public void SetSfxListenerVolume(float listenerVolume01)
    {
        currentSfxListenerVolume = Mathf.Clamp01(listenerVolume01);
        AudioListener.volume = currentSfxListenerVolume;
        UpdateSfxOffsetBoostFactor();

        if (!isPausedByMenu)
        {
            ApplyCurrentVolumesToSources();
        }
    }

    /// <summary>
    /// Called by PauseManager when pausing/resuming the game so music is completely silent during pause
    /// and resumes cleanly at the configured slider volume when unpaused.
    /// </summary>
    public void SetPausedState(bool paused)
    {
        if (isPausedByMenu == paused) return;
        isPausedByMenu = paused;

        if (isPausedByMenu)
        {
            if (activeSource != null)
            {
                if (activeSource.isPlaying) activeSource.Pause();
                activeSource.mute = true;
            }
            if (fadingOutSource != null)
            {
                if (fadingOutSource.isPlaying) fadingOutSource.Pause();
                fadingOutSource.mute = true;
            }
        }
        else
        {
            if (activeSource != null)
            {
                activeSource.mute = false;
                if (activeSource.clip != null) activeSource.UnPause();
            }
            if (fadingOutSource != null)
            {
                fadingOutSource.mute = false;
                if (fadingOutSource.clip != null && fadingOutWeight > 0.001f) fadingOutSource.UnPause();
            }
            ApplyCurrentVolumesToSources();
        }
    }

    private void UpdateSfxOffsetBoostFactor()
    {
        sfxOffsetMusicBoost = currentSfxListenerVolume > 0.0001f
            ? (1f / currentSfxListenerVolume)
            : 1f;
    }

    // =========================================================================
    // AUTOMATIC TRACK VOLUME NORMALIZATION
    // =========================================================================

    private void SyncOrCalculateTrackNormalizations(bool forceReanalyze)
    {
        List<AudioClip> allAssignedClips = GatherAllAssignedClips();
        runtimeNormalizationCache.Clear();

        for (int i = 0; i < allAssignedClips.Count; i++)
        {
            AudioClip clip = allAssignedClips[i];
            if (clip == null) continue;

            int existingIdx = FindNormalizationEntryIndex(clip);
            if (existingIdx >= 0 && !forceReanalyze && trackNormalizations[existingIdx].measuredRms > 0.0001f)
            {
                TrackNormalizationEntry entry = trackNormalizations[existingIdx];
                entry.trackName = clip.name;
                entry.volumeMultiplier = ComputeMultiplierFromRmsAndPeak(entry.measuredRms, entry.measuredPeak);
                trackNormalizations[existingIdx] = entry;
                runtimeNormalizationCache[clip] = entry.volumeMultiplier;
            }
            else
            {
                if (TryAnalyzeClipLoudness(clip, out float rms, out float peak))
                {
                    float mult = ComputeMultiplierFromRmsAndPeak(rms, peak);
                    TrackNormalizationEntry newEntry = new TrackNormalizationEntry
                    {
                        trackName = clip.name,
                        clip = clip,
                        measuredRms = rms,
                        measuredPeak = peak,
                        volumeMultiplier = mult
                    };

                    if (existingIdx >= 0)
                        trackNormalizations[existingIdx] = newEntry;
                    else
                        trackNormalizations.Add(newEntry);

                    runtimeNormalizationCache[clip] = mult;
                }
                else if (existingIdx >= 0 && trackNormalizations[existingIdx].volumeMultiplier > 0.01f)
                {
                    runtimeNormalizationCache[clip] = trackNormalizations[existingIdx].volumeMultiplier;
                }
                else
                {
                    runtimeNormalizationCache[clip] = 1.0f;
                }
            }
        }
    }

    private int FindNormalizationEntryIndex(AudioClip clip)
    {
        if (trackNormalizations == null || clip == null) return -1;
        for (int i = 0; i < trackNormalizations.Count; i++)
        {
            if (trackNormalizations[i].clip == clip)
                return i;
        }
        return -1;
    }

    private List<AudioClip> GatherAllAssignedClips()
    {
        List<AudioClip> clips = new List<AudioClip>();
        void AddUnique(AudioClip c)
        {
            if (c != null && !clips.Contains(c)) clips.Add(c);
        }

        if (baseMusicPlaylist != null)
        {
            for (int i = 0; i < baseMusicPlaylist.Count; i++) AddUnique(baseMusicPlaylist[i]);
        }

        AddUnique(hungerTheme);
        AddUnique(needsTheme);
        AddUnique(sleepTheme);
        AddUnique(dirtinessTheme);
        AddUnique(depressionTheme);

        if (extremeMusicTracks != null)
        {
            for (int i = 0; i < extremeMusicTracks.Count; i++) AddUnique(extremeMusicTracks[i]);
        }

        AddUnique(drunkMusicTrack);
        AddUnique(caffeinatedMusicTrack);
        AddUnique(highMusicTrack);
        AddUnique(nicotinedMusicTrack);

        AddUnique(hyperfocusComboTrack);
        AddUnique(heartAttackComboTrack);
        AddUnique(hanyingerComboTrack);
        AddUnique(anxietyAttackComboTrack);
        AddUnique(dizzyBuzzComboTrack);
        AddUnique(threeEffectOverdoseTrack);

        return clips;
    }

    private float ComputeMultiplierFromRmsAndPeak(float rms, float peak)
    {
        if (rms <= 0.0001f) return 1.0f;

        float rawGain = targetNormalizedRms / Mathf.Max(0.005f, rms);

        // Soft-limit against the clip's peak so boosting quiet tracks doesn't cause harsh digital clipping
        if (peak > 0.05f && rawGain * peak > 1.0f)
        {
            float maxUncappedGain = 1.0f / peak;
            rawGain = Mathf.Lerp(rawGain, maxUncappedGain, 0.45f);
        }

        return Mathf.Clamp(rawGain, minNormalizationMultiplier, maxNormalizationMultiplier);
    }

    private bool TryAnalyzeClipLoudness(AudioClip clip, out float rms, out float peak)
    {
        rms = 0f;
        peak = 0f;
        if (clip == null) return false;

        try
        {
            if (clip.loadState == AudioDataLoadState.Unloaded)
            {
                clip.LoadAudioData();
            }

            if (clip.loadState != AudioDataLoadState.Loaded || clip.samples <= 0)
            {
                return false;
            }

            int totalFrames = clip.samples;
            int channels = Mathf.Max(1, clip.channels);

            const int windowCount = 64;
            const int windowFrames = 512;
            int actualWindows = Mathf.Min(windowCount, Mathf.Max(1, totalFrames / windowFrames));
            float[] buffer = new float[windowFrames * channels];

            double sumSquaresActive = 0.0;
            long activeSampleCount = 0;
            double sumSquaresAll = 0.0;
            long allSampleCount = 0;
            float maxAbs = 0f;

            for (int w = 0; w < actualWindows; w++)
            {
                int frameOffset = actualWindows > 1
                    ? (int)((long)w * (totalFrames - windowFrames) / (actualWindows - 1))
                    : 0;
                frameOffset = Mathf.Clamp(frameOffset, 0, Mathf.Max(0, totalFrames - windowFrames));

                if (!clip.GetData(buffer, frameOffset))
                {
                    return false;
                }

                double windowSumSq = 0.0;
                for (int s = 0; s < buffer.Length; s++)
                {
                    float sample = buffer[s];
                    float abs = Mathf.Abs(sample);
                    if (abs > maxAbs) maxAbs = abs;
                    double sq = (double)sample * sample;
                    windowSumSq += sq;
                }

                sumSquaresAll += windowSumSq;
                allSampleCount += buffer.Length;

                double windowRms = System.Math.Sqrt(windowSumSq / buffer.Length);
                // Ignore near-silent intro/outro windows so silence doesn't artificially inflate a track's gain
                if (windowRms > 0.008)
                {
                    sumSquaresActive += windowSumSq;
                    activeSampleCount += buffer.Length;
                }
            }

            if (activeSampleCount > 0)
            {
                rms = (float)System.Math.Sqrt(sumSquaresActive / activeSampleCount);
            }
            else if (allSampleCount > 0)
            {
                rms = (float)System.Math.Sqrt(sumSquaresAll / allSampleCount);
            }

            peak = maxAbs;
            return rms > 0.0001f;
        }
        catch
        {
            return false;
        }
    }

    public float GetClipNormalizationMultiplier(AudioClip clip)
    {
        if (!normalizeTrackVolumes || clip == null) return 1.0f;

        if (runtimeNormalizationCache.TryGetValue(clip, out float cached))
        {
            return cached;
        }

        int existingIdx = FindNormalizationEntryIndex(clip);
        if (existingIdx >= 0 && trackNormalizations[existingIdx].measuredRms > 0.0001f)
        {
            float mult = ComputeMultiplierFromRmsAndPeak(
                trackNormalizations[existingIdx].measuredRms,
                trackNormalizations[existingIdx].measuredPeak);
            runtimeNormalizationCache[clip] = mult;
            return mult;
        }

        if (TryAnalyzeClipLoudness(clip, out float rms, out float peak))
        {
            float mult = ComputeMultiplierFromRmsAndPeak(rms, peak);
            runtimeNormalizationCache[clip] = mult;
            trackNormalizations.Add(new TrackNormalizationEntry
            {
                trackName = clip.name,
                clip = clip,
                measuredRms = rms,
                measuredPeak = peak,
                volumeMultiplier = mult
            });
            return mult;
        }

        runtimeNormalizationCache[clip] = 1.0f;
        return 1.0f;
    }

    /// <summary>
    /// Computes the desired speaker output volume for a given clip before SFX AudioListener compensation.
    /// </summary>
    private float GetUncompensatedClipVolume(AudioClip clip)
    {
        float normMult = GetClipNormalizationMultiplier(clip);
        return Mathf.Clamp01(masterMusicVolume * musicSliderVolume * normMult);
    }

    /// <summary>
    /// Applies the desired music volume to the given AudioSource while secretly boosting it by
    /// <see cref="sfxOffsetMusicBoost"/> (1 / AudioListener.volume) to offset the SFX slider.
    /// If the required boost exceeds Unity's 1.0 AudioSource ceiling (e.g. when SFX is near 0),
    /// seamlessly enables ignoreListenerVolume so music never drops or mutes.
    /// </summary>
    private void ApplyVolumeWithSfxOffset(AudioSource source, float desiredSpeakerVolume)
    {
        if (source == null) return;

        desiredSpeakerVolume = Mathf.Clamp01(desiredSpeakerVolume);
        UpdateSfxOffsetBoostFactor();

        float boostedSourceVolume = desiredSpeakerVolume * sfxOffsetMusicBoost;

        if (currentSfxListenerVolume > 0.001f && boostedSourceVolume <= 1.0f)
        {
            // Secretly boost AudioSource.volume to directly offset AudioListener.volume
            source.ignoreListenerVolume = false;
            source.volume = Mathf.Clamp01(boostedSourceVolume);
        }
        else
        {
            // When SFX slider is lowered past the point where a 1.0 AudioSource volume can compensate (e.g., SFX = 0),
            // bypass AudioListener.volume so music still plays at its exact target volume.
            source.ignoreListenerVolume = true;
            source.volume = desiredSpeakerVolume;
        }
    }

    private void ApplyCurrentVolumesToSources()
    {
        if (activeSource != null)
        {
            float targetVol = activeFadeWeight * GetUncompensatedClipVolume(activeSource.clip);
            ApplyVolumeWithSfxOffset(activeSource, targetVol);
        }
        if (fadingOutSource != null)
        {
            float fadeOutVol = fadingOutWeight * GetUncompensatedClipVolume(fadingOutSource.clip);
            ApplyVolumeWithSfxOffset(fadingOutSource, fadeOutVol);
        }
    }

    // =========================================================================
    // MUSIC SELECTION & CROSSFADING
    // =========================================================================

    private void EvaluateAndSwitchMusic(bool immediate)
    {
        ActiveSubstanceComboState comboState = SubstanceCombinationManager.Instance != null
            ? SubstanceCombinationManager.Instance.CurrentComboState
            : ActiveSubstanceComboState.None;

        // 1. 3-Effect Overdose always takes top priority if a track is assigned
        if (comboState == ActiveSubstanceComboState.SuperOverdose_ThreeEffects && threeEffectOverdoseTrack != null)
        {
            TransitionToClip(threeEffectOverdoseTrack, true, immediate);
            return;
        }

        AudioClip desiredSubstanceOrComboClip = GetDesiredSubstanceOrComboClip(comboState);

        // 2. If substance priority is OverrideEverything, play the combo/substance track
        if (substancePriority == SubstanceMusicPriority.OverrideEverything && desiredSubstanceOrComboClip != null)
        {
            TransitionToClip(desiredSubstanceOrComboClip, true, immediate);
            return;
        }

        // 3. Evaluate BarManager percentages
        AudioClip desiredBarClip = EvaluateBarMusic();
        if (desiredBarClip != null)
        {
            TransitionToClip(desiredBarClip, true, immediate);
            return;
        }

        // 4. If no bars are critical, check if a combo or single substance track should override Base music
        if (substancePriority == SubstanceMusicPriority.OverrideBaseMusicOnly && desiredSubstanceOrComboClip != null)
        {
            TransitionToClip(desiredSubstanceOrComboClip, true, immediate);
            return;
        }

        // 5. Otherwise, play / cycle through the Base Music Playlist
        PlayOrAdvanceBasePlaylist(immediate);
    }

    private AudioClip GetDesiredSubstanceOrComboClip(ActiveSubstanceComboState comboState)
    {
        if (substancePriority == SubstanceMusicPriority.PitchModulationOnly) return null;

        // First check active 2-substance or 3-substance combinations
        switch (comboState)
        {
            case ActiveSubstanceComboState.Hyperfocus_NicotineCaffeine:
                if (hyperfocusComboTrack != null) return hyperfocusComboTrack;
                break;
            //case ActiveSubstanceSubstanceCombo_HeartAttack:
            case ActiveSubstanceComboState.HeartAttack_AlcoholCaffeine:
                if (heartAttackComboTrack != null) return heartAttackComboTrack;
                break;
            case ActiveSubstanceComboState.Hanyinger_AlcoholWeed:
                if (hanyingerComboTrack != null) return hanyingerComboTrack;
                break;
            case ActiveSubstanceComboState.AnxietyAttack_WeedNicOrCaff:
                if (anxietyAttackComboTrack != null) return anxietyAttackComboTrack;
                break;
            case ActiveSubstanceComboState.DizzyBuzz_AlcoholNicotine:
                if (dizzyBuzzComboTrack != null) return dizzyBuzzComboTrack;
                break;
            case ActiveSubstanceComboState.SuperOverdose_ThreeEffects:
                if (threeEffectOverdoseTrack != null) return threeEffectOverdoseTrack;
                break;
        }

        // Otherwise check single active substances
        float dInt = TimedConsumableBooster.GetEffectIntensity(ConsumableVisualEffectType.Drunk);
        float cInt = TimedConsumableBooster.GetEffectIntensity(ConsumableVisualEffectType.Caffeinated);
        float hInt = TimedConsumableBooster.GetEffectIntensity(ConsumableVisualEffectType.High);
        float nInt = TimedConsumableBooster.GetEffectIntensity(ConsumableVisualEffectType.Nicotined);

        AudioClip bestClip = null;
        float bestVal = 0.05f;

        if (drunkMusicTrack != null && dInt >= bestVal) { bestVal = dInt; bestClip = drunkMusicTrack; }
        if (caffeinatedMusicTrack != null && cInt >= bestVal) { bestVal = cInt; bestClip = caffeinatedMusicTrack; }
        if (highMusicTrack != null && hInt >= bestVal) { bestVal = hInt; bestClip = highMusicTrack; }
        if (nicotinedMusicTrack != null && nInt >= bestVal) { bestVal = nInt; bestClip = nicotinedMusicTrack; }

        return bestClip;
    }

    private const ActiveSubstanceComboState ActiveSubstanceSubstanceCombo_HeartAttack = ActiveSubstanceComboState.HeartAttack_AlcoholCaffeine;

    private AudioClip EvaluateBarMusic()
    {
        if (barManager == null) return null;

        float effectiveThreshold = isInBarCrisisState
            ? Mathf.Max(0.05f, barTriggerPercentage - barReleaseMargin)
            : barTriggerPercentage;

        float hungerPct = SafeRatio(barManager.currentHunger, barManager.maxHunger);
        float needsPct = SafeRatio(barManager.currentNeeds, barManager.maxNeeds);
        float sleepPct = SafeRatio(barManager.currentSleep, barManager.maxSleep);
        float dirtinessPct = SafeRatio(barManager.currentDirtiness, barManager.maxDirtiness);
        float depressionPct = SafeRatio(barManager.currentDepression, barManager.maxDepression);

        int criticalCount = 0;
        float highestPct = -1f;
        AudioClip highestBarTheme = null;

        CheckBar(hungerPct, effectiveThreshold, hungerTheme, ref criticalCount, ref highestPct, ref highestBarTheme);
        CheckBar(needsPct, effectiveThreshold, needsTheme, ref criticalCount, ref highestPct, ref highestBarTheme);
        CheckBar(sleepPct, effectiveThreshold, sleepTheme, ref criticalCount, ref highestPct, ref highestBarTheme);
        CheckBar(dirtinessPct, effectiveThreshold, dirtinessTheme, ref criticalCount, ref highestPct, ref highestBarTheme);
        CheckBar(depressionPct, effectiveThreshold, depressionTheme, ref criticalCount, ref highestPct, ref highestBarTheme);

        isInBarCrisisState = criticalCount > 0;

        if (criticalCount == 0) return null;

        if (criticalCount >= multipleBarsCountForExtreme && extremeMusicTracks != null && extremeMusicTracks.Count > 0)
        {
            if (activeSource.clip != null && extremeMusicTracks.Contains(activeSource.clip))
            {
                return activeSource.clip;
            }
            return extremeMusicTracks[0];
        }

        return highestBarTheme;
    }

    private void CheckBar(
        float pct,
        float threshold,
        AudioClip theme,
        ref int criticalCount,
        ref float highestPct,
        ref AudioClip highestTheme)
    {
        if (pct >= threshold)
        {
            criticalCount++;
            if (theme != null && pct > highestPct)
            {
                highestPct = pct;
                highestTheme = theme;
            }
        }
    }

    private float SafeRatio(float current, float max)
    {
        return max > 0.0001f ? Mathf.Clamp01(current / max) : 0f;
    }

    private void PlayOrAdvanceBasePlaylist(bool immediate)
    {
        if (baseMusicPlaylist == null || baseMusicPlaylist.Count == 0) return;

        bool isPlayingBaseTrack = activeSource.clip != null && baseMusicPlaylist.Contains(activeSource.clip);

        if (isPlayingBaseTrack && activeSource.isPlaying)
        {
            activeSource.loop = (baseMusicPlaylist.Count == 1);
            return;
        }

        if (shuffleBasePlaylist && baseMusicPlaylist.Count > 1)
        {
            int next = Random.Range(0, baseMusicPlaylist.Count);
            if (next == currentBasePlaylistIndex) next = (next + 1) % baseMusicPlaylist.Count;
            currentBasePlaylistIndex = next;
        }
        else
        {
            currentBasePlaylistIndex = (currentBasePlaylistIndex + 1) % baseMusicPlaylist.Count;
        }

        AudioClip nextBaseClip = baseMusicPlaylist[currentBasePlaylistIndex];
        TransitionToClip(nextBaseClip, baseMusicPlaylist.Count == 1, immediate);
    }

    private void TransitionToClip(AudioClip newClip, bool loopClip, bool immediate)
    {
        if (newClip == null) return;

        if (activeSource.clip == newClip && activeSource.isPlaying)
        {
            activeSource.loop = loopClip;
            return;
        }

        AudioSource temp = fadingOutSource;
        fadingOutSource = activeSource;
        activeSource = temp;

        fadingOutWeight = activeFadeWeight;
        activeFadeWeight = 0f;

        activeSource.clip = newClip;
        activeSource.loop = loopClip;
        activeSource.Play();

        if (immediate || crossfadeDuration <= 0.01f)
        {
            activeFadeWeight = 1f;
            fadingOutWeight = 0f;
            fadingOutSource.volume = 0f;
            fadingOutSource.Stop();
            ApplyCurrentVolumesToSources();
        }
    }

    private void UpdateCrossfadeAndPitch()
    {
        float dt = Time.unscaledDeltaTime;
        float fadeRate = 1f / Mathf.Max(0.05f, crossfadeDuration);

        if (activeSource.isPlaying)
        {
            activeFadeWeight = Mathf.MoveTowards(activeFadeWeight, 1f, fadeRate * dt);
        }

        if (fadingOutSource.isPlaying)
        {
            fadingOutWeight = Mathf.MoveTowards(fadingOutWeight, 0f, fadeRate * dt);
            if (fadingOutWeight <= 0.001f)
            {
                fadingOutWeight = 0f;
                fadingOutSource.volume = 0f;
                fadingOutSource.Stop();
            }
        }

        ApplyCurrentVolumesToSources();

        float targetPitch = 1.0f;
        if (enableSubstancePitchModulation)
        {
            ActiveSubstanceComboState combo = SubstanceCombinationManager.Instance != null
                ? SubstanceCombinationManager.Instance.CurrentComboState
                : ActiveSubstanceComboState.None;

            float t = Time.unscaledTime;

            switch (combo)
            {
                case ActiveSubstanceComboState.Hyperfocus_NicotineCaffeine:
                    // Crisp, locked-in fast tempo
                    targetPitch = 1.16f;
                    break;

                case ActiveSubstanceComboState.HeartAttack_AlcoholCaffeine:
                    // Thumping, erratic slowed/fluttering heart-attack tempo
                    float flutter = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * 16f)), 8f) * 0.22f;
                    targetPitch = 0.82f + flutter;
                    break;

                case ActiveSubstanceComboState.Hanyinger_AlcoholWeed:
                    // Deep, seasick pitch lurching up and down
                    targetPitch = 0.85f + Mathf.Sin(t * 1.6f) * 0.14f;
                    break;

                case ActiveSubstanceComboState.AnxietyAttack_WeedNicOrCaff:
                    // Tense, jittery high-strung pitch
                    targetPitch = 1.08f + Mathf.Sin(t * 28f) * 0.04f;
                    break;

                case ActiveSubstanceComboState.DizzyBuzz_AlcoholNicotine:
                    targetPitch = 0.94f + Mathf.Sin(t * 3.0f) * 0.06f;
                    break;

                case ActiveSubstanceComboState.SuperOverdose_ThreeEffects:
                    // Reality-tearing warble that drags down into pitch black
                    targetPitch = 0.72f + Mathf.Sin(t * 9.0f) * 0.25f;
                    break;

                case ActiveSubstanceComboState.None:
                    // Single-substance pitch modulation
                    float drunkInt = TimedConsumableBooster.GetEffectIntensity(ConsumableVisualEffectType.Drunk);
                    float caffInt = TimedConsumableBooster.GetEffectIntensity(ConsumableVisualEffectType.Caffeinated);
                    float highInt = TimedConsumableBooster.GetEffectIntensity(ConsumableVisualEffectType.High);
                    float nicInt = TimedConsumableBooster.GetEffectIntensity(ConsumableVisualEffectType.Nicotined);

                    if (caffInt > 0.01f) targetPitch *= Mathf.Lerp(1f, caffeinePitchMultiplier, caffInt);
                    if (highInt > 0.01f) targetPitch *= Mathf.Lerp(1f, highPitchMultiplier, highInt);
                    if (nicInt > 0.01f) targetPitch *= Mathf.Lerp(1f, nicotinePitchMultiplier, nicInt);
                    if (drunkInt > 0.01f)
                    {
                        float wobble = Mathf.Sin(t * 2.2f) * drunkPitchWobbleAmount;
                        targetPitch *= Mathf.Lerp(1f, drunkBasePitch + wobble, drunkInt);
                    }
                    break;
            }
        }

        activeSource.pitch = Mathf.Lerp(activeSource.pitch, targetPitch, 1f - Mathf.Exp(-8f * dt));
        fadingOutSource.pitch = activeSource.pitch;
    }
}
