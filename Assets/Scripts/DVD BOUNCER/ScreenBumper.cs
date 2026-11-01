using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// System 4 (Pinball Bumpers Remote & Ultimate Remote):
/// Represents a 3D bumper sphere placed directly inside the TV renderCamera's world space
/// so it is captured into the TV's RenderTexture (SCREENTRANSFER) alongside the DVD logo.
/// Enforces a FIFO constraint (default max 5 active bumpers) and violently ricochets any
/// colliding DVDLogoBouncer with bonus speed and a crisp ping sound.
/// </summary>
public class ScreenBumper : MonoBehaviour
{
    public static int MaxBumpers = 5;
    private static readonly Queue<ScreenBumper> activeBumpers = new Queue<ScreenBumper>();

    [Header("Runtime Bumper State")]
    public Vector3 orthoWorldPos;
    public float orthoRadius = 0.42f;
    public AudioSource audioSource;
    public AudioClip pingSound;

    private Vector3 baseScale;
    private float pulseTimer = 0f;
    private float hitCooldown = 0f;

    /// <summary>
    /// Spawns a 3D bumper inside the TV renderCamera's world space, enforcing the FIFO max bumper limit.
    /// </summary>
    public static ScreenBumper SpawnBumper(
        Vector3 tvSurfacePos,
        Vector3 surfaceNormal,
        Vector3 orthoPos,
        Camera renderCamera,
        int targetLayer,
        AudioSource sharedAudio,
        AudioClip spawnClip,
        AudioClip pingClip,
        float customOrthoRadius = 0.42f,
        int maxAllowedBumpers = 5,
        GameObject customBumperPrefab = null,
        Color? customBumperColor = null)
    {
        MaxBumpers = Mathf.Max(1, maxAllowedBumpers);

        // Clean up any destroyed references at the front of the queue
        while (activeBumpers.Count > 0 && activeBumpers.Peek() == null)
        {
            activeBumpers.Dequeue();
        }

        // FIFO constraint: placing a new bumper beyond MaxBumpers despawns the oldest
        while (activeBumpers.Count >= MaxBumpers)
        {
            ScreenBumper oldest = activeBumpers.Dequeue();
            if (oldest != null)
            {
                oldest.DespawnImmediate();
            }
        }

        GameObject bumperObj;
        if (customBumperPrefab != null)
        {
            bumperObj = Instantiate(customBumperPrefab);
            bumperObj.name = "ScreenPinballBumper_InScreen";
            Collider[] cols = bumperObj.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null) Destroy(cols[i]);
            }
        }
        else
        {
            bumperObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bumperObj.name = "ScreenPinballBumper_InScreen";
            Collider col = bumperObj.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        if (renderCamera != null)
        {
            bumperObj.transform.SetParent(renderCamera.transform, true);
        }

        // Place directly inside the TV renderCamera space so it streams into SCREENTRANSFER
        bumperObj.transform.position = orthoPos;
        float diameter = Mathf.Max(0.1f, customOrthoRadius * 2f);
        bumperObj.transform.localScale = new Vector3(diameter, diameter, diameter);
        SetLayerRecursively(bumperObj, targetLayer);

        if (customBumperPrefab == null)
        {
            Material glowMat = CreateGlowingBumperMaterial(customBumperColor ?? new Color(0.2f, 0.95f, 1f, 1f));
            Renderer r = bumperObj.GetComponent<Renderer>();
            if (r != null)
            {
                r.material = glowMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        ScreenBumper bumper = bumperObj.GetComponent<ScreenBumper>();
        if (bumper == null) bumper = bumperObj.AddComponent<ScreenBumper>();
        bumper.orthoWorldPos = orthoPos;
        bumper.orthoRadius = customOrthoRadius;
        bumper.audioSource = sharedAudio;
        bumper.pingSound = pingClip;
        bumper.baseScale = bumperObj.transform.localScale;

        activeBumpers.Enqueue(bumper);

        if (sharedAudio != null && spawnClip != null)
        {
            sharedAudio.PlayOneShot(spawnClip, 0.75f);
        }

        return bumper;
    }

    public static void ClearAllBumpers()
    {
        while (activeBumpers.Count > 0)
        {
            ScreenBumper b = activeBumpers.Dequeue();
            if (b != null) b.DespawnImmediate();
        }
    }

    public void DespawnImmediate()
    {
        Destroy(gameObject);
    }

    private static Material CreateGlowingBumperMaterial(Color baseGlowColor)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");

        Material mat = new Material(shader);
        mat.color = baseGlowColor;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseGlowColor);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.85f);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.25f);
        if (mat.HasProperty("_EmissionColor"))
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", baseGlowColor * 1.6f);
        }
        return mat;
    }

    private static void SetLayerRecursively(GameObject obj, int layer)
    {
        if (obj == null) return;
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            if (child != null) SetLayerRecursively(child.gameObject, layer);
        }
    }

    private void Update()
    {
        // Bumpers only belong to the Pinball / Omni remotes: remove them if another remote is equipped
        // so they never leak into other remote minigames.
        if (UpgradeManager.Instance != null && !UpgradeManager.Instance.IsCurrentRemoteBumperCapable)
        {
            ClearAllBumpers();
            Destroy(gameObject);
            return;
        }

        if (PauseManager.IsGamePaused || DVDLogoBouncer.GlobalStaticHalt) return;

        if (hitCooldown > 0f)
        {
            hitCooldown -= Time.deltaTime;
        }

        if (pulseTimer > 0f)
        {
            pulseTimer -= Time.deltaTime;
            float t = 1f - Mathf.Clamp01(pulseTimer / 0.22f);
            float s = 1f + 0.5f * Mathf.Sin(t * Mathf.PI) * (1f - t);
            transform.localScale = baseScale * s;
        }
        else
        {
            float idle = 1f + 0.06f * Mathf.Sin(Time.time * 5f);
            transform.localScale = baseScale * idle;
        }

        if (hitCooldown <= 0f)
        {
            CheckLogoCollisions();
        }
    }

    private void CheckLogoCollisions()
    {
        var logos = DVDLogoBouncer.ActiveLogos;
        for (int i = 0; i < logos.Count; i++)
        {
            DVDLogoBouncer logo = logos[i];
            if (logo == null || !logo.isActiveAndEnabled) continue;

            Vector3 logoCenter = logo.GetLogoCenter();
            float halfW = logo.HalfWidth;
            float halfH = logo.HalfHeight;

            // Closest point on the logo's 2D AABB to the bumper's ortho center
            float closestX = Mathf.Clamp(orthoWorldPos.x, logoCenter.x - halfW, logoCenter.x + halfW);
            float closestY = Mathf.Clamp(orthoWorldPos.y, logoCenter.y - halfH, logoCenter.y + halfH);

            float dx = orthoWorldPos.x - closestX;
            float dy = orthoWorldPos.y - closestY;
            float distSq = dx * dx + dy * dy;

            if (distSq <= orthoRadius * orthoRadius)
            {
                hitCooldown = 0.10f;
                pulseTimer = 0.22f;

                if (audioSource != null && pingSound != null)
                {
                    audioSource.PlayOneShot(pingSound, 0.95f);
                }

                logo.ApplyBumperRicochet(orthoWorldPos, orthoWorldPos);
                break;
            }
        }
    }
}
