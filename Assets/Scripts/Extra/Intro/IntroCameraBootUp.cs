using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class GTFOMenuScreen : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI terminalText;
    public TextMeshProUGUI injectLabel;
    public Image injectFillBar;
    public CanvasGroup screenGroup;

    [Header("OS Welcome Screen")]
    public CanvasGroup osWelcomeGroup;
    public Image osLogo;
    public TextMeshProUGUI welcomeText;
    public float welcomeDuration = 5.0f; // Add this line to control the timing

    [Header("Settings")]
    public float holdDuration = 2.0f;
    public string mainMenuSceneName = "MainMenu";
    private string cursorChar = "<color=#FFFFFF>█</color>"; 

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioSource musicSource;
    public AudioClip[] musicTracks;
    public AudioClip introStartSound;
    public AudioClip lineBeepSound;
    public AudioClip systemReadySound;
    public AudioClip injectHoldSound;
    public AudioClip injectDoneSound;
    public AudioClip glitchSound;
    public AudioClip osStartupSound; // Win95 startup chime

    private bool injecting = false;
    private float holdTimer = 0f;
    private bool readyToInject = false;
    private bool injected = false;
    private bool skipToInject = false;
    private string currentTypedText = ""; 

    // PHASE 1: Diagnostic Boot
    // FORMAT: ("Your text here", delayInSeconds)
        static readonly (string text, float delay)[] phase1Lines =
        {
            ("Linux version 6.5.0-operator (root@area4) (gcc version 12.3.0)", 0.4f),
            ("Command line: root=/dev/nvme0n1p1 ro quiet splash mitigations=off", 0.35f),
            ("BIOS-provided physical RAM map:", 0.25f),
            ("BIOS-e820: [mem 0x0000000000000000-0x000000000009ffff] usable", 0.2f),
            ("BIOS-e820: [mem 0x0000000000100000-0x00000003ffffffff] usable", 0.2f),
            ("[    0.000000] Linux kernel booted on area-4-node", 0.3f),
            ("[    0.123456] x86/fpu: Supporting XSAVE features 0x001", 0.25f),
            ("[    0.345678] CPU: 64 cores, 128 threads", 0.3f),
            ("[    0.567890] Memory: 131072M/131072M available", 0.3f),
            ("[    0.789012] Calibrating delay loop (skipped)", 0.2f),
            ("[    1.012345] PID hash table entries: 262144", 0.25f),
            ("[    1.234567] Dentry cache hash table entries: 8388608", 0.25f),
            ("[    1.456789] Inode-cache hash table entries: 4194304", 0.25f),
            ("[    1.678901] Mount-cache hash table entries: 131072", 0.2f),
            ("[    1.901234] Initializing cgroup subsys cpu,cpuacct,memory", 0.3f),
            ("[    2.123456] ULSEC network interface eth0: link up", 0.6f),
            ("[    2.456789] SEC subnet: establishing encrypted tunnel", 0.7f),
            ("[    3.012345] OSEC firewall: <color=#FF0000>bypassing ruleset</color>", 0.8f),
            ("[    3.567890] OSEC firewall: <color=#FF0000>breached</color> <color=#00FF00>[SUCCESS]</color>", 0.9f),
            ("<color=#FF0000>[mem error 0x01003432] corrupted page @ 0x43%23rvvv</color>", 0.05f),
            ("<color=#FF0000>[mem error 0x01003432] corrupted page @ 0x43%23rvvv</color>", 0.05f),
            ("<color=#FF0000>[mem error 0x01003432] corrupted page @ 0x43%23rvvv</color>\n", 0.35f),
            ("[    4.123456] Loading ADAFX protocol module", 0.5f),
            ("[    4.567890] modprobe adafx.prt", 0.3f),
            ("[    5.012345] adafx: initializing quantum-secure link", 0.6f),
            ("<color=#FF0000>[WARNING] Manual override detected in kernel ring buffer</color>", 0.7f),
            ("[    5.678901] Starting ADAFX daemon...", 0.8f),
            ("[    6.234567] ADAFX protocol v3.1.0 loaded successfully", 0.6f),
            ("[    6.789012] Remote link latency: 12ms", 0.4f),
            ("[    7.345678] Initializing threat detection engine", 0.7f),
            ("[    8.012345] Mounting operator overlay filesystem", 0.5f),
            ("[    8.567890] System uptime: 00h 13m 42s", 0.4f)
        };

        static readonly (string text, float delay)[] phase2Lines =
        {
            ("Unit ID: ********************", 0.2f),
            ("Hostname: area-4-entrypoint-b3", 0.2f),
            ("Operator: *******************\n", 0.4f),
            ("Uptime: 00:13:42", 0.2f),
            ("Load average: 0.69, 0.67, 0.66", 0.25f),
            ("Tasks: 420 total, 12 running, 89 sleeping", 0.2f),
            ("Memory: 73% used (95.8GiB / 131GiB)", 0.25f),
            ("Swap: 0B used", 0.2f),
            ("CPU usage: 36% user, 4% system, 420 threads", 0.25f),
            ("Chassis diagnostics: <color=#00FF00>OK</color>", 0.2f),
            ("Hull integrity: 100%", 0.2f),
            ("Power core temperature: nominal (42.3°C)", 0.2f),
            ("Actuator response: optimal", 0.2f),
            ("Kinetic systems: armed and ready", 0.25f),
            ("<color=#FF0000>Threat detection: ACTIVE</color>", 0.5f),
            ("Network latency: 12ms (stable)", 0.2f),
            ("Sector 4 hostiles detected: HIGH", 0.6f),
            ("[    9.123456] Scanning local area for anomalies", 0.4f),
            ("[    9.567890] 17 unknown signatures detected", 0.35f),
            ("System status: <color=#FFAA00>DEGRADED</color>", 0.4f),
            ("Suggested action: STAND DOWN", 0.4f),
            ("<color=#FF0000>OVERRIDE: DEPLOY</color>\n", 1.1f),
            ("<color=#00FF00>READY FOR DEPLOYMENT</color>", 0.4f)
        };

    void Start()
    {
        terminalText.text = "";
        injectLabel.alpha = 0f;
        injectFillBar.fillAmount = 0f;
        injectFillBar.gameObject.SetActive(false);
        
        if (osWelcomeGroup != null)
        {
            osWelcomeGroup.alpha = 0f;
            osWelcomeGroup.gameObject.SetActive(false);
        }
        
        StartCoroutine(RunIntro());
        StartCoroutine(PlayRandomMusic());
    }

    void Update()
    {
        if (!readyToInject && !injected && Input.GetKeyDown(KeyCode.Space))
            skipToInject = true;

        if (!readyToInject || injected) return;

        if (Input.GetKey(KeyCode.E) || Input.GetMouseButton(0))
        {
            if (!injecting && injectHoldSound && audioSource)
            {
                audioSource.clip = injectHoldSound;
                audioSource.loop = true;
                audioSource.pitch = 1f; 
                audioSource.Play();
            }

            injecting = true;
            injectFillBar.gameObject.SetActive(true);
            holdTimer += Time.deltaTime;
            injectFillBar.fillAmount = holdTimer / holdDuration;

            if (holdTimer >= holdDuration)
            {
                injected = true;
                StopHoldSound();
                StartCoroutine(Inject());
            }
        }
        else
        {
            if (injecting) StopHoldSound();

            injecting = false;
            holdTimer = Mathf.Max(0f, holdTimer - Time.deltaTime * 3f);
            injectFillBar.fillAmount = holdTimer / holdDuration;
            if (holdTimer <= 0f)
                injectFillBar.gameObject.SetActive(false);
        }
    }

    void PlaySound(AudioClip clip, float volume = 1f, float pitchVariation = 0.05f)
    {
        if (audioSource && clip)
        {
            audioSource.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
            audioSource.PlayOneShot(clip, volume);
        }
    }

    void StopHoldSound()
    {
        if (audioSource && audioSource.clip == injectHoldSound)
        {
            audioSource.Stop();
            audioSource.loop = false;
            audioSource.clip = null;
        }
    }

    IEnumerator PlayRandomMusic()
    {
        if (musicSource == null || musicTracks == null || musicTracks.Length == 0) yield break;
        musicSource.clip = musicTracks[Random.Range(0, musicTracks.Length)];
        musicSource.loop = true;
        musicSource.Play();
    }

    IEnumerator RunIntro()
    {
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * 1.5f;
            screenGroup.alpha = t;
            yield return null;
        }

        PlaySound(introStartSound);
        yield return new WaitForSeconds(0.4f);

        terminalText.alignment = TextAlignmentOptions.TopLeft;

        // ==== PHASE 1 ====
        if (!skipToInject)
        {
            yield return StartCoroutine(TypeLines(phase1Lines));
            
            yield return StartCoroutine(Spinner(1.5f)); 
            
            PlaySound(glitchSound, 0.4f);
            terminalText.text = "";
            currentTypedText = "";
            yield return new WaitForSeconds(0.5f);
        }

        // ==== PHASE 2 ====
        if (!skipToInject)
        {
            yield return StartCoroutine(TypeLines(phase2Lines));
        }
        else
        {
            currentTypedText = "";
            foreach (var line in phase2Lines) currentTypedText += line.text + "\n";
            terminalText.text = currentTypedText + cursorChar;
        }

        PlaySound(systemReadySound);
        yield return new WaitForSeconds(0.5f);
        
        readyToInject = true;
        
        StartCoroutine(BlinkCursor());
        StartCoroutine(BlinkInject());
    }

    IEnumerator TypeLines((string text, float delay)[] lines)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            if (skipToInject) break;

            string line = lines[i].text;
            float delay = lines[i].delay;
            int statusIndex = line.IndexOf("... [");

            if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("---"))
                PlaySound(lineBeepSound, 0.4f, 0.1f);

            if (statusIndex != -1)
            {
                string baseLine = line.Substring(0, statusIndex + 3);
                string status = line.Substring(statusIndex + 3);

                currentTypedText += baseLine;
                terminalText.text = currentTypedText + cursorChar;
                
                yield return StartCoroutine(Spinner(delay));
                
                currentTypedText += status + "\n";
                terminalText.text = currentTypedText + cursorChar;
                PlaySound(lineBeepSound, 0.6f); 
            }
            else
            {
                currentTypedText += line + "\n";
                terminalText.text = currentTypedText + cursorChar;
                yield return new WaitForSeconds(delay);
            }
        }
    }

    IEnumerator Spinner(float duration)
    {
        string[] frames = { "/", "-", "\\", "|" };
        float elapsed = 0f;
        int i = 0;
        
        while (elapsed < duration)
        {
            if (skipToInject) break;
            
            terminalText.text = currentTypedText + " " + frames[i % frames.Length];
            i++;
            elapsed += 0.1f;
            yield return new WaitForSeconds(0.1f);
        }
        
        terminalText.text = currentTypedText + cursorChar;
    }

    IEnumerator BlinkCursor()
    {
        bool showCursor = true;
        string baseText = terminalText.text.Replace(cursorChar, "");
        
        while (!injected)
        {
            terminalText.text = baseText + (showCursor ? cursorChar : "");
            showCursor = !showCursor;
            yield return new WaitForSeconds(0.4f);
        }
    }

    IEnumerator BlinkInject()
    {
        injectLabel.text = "[ HOLD E TO CONNECT ]";
        while (!injecting)
        {
            injectLabel.alpha = 1f;
            yield return new WaitForSeconds(0.6f);
            injectLabel.alpha = 0.2f;
            yield return new WaitForSeconds(0.3f);
        }
        injectLabel.alpha = 1f;
    }

    IEnumerator Inject()
    {
        PlaySound(glitchSound);

        float t = 0f;
        while (t < 0.5f)
        {
            t += Time.deltaTime;
            screenGroup.alpha = 1f - Mathf.Sin(t * 80f) * 0.3f;
            yield return null;
        }

        injectLabel.gameObject.SetActive(false);
        injectFillBar.gameObject.SetActive(false);

        terminalText.text = "";
        yield return new WaitForSeconds(0.2f);

        string[] postInject =
        {
            "CONNECTION COMPLETE.",
            "TRANSFERRING TO MAIN SYSTEMS...",
            "-----------------------------"
        };

        currentTypedText = "";

        foreach (string line in postInject)
        {
            currentTypedText += line + "\n";
            terminalText.text = currentTypedText + cursorChar;
            
            if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("---"))
                PlaySound(lineBeepSound, 0.5f, 0.1f);

            yield return new WaitForSeconds(0.5f); 
        }

        PlaySound(injectDoneSound);
        yield return new WaitForSeconds(0.3f);
        
        terminalText.text = terminalText.text.Replace(cursorChar, "");
        yield return StartCoroutine(SlideUp());

        // --- WIN95 OS WELCOME SEQUENCE ---
        if (osWelcomeGroup != null)
        {
            osWelcomeGroup.gameObject.SetActive(true);
            
            if (osStartupSound != null && audioSource != null)
            {
                audioSource.pitch = 1f;
                audioSource.PlayOneShot(osStartupSound, 1f);
            }

            // Fade in the OS Logo and Welcome Text
            float fadeTime = 0f;
            while (fadeTime < 1f)
            {
                fadeTime += Time.deltaTime;
                osWelcomeGroup.alpha = fadeTime;
                yield return null;
            }

            // Hold on the welcome screen for your custom duration
            yield return new WaitForSeconds(welcomeDuration);
            
            // Fade out smoothly before switching scenes
            float fadeOutTime = 0f;
            while (fadeOutTime < 1f)
            {
                fadeOutTime += Time.deltaTime;
                osWelcomeGroup.alpha = 1f - fadeOutTime;
                yield return null;
            }
        }
    

        SceneManager.LoadScene(mainMenuSceneName);
    }

    IEnumerator SlideUp()
    {
        float duration = 0.8f;
        float t = 0f;
        Vector2 startPos = terminalText.rectTransform.anchoredPosition;
        Vector2 endPos = new Vector2(startPos.x, startPos.y + Screen.height);

        while (t < duration)
        {
            t += Time.deltaTime;
            float ease = 1f - Mathf.Pow(1f - t / duration, 3f);
            terminalText.rectTransform.anchoredPosition = Vector2.Lerp(startPos, endPos, ease);
            yield return null;
        }
    }
}