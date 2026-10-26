using UnityEngine;

/// <summary>
/// Global manager for player audio preferences (Music and SFX volume).
/// Automatically persists settings in PlayerPrefs, keeps AudioListener and DynamicMusicManager
/// in sync, and provides a single source of truth across all scenes and runs.
/// </summary>
public static class GlobalAudioSettings
{
    public const string MusicPrefKey = "Settings_MusicVolume";
    public const string SfxPrefKey = "Settings_SfxVolume";

    public const float DefaultMusicVolume = 0.8f;
    public const float DefaultSfxVolume = 1.0f;

    private static float cachedMusicVolume = -1f;
    private static float cachedSfxVolume = -1f;

    public static float MusicVolume
    {
        get
        {
            if (cachedMusicVolume < 0f)
            {
                cachedMusicVolume = PlayerPrefs.GetFloat(MusicPrefKey, DefaultMusicVolume);
            }
            return cachedMusicVolume;
        }
    }

    public static float SfxVolume
    {
        get
        {
            if (cachedSfxVolume < 0f)
            {
                cachedSfxVolume = PlayerPrefs.GetFloat(SfxPrefKey, DefaultSfxVolume);
            }
            return cachedSfxVolume;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void InitializeOnBoot()
    {
        ApplyLoadedSettings();
    }

    public static void SetMusicVolume(float volume)
    {
        cachedMusicVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(MusicPrefKey, cachedMusicVolume);
        PlayerPrefs.Save();

        DynamicMusicManager musicMgr = Object.FindObjectOfType<DynamicMusicManager>();
        if (musicMgr != null)
        {
            musicMgr.SetMusicSliderVolume(cachedMusicVolume);
        }

        // Apply music volume to pause menu audio
        PauseManager pauseMgr = Object.FindObjectOfType<PauseManager>();
        if (pauseMgr != null && pauseMgr.pausedAudioSource != null)
        {
            pauseMgr.pausedAudioSource.ignoreListenerVolume = true;
            pauseMgr.pausedAudioSource.volume = pauseMgr.pausedSoundVolume * cachedMusicVolume;
        }

        // Apply music volume to main menu music
        MainMenuController menuCtrl = Object.FindObjectOfType<MainMenuController>();
        if (menuCtrl != null && menuCtrl.mainMenuMusicSource != null)
        {
            menuCtrl.mainMenuMusicSource.ignoreListenerVolume = true;
            menuCtrl.mainMenuMusicSource.volume = cachedMusicVolume;
        }
        else
        {
            GameObject menuMusicObj = GameObject.Find("mainmenumusic");
            if (menuMusicObj != null)
            {
                AudioSource src = menuMusicObj.GetComponent<AudioSource>();
                if (src != null)
                {
                    src.ignoreListenerVolume = true;
                    src.volume = cachedMusicVolume;
                }
            }
        }

                // Apply music volume to 3-effect overdose hallucination video
        SubstanceCombinationManager comboMgr = Object.FindObjectOfType<SubstanceCombinationManager>();
        if (comboMgr != null)
        {
            comboMgr.UpdateVideoAudioSettings();
        }

        if (RunSaveManager.HasInstance)
        {
            RunSaveManager.Instance.savedMusicSliderVolume = cachedMusicVolume;
            RunSaveManager.Instance.hasSavedAudioSettings = true;
        }
    }

    public static void SetSfxVolume(float volume)
    {
        cachedSfxVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(SfxPrefKey, cachedSfxVolume);
        PlayerPrefs.Save();

        AudioListener.volume = cachedSfxVolume;

        DynamicMusicManager musicMgr = Object.FindObjectOfType<DynamicMusicManager>();
        if (musicMgr != null)
        {
            musicMgr.SetSfxListenerVolume(cachedSfxVolume);
        }

        if (RunSaveManager.HasInstance)
        {
            RunSaveManager.Instance.savedSfxListenerVolume = cachedSfxVolume;
            RunSaveManager.Instance.hasSavedAudioSettings = true;
        }
    }

    public static void ApplyLoadedSettings()
    {
        float music = MusicVolume;
        float sfx = SfxVolume;

        AudioListener.volume = sfx;

        DynamicMusicManager musicMgr = Object.FindObjectOfType<DynamicMusicManager>();
        if (musicMgr != null)
        {
            musicMgr.SetMusicSliderVolume(music);
            musicMgr.SetSfxListenerVolume(sfx);
        }

        // Apply music volume to pause menu audio
        PauseManager pauseMgr = Object.FindObjectOfType<PauseManager>();
        if (pauseMgr != null && pauseMgr.pausedAudioSource != null)
        {
            pauseMgr.pausedAudioSource.ignoreListenerVolume = true;
            pauseMgr.pausedAudioSource.volume = pauseMgr.pausedSoundVolume * music;
        }

        // Apply music volume to main menu music
        MainMenuController menuCtrl = Object.FindObjectOfType<MainMenuController>();
        if (menuCtrl != null && menuCtrl.mainMenuMusicSource != null)
        {
            menuCtrl.mainMenuMusicSource.ignoreListenerVolume = true;
            menuCtrl.mainMenuMusicSource.volume = music;
        }
        else
        {
            GameObject menuMusicObj = GameObject.Find("mainmenumusic");
            if (menuMusicObj != null)
            {
                AudioSource src = menuMusicObj.GetComponent<AudioSource>();
                if (src != null)
                {
                    src.ignoreListenerVolume = true;
                    src.volume = music;
                }
            }
        }

        // Apply music volume to 3-effect overdose hallucination video
        SubstanceCombinationManager comboMgr = Object.FindObjectOfType<SubstanceCombinationManager>();
        if (comboMgr != null)
        {
            comboMgr.UpdateVideoAudioSettings();
        }
    }
}
