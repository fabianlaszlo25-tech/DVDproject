using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dynamic Music Manager that listens to <see cref="BarManager"/>, single consumable effects,
/// and all 2-substance / 3-substance combinations from <see cref="SubstanceCombinationManager"/>.
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

    private AudioSource activeSource;
    private AudioSource fadingOutSource;
    private int currentBasePlaylistIndex = -1;
    private bool isInBarCrisisState = false;

    private void Awake()
    {
        if (barManager == null) barManager = FindObjectOfType<BarManager>();

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
        EvaluateAndSwitchMusic(false);
        UpdateCrossfadeAndPitch();
    }

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

        activeSource.clip = newClip;
        activeSource.loop = loopClip;
        activeSource.Play();

        if (immediate || crossfadeDuration <= 0.01f)
        {
            activeSource.volume = masterMusicVolume;
            fadingOutSource.volume = 0f;
            fadingOutSource.Stop();
        }
    }

    private void UpdateCrossfadeAndPitch()
    {
        float dt = Time.unscaledDeltaTime;
        float fadeSpeed = masterMusicVolume / Mathf.Max(0.05f, crossfadeDuration);

        if (activeSource.isPlaying)
        {
            activeSource.volume = Mathf.MoveTowards(activeSource.volume, masterMusicVolume, fadeSpeed * dt);
        }

        if (fadingOutSource.isPlaying)
        {
            fadingOutSource.volume = Mathf.MoveTowards(fadingOutSource.volume, 0f, fadeSpeed * dt);
            if (fadingOutSource.volume <= 0.001f)
            {
                fadingOutSource.Stop();
            }
        }

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