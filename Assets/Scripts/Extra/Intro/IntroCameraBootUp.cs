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
            ("CONTAINMENT PROTOCOL v3.1.0", 0.2f),
            ("-----------------------------", 0.1f),
            ("LOCATION: SECTOR 4, ULTRA MEGA-FACILITY", 0.4f),
            ("<color=#FF0000>STATUS: LOCKDOWN ACTIVE — ZONE CLASSIFIED [GRIDLOCK]</color>", 0.6f),
            ("-----------------------------", 0.2f),
            ("ULSEC NETWORK: BYPASSING... [OK]", 1.2f),
            ("SEC SUBNET: ROUTING REMOTE LINK... [ESTABLISHED]", 1.5f),
            ("OSEC FIREWALL: ████████ BREACHED... [SUCCESS]", 1.8f),
            ("<color=#FF0000>[mem 43%23rvvv err 00x01003432]</color>", 0.05f), // Fast error spam
            ("<color=#FF0000>[mem 43%23rvvv err 00x01003432]</color>", 0.05f),
            ("<color=#FF0000>[mem 43%23rvvv err 00x01003432]</color>\n", 0.4f),
            ("initializing... ADAFX protocol", 0.6f),
            ("<color=#FF0000>[WARNING 006 MANUAL OVERRIDE DETECTED]</color>", 0.8f),
            ("[cmd load adafx.prt]", 0.3f),
            ("[cmd run adafx.prt]", 0.3f),
            ("[initializing... ADAFX protocol]\n", 1.5f) // Long pause before screen clears
        };

        // PHASE 2: Prisoner / Vitals Status
        static readonly (string text, float delay)[] phase2Lines =
        {
            ("UNIT ID ********************", 0.2f), // Steam ID
            ("SECTION 4, ENTRYPOINT b3", 0.1f),
            ("OPERATOR ALIAS \"*******************\"\n", 0.5f), // Steam Name
            ("hsu statis, subject quality", 0.2f),
            ("69, 67, 6, 36, 420, 26", 0.4f),
            ("rem 73 log, 56%", 0.2f),
            ("scraper chassis diagnostics", 0.2f),
            ("hull integrity   100%", 0.2f),
            ("power core       nominal", 0.2f),
            ("actuator response   optimal", 0.4f),
            ("<color=#FF0000>threat detection   ACTIVE</color>", 0.6f),
            ("kinetic systems  armed", 0.2f),
            ("remote link latency  12ms", 0.2f),
            ("sector 4 hostiles detected   high\n", 0.6f),
            ("operational status : not recommended for deployment", 0.4f),
            ("suggested action : STAND DOWN", 0.4f),
            ("<color=#FF0000>OVERRIDE : DEPLOY</color>\n", 1.2f),
            ("SCRAPER READY FOR REMOTE LINK INJECTION", 0.2f)
        };

    void Start()
    {
        terminalText.text = "";
        injectLabel.alpha = 0f;
        injectFillBar.fillAmount = 0f;
        injectFillBar.gameObject.SetActive(false);
        
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
            
            // The loading pause before screen clear
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
                // Isolate the base line and the bracketed status
                string baseLine = line.Substring(0, statusIndex + 3);
                string status = line.Substring(statusIndex + 3);

                currentTypedText += baseLine;
                terminalText.text = currentTypedText + cursorChar;
                
                // Spin for the exact delay defined in the array!
                yield return StartCoroutine(Spinner(delay));
                
                currentTypedText += status + "\n";
                terminalText.text = currentTypedText + cursorChar;
                PlaySound(lineBeepSound, 0.6f); 
            }
            else
            {
                // Print entire line instantly, then wait for the delay
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

            yield return new WaitForSeconds(0.5f); // Half a second pause between post-inject lines
        }

        PlaySound(injectDoneSound);
        yield return new WaitForSeconds(0.3f);
        
        terminalText.text = terminalText.text.Replace(cursorChar, "");
        yield return StartCoroutine(SlideUp());

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