using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dynamic Music Manager that listens to <see cref="BarManager"/> and active consumable effects
/// (Drunk, Caffeinated, High, Nicotined):
/// 1. Base Playlist: Cycles through a list of base music tracks when all bars are below threshold.
/// 2. Single Bar Threshold: When a bar exceeds its threshold percentage (e.g. 70%), its theme plays.
///    If multiple bars exceed threshold, the HIGHEST filled bar takes priority—UNLESS the count of
///    critical bars reaches <see cref="multipleBarsCountForExtreme"/> (default 2), in which case
///    the Extreme / Multi-Bar Crisis Music takes over!
/// 3. Substance States (Drunk / Caffeinated / High / Nicotined): Can either play dedicated substance
///    music tracks OR dynamically warp the pitch/tempo of the currently playing music!
/// </summary>
public class DynamicMusicManager : MonoBehaviour
{
    public enum SubstanceMusicPriority
    {
        [Tooltip("Substance music overrides Base music, but High/Extreme Bar themes still take priority during emergencies.")]
        OverrideBaseMusicOnly,
        [Tooltip("Substance music overrides everything while you are under the influence.")]
        OverrideEverything,
        [Tooltip("Keep playing the Bar/Base music, and only apply the Drunk/Caffeine/High/Nicotine pitch & tempo modulation.")]
        PitchModulationOnly
    }

    [Header("Core References (Auto-filled if left empty)")]
    public BarManager barManager;

    [Header("Disable Old Music Handling")]
    [Tooltip("Drag any old AudioSources or objects that previously played background music here so they are automatically stopped/disabled.")]
    public List<AudioSource> legacyMusicSourcesToDisable = new List<AudioSource>();
    public List<MonoBehaviour> legacyMusicScriptsToDisable = new List<MonoBehaviour>();

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
    [Tooltip("How many bars must be above barTriggerPercentage at the same time to trigger the Extreme music (2 = any 2 or more bars).")]
    [Min(2)]
    public int multipleBarsCountForExtreme = 2;
    [Tooltip("Played when multiple bars are critically high at once (can assign 1 or multiple tracks).")]
    public List<AudioClip> extremeMusicTracks = new List<AudioClip>();

    [Header("4. Consumable Effect Music & Audio Warping")]
    public SubstanceMusicPriority substancePriority = SubstanceMusicPriority.OverrideBaseMusicOnly;
    [Tooltip("Optional dedicated track when Drunk (leave empty to just warp the current music's pitch/wobble).")]
    public AudioClip drunkMusicTrack;
    [Tooltip("Optional dedicated track when Caffeinated (Energy Drink).")]
    public AudioClip caffeinatedMusicTrack;
    [Tooltip("Optional dedicated track when High.")]
    public AudioClip highMusicTrack;
    [Tooltip("Optional dedicated track when Nicotined.")]
    public AudioClip nicotinedMusicTrack;
    [Tooltip("Optional intense track if multiple substances are active at the same time!")]
    public AudioClip mixedSubstanceMusicTrack;

    [Header("Substance Pitch / Tempo Modulation (Applies to any active music)")]
    [Tooltip("If true, being Drunk/Caffeinated/High/Nicotined subtly warps the music pitch & tempo.")]
    public bool enableSubstancePitchModulation = true;
    [Tooltip("Caffeine speeds up music pitch (e.g. 1.12 = +12% faster).")]
    public float caffeinePitchMultiplier = 1.12f;
    [Tooltip("High slows down music into a chill vibe (e.g. 0.88 = -12% slower).")]
    public float highPitchMultiplier = 0.88f;
    [Tooltip("Drunk adds a slow, woozy pitch wobble around 0.95x.")]
    public float drunkBasePitch = 0.95f;
    public float drunkPitchWobbleAmount = 0.045f;
    [Tooltip("Nicotine adds a subtle crisp pitch lift (e.g. 1.04x).")]
    public float nicotinePitchMultiplier = 1.04f;

    [Header("Crossfade & Volume Settings")]
    [Range(0f, 1f)] public float masterMusicVolume = 0.8f;
    [Tooltip("Seconds to smoothly crossfade when switching between themes.")]
    public float crossfadeDuration = 2.0f;

    // Optional direct references if triggering effects outside TimedConsumableBooster
    private DrunkScreenEffect drunkEffect;
    private CaffeineScreenEffect caffeineEffect;
    private HighScreenEffect highEffect;
    private NicotineScreenEffect nicotineEffect;

    private AudioSource activeSource;
    private AudioSource fadingOutSource;
    private int currentBasePlaylistIndex = -1;
    private bool isInBarCrisisState = false;

    private void Awake()
    {
        if (barManager == null) barManager = FindObjectOfType<BarManager>();

        // Disable any legacy music scripts/sources so this manager has sole control
        foreach (MonoBehaviour script in legacyMusicScriptsToDisable)
        {
            if (script != null) script.enabled = false;
        }
        foreach (AudioSource oldSource in legacyMusicSourcesToDisable)
        {
            if (oldSource != null)
            {
                oldSource.Stop();
                oldSource.enabled = false;
            }
        }

        // Create two internal AudioSources for seamless crossfading
        activeSource = CreateMusicSource("MusicChannel_A");
        fadingOutSource = CreateMusicSource("MusicChannel_B");
    }

    private AudioSource CreateMusicSource(string sourceName)
    {
        GameObject child = new GameObject(sourceName);
        child.transform.SetParent(transform, false);
        AudioSource src = child.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = 0f; // 2D fullscreen music
        src.loop = true;
        src.volume = 0f;
        return src;
    }

    private void Start()
    {
        RefreshEffectReferences();
        EvaluateAndSwitchMusic(true);
    }

    private void Update()
    {
        EvaluateAndSwitchMusic(false);
        UpdateCrossfadeAndPitch();
    }

    private void RefreshEffectReferences()
    {
        if (drunkEffect == null) drunkEffect = FindObjectOfType<DrunkScreenEffect>();
        if (caffeineEffect == null) caffeineEffect = FindObjectOfType<CaffeineScreenEffect>();
        if (highEffect == null) highEffect = FindObjectOfType<HighScreenEffect>();
        if (nicotineEffect == null) nicotineEffect = FindObjectOfType<NicotineScreenEffect>();
    }

    private void EvaluateAndSwitchMusic(bool immediate)
    {
        // 1. Check active consumable states (via TimedConsumableBooster OR standalone screen effects)
        bool isDrunk = TimedConsumableBooster.IsEffectActive(ConsumableVisualEffectType.Drunk)
                    || (drunkEffect != null && drunkEffect.enabled && drunkEffect.gameObject.activeInHierarchy && TimedConsumableBooster.GetEffectIntensity(ConsumableVisualEffectType.Drunk) > 0.05f);
        bool isCaff = TimedConsumableBooster.IsEffectActive(ConsumableVisualEffectType.Caffeinated)
                    || (caffeineEffect != null && caffeineEffect.enabled && caffeineEffect.gameObject.activeInHierarchy && TimedConsumableBooster.GetEffectIntensity(ConsumableVisualEffectType.Caffeinated) > 0.05f);
        bool isHigh = TimedConsumableBooster.IsEffectActive(ConsumableVisualEffectType.High)
                    || (highEffect != null && highEffect.CurrentIntensity > 0.05f);
        bool isNic = TimedConsumableBooster.IsEffectActive(ConsumableVisualEffectType.Nicotined)
                    || (nicotineEffect != null && nicotineEffect.CurrentIntensity > 0.05f);

        AudioClip desiredSubstanceClip = GetDesiredSubstanceClip(isDrunk, isCaff, isHigh, isNic);

        // 2. If substance priority is set to OverrideEverything and we have a substance track, play it!
        if (substancePriority == SubstanceMusicPriority.OverrideEverything && desiredSubstanceClip != null)
        {
            TransitionToClip(desiredSubstanceClip, true, immediate);
            return;
        }

        // 3. Evaluate BarManager percentages
        AudioClip desiredBarClip = EvaluateBarMusic();
        if (desiredBarClip != null)
        {
            TransitionToClip(desiredBarClip, true, immediate);
            return;
        }

        // 4. If no bars are critical, check if a substance track should override Base music
        if (substancePriority == SubstanceMusicPriority.OverrideBaseMusicOnly && desiredSubstanceClip != null)
        {
            TransitionToClip(desiredSubstanceClip, true, immediate);
            return;
        }

        // 5. Otherwise, play / cycle through the Base Music Playlist!
        PlayOrAdvanceBasePlaylist(immediate);
    }

    private AudioClip GetDesiredSubstanceClip(bool isDrunk, bool isCaff, bool isHigh, bool isNic)
    {
        if (substancePriority == SubstanceMusicPriority.PitchModulationOnly) return null;

        int activeCount = (isDrunk ? 1 : 0) + (isCaff ? 1 : 0) + (isHigh ? 1 : 0) + (isNic ? 1 : 0);
        if (activeCount >= 2 && mixedSubstanceMusicTrack != null)
        {
            return mixedSubstanceMusicTrack;
        }

        // Pick whichever substance has the highest current intensity
        float dInt = TimedConsumableBooster.GetEffectIntensity(ConsumableVisualEffectType.Drunk);
        float cInt = TimedConsumableBooster.GetEffectIntensity(ConsumableVisualEffectType.Caffeinated);
        float hInt = TimedConsumableBooster.GetEffectIntensity(ConsumableVisualEffectType.High);
        float nInt = TimedConsumableBooster.GetEffectIntensity(ConsumableVisualEffectType.Nicotined);

        AudioClip bestClip = null;
        float bestVal = 0.05f;

        if (isDrunk && drunkMusicTrack != null && dInt >= bestVal) { bestVal = dInt; bestClip = drunkMusicTrack; }
        if (isCaff && caffeinatedMusicTrack != null && cInt >= bestVal) { bestVal = cInt; bestClip = caffeinatedMusicTrack; }
        if (isHigh && highMusicTrack != null && hInt >= bestVal) { bestVal = hInt; bestClip = highMusicTrack; }
        if (isNic && nicotinedMusicTrack != null && nInt >= bestVal) { bestVal = nInt; bestClip = nicotinedMusicTrack; }

        return bestClip;
    }

    private AudioClip EvaluateBarMusic()
    {
        if (barManager == null) return null;

        // Use hysteresis so the music doesn't rapid-toggle at the exact threshold boundary
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

        if (criticalCount == 0)
        {
            return null;
        }

        // If MULTIPLE bars are above the threshold at the same time, play Extreme Music!
        if (criticalCount >= multipleBarsCountForExtreme && extremeMusicTracks != null && extremeMusicTracks.Count > 0)
        {
            // Keep playing the current extreme track if one is already active, otherwise pick the first/random
            if (activeSource.clip != null && extremeMusicTracks.Contains(activeSource.clip))
            {
                return activeSource.clip;
            }
            return extremeMusicTracks[0];
        }

        // Otherwise, the single HIGHEST filled bar takes priority!
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

        // Base playlist tracks don't loop on a single track if there are multiple tracks in the list:
        // when one finishes, it automatically advances to the next track in the list!
        bool isPlayingBaseTrack = activeSource.clip != null && baseMusicPlaylist.Contains(activeSource.clip);

        if (isPlayingBaseTrack && activeSource.isPlaying)
        {
            activeSource.loop = (baseMusicPlaylist.Count == 1);
            return;
        }

        // Pick next track from baseMusicPlaylist
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

        // Swap channels so the old active channel fades out while the new one fades in
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

        // Calculate dynamic pitch modulation from active substances (Drunk / Caffeinated / High / Nicotined)
        float targetPitch = 1.0f;
        if (enableSubstancePitchModulation)
        {
            float drunkInt = TimedConsumableBooster.GetEffectIntensity(ConsumableVisualEffectType.Drunk);
            float caffInt = TimedConsumableBooster.GetEffectIntensity(ConsumableVisualEffectType.Caffeinated);
            float highInt = TimedConsumableBooster.GetEffectIntensity(ConsumableVisualEffectType.High);
            float nicInt = TimedConsumableBooster.GetEffectIntensity(ConsumableVisualEffectType.Nicotined);

            if (caffInt > 0.01f)
            {
                targetPitch *= Mathf.Lerp(1f, caffeinePitchMultiplier, caffInt);
            }
            if (highInt > 0.01f)
            {
                targetPitch *= Mathf.Lerp(1f, highPitchMultiplier, highInt);
            }
            if (nicInt > 0.01f)
            {
                targetPitch *= Mathf.Lerp(1f, nicotinePitchMultiplier, nicInt);
            }
            if (drunkInt > 0.01f)
            {
                float wobble = Mathf.Sin(Time.unscaledTime * 2.2f) * drunkPitchWobbleAmount;
                targetPitch *= Mathf.Lerp(1f, drunkBasePitch + wobble, drunkInt);
            }
        }

        activeSource.pitch = Mathf.Lerp(activeSource.pitch, targetPitch, 1f - Mathf.Exp(-8f * dt));
        fadingOutSource.pitch = activeSource.pitch;
    }
}