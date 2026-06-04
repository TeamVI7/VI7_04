using System.Collections;
using System.Text; // Required for StringBuilder
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;

public class StartUpBootSequence : MonoBehaviour
{
    [Header("HUD")]
    public CanvasGroup hudRoot;
    public CanvasGroup[] hudLayers;
    public TextMeshProUGUI bootText;
    public Image scanlineOverlay;
    public Image staticOverlay;
    public Image vignetteRing;

    [Header("Timing")]
    public float layerStaggerDelay = 0.2f;

    [Header("Loading")]
    public float fakeLoadDuration = 5f;

    [Header("Player")]
    public GameObject player;

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioSource musicSource;
    public AudioClip[] musicTracks;
    public AudioClip logoLineSound;
    public AudioClip corpNameSound;
    public AudioClip bootStartSound;
    public AudioClip lineBeepSound;
    public AudioClip spinnerDoneSound;
    public AudioClip warningSound;
    public AudioClip readySound;
    public AudioClip lockInSound;

    [Header("Events")] // FIXED: Now public so you can actually use it in the Inspector!
    public UnityEvent onBootSequenceComplete;

    private bool skipSequence = false;
    private StringBuilder textBuilder = new StringBuilder();

    // ARCHITECTURE UPGRADE: (string text, float delay, int soundType)
    // Sound Types: 0 = Silent, 1 = Logo, 2 = CorpName, 3 = BootStart, 4 = Warning, 5 = Ready, 6 = Standard Beep
    static readonly (string text, float delay, int soundType)[] bootLines =
    {
        ("",                                                                                        0.1f,  0),
        ("     -###.     +#############################+",                                          0.05f, 1),
        ("     -###.     +#############################+",                                          0.05f, 1),
        ("     -###.     +###.....####....####......###+",                                          0.05f, 1),
        ("     -###.     +###.    ####    ####      ###+",                                          0.05f, 1),
        ("     -###.     +###     ####    ####++++++###+",                                          0.05f, 1),
        ("     -###.     +###.    ####    #############+",                                          0.05f, 1),
        ("     -#############.    ####    ####------###+",                                          0.05f, 1),
        ("     -#############     ####    ####      ###+",                                          0.05f, 1),
        ("     .-------------     ----    ----      ----",                                          0.05f, 1),
        ("",                                                                                        0.3f,  0),
        ("     ULTRA TECHNOLOGY CORPORATION",                                                       0.3f,  2),
        ("     VINSON DYNAMICS v4.2.1",                                                             0.5f,  6),
        ("",                                                                                        0.3f,  0),
        ("     Booting VINSON DYNAMICS OS kernel v4.2.1...",                                        0.3f,  3),
        ("[    0.000000] ACPI: Core revision 20320415",                                             0.1f,  6),
        ("[    0.012014] smpboot: CPU0: Vinson-Class Neural Processor @ 4.20GHz",                  0.1f,  6),
        ("[    0.054112] Loading dual-boot env: ULTRA OS v7.2.1 - (C) 2032 Ultra Technology Corporation", 0.1f, 6),
        ("[    0.104221] ultra-energy-subsys: Initializing UEC grid... [OK]",                       1f,    6),
        ("[    0.155000] manh-sec: Bypassing MIC firewall... [SUCCESS]",                            2f,    6),
        ("[    0.201543] medirc-core: Syncing Medirc bio-monitors... [STABLE]",                     2f,    6),
        ("[    0.410221] shinobuya-net: Routing via SEC subnets... [ESTABLISHED]",                  1f,    6),
        ("[    0.455000] longtian-ai: Longtian AI core handshake... [SYNCHRONIZED]",                1.5f,  6),
        ("[    0.500000] volskov-perim: Volskov perimeter uplink... [SECURED]",                     1.5f,  6),
        ("[    0.550111] systemd[1]: Reached target Simulation Environment.",                       0.2f,  6),
        ("[    0.553210] kernel: [CRITICAL] WARNING: SECTOR 4 AI LOCKDOWN DETECTED.",               2f,    4),
        ("[    0.560112] init: DEPLOYING REMOTE LINK.",                                             0.5f,  6),
        ("[    0.601200] init-physics: KINETIC SYSTEMS: UNRESTRICTED",                              0.3f,  6),
        ("[    0.650000] diag: CHASSIS INTEGRITY: 100%",                                            0.3f,  6),
        ("[    0.702111] link-layer: REMOTE NEURAL LINK: CONFIRMED",                                0.3f,  6),
        ("[    0.755432] armory-sys: WEAPON SYSTEMS: ONLINE",                                       0.3f,  6),
        ("[    0.801001] board-sys: RANK: PAWN",                                                    0.5f,  6),
        ("[    0.850000] systemd[1]: READY.",                                                       1.5f,  5),
        ("",                                                                                        0.1f,  0),
        ("     root@gridlock-node:~# Awaiting deployment command_",                                 0f,    6),
    };

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
        player.SetActive(false);
        if (hudRoot != null) hudRoot.alpha = 1f;
        foreach (var layer in hudLayers)
            if (layer != null) layer.alpha = 0f;
        
        if (bootText != null) bootText.text = "";
        if (staticOverlay != null) staticOverlay.color = Color.clear;
        
        StartCoroutine(Boot());
        StartCoroutine(PlayRandomMusic());
    }

    void Update()
    {
        // UX UPGRADE: Press Space or Escape to skip the long intro
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Escape))
        {
            skipSequence = true;
        }
    }

    IEnumerator PlayRandomMusic()
    {
        if (musicSource == null || musicTracks == null || musicTracks.Length == 0) yield break;
        musicSource.clip = musicTracks[Random.Range(0, musicTracks.Length)];
        musicSource.loop = true;
        musicSource.Play();
    }

    void PlaySound(AudioClip clip, float volume = 1f)
    {
        if (audioSource && clip && !skipSequence)
            audioSource.PlayOneShot(clip, volume);
    }

    void PlayMappedSound(int soundType)
    {
        switch (soundType)
        {
            case 1: PlaySound(logoLineSound, 0.4f); break;
            case 2: PlaySound(corpNameSound); break;
            case 3: PlaySound(bootStartSound); break;
            case 4: PlaySound(warningSound); break;
            case 5: PlaySound(readySound); break;
            case 6: PlaySound(lineBeepSound, 0.5f); break;
        }
    }

    IEnumerator Boot()
    {
        // Skips the fake load duration if player pressed space
        float loadTimer = 0;
        while (loadTimer < fakeLoadDuration)
        {
            if (skipSequence) break;
            loadTimer += Time.deltaTime;
            yield return null;
        }

        yield return StartCoroutine(TypeLog());
        yield return StartCoroutine(BootHUD());
        yield return StartCoroutine(LockIn());
        
        onBootSequenceComplete?.Invoke();
        player.SetActive(true);
    }

    IEnumerator TypeLog()
    {
        bootText.gameObject.SetActive(true);
        textBuilder.Clear();

        for (int i = 0; i < bootLines.Length; i++)
        {
            if (skipSequence) break;

            var (line, delay, soundType) = bootLines[i];
            
            PlayMappedSound(soundType);

            int statusIndex = line.IndexOf("... [");
            if (statusIndex != -1)
            {
                string baseLine = line.Substring(0, statusIndex + 3);
                string status   = line.Substring(statusIndex + 3);

                textBuilder.Append(baseLine);
                yield return StartCoroutine(Spinner(delay));
                
                PlaySound(spinnerDoneSound);
                
                textBuilder.Append(status).Append("\n");
                bootText.text = textBuilder.ToString();
            }
            else
            {
                textBuilder.Append(line).Append("\n");
                bootText.text = textBuilder.ToString();
                
                // Allow skipping during the delay
                float delayTimer = 0;
                while (delayTimer < delay)
                {
                    if (skipSequence) break;
                    delayTimer += Time.deltaTime;
                    yield return null;
                }
            }
        }

        // If skipped, instantly dump the full text to screen
        if (skipSequence)
        {
            textBuilder.Clear();
            foreach (var bootLine in bootLines)
            {
                textBuilder.Append(bootLine.text).Append("\n");
            }
            bootText.text = textBuilder.ToString();
        }

        yield return StartCoroutine(WaitForInput());
        yield return StartCoroutine(SlideUp());
        bootText.gameObject.SetActive(false);
    }

    IEnumerator WaitForInput()
    {
        yield return new WaitForSeconds(0.5f);

        string baseText = bootText.text;
        bool show = true;
        float blinkTimer = 0f;

        while (true)
        {
            blinkTimer += Time.deltaTime;
            if (blinkTimer >= 0.5f)
            {
                blinkTimer = 0f;
                show = !show;
                bootText.text = baseText + (show ? "\n[ PRESS ANY KEY ]" : "\n                 ");
            }

            if (Input.anyKeyDown)
            {
                bootText.text = baseText;
                if (audioSource) audioSource.Stop();
                yield return new WaitForSeconds(0.15f);
                break;
            }

            yield return null;
        }
    }

    IEnumerator BootHUD()
    {
        foreach (var layer in hudLayers)
        {
            if (layer == null) continue;
            float t = 0f;
            while (t < 0.2f) { t += Time.deltaTime; layer.alpha = t / 0.2f; yield return null; }
            layer.alpha = 1f;
            yield return new WaitForSeconds(layerStaggerDelay);
        }
    }

    IEnumerator LockIn()
    {
        if (vignetteRing != null)
        {
            if (!skipSequence) PlaySound(lockInSound);
            
            vignetteRing.color = new Color(0.2f, 0.85f, 1f, 1f);
            float t = 0f;
            while (t < 0.5f)
            {
                t += Time.deltaTime;
                vignetteRing.color = Color.Lerp(new Color(0.2f, 0.85f, 1f, 1f), Color.clear, t / 0.5f);
                yield return null;
            }
        }
        if (scanlineOverlay != null)
            scanlineOverlay.color = new Color(0, 0, 0, 0.08f);
    }

    IEnumerator SlideUp()
    {
        float duration = 0.8f;
        float t = 0f;
        Vector2 startPos = bootText.rectTransform.anchoredPosition;
        Vector2 endPos = new Vector2(startPos.x, startPos.y + Screen.height);

        while (t < duration)
        {
            t += Time.deltaTime;
            float ease = 1f - Mathf.Pow(1f - t / duration, 3f);
            bootText.rectTransform.anchoredPosition = Vector2.Lerp(startPos, endPos, ease);
            yield return null;
        }
    }

    IEnumerator Spinner(float duration)
    {
        string[] frames = { "/", "-", "\\", "|" };
        float elapsed = 0f;
        int i = 0;
        
        while (elapsed < duration)
        {
            if (skipSequence) break;
            bootText.text = textBuilder.ToString() + frames[i % frames.Length];
            i++;
            elapsed += 0.1f;
            yield return new WaitForSeconds(0.1f);
        }
        bootText.text = textBuilder.ToString();
    }
}