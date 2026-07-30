using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Attach to any wall picture, poster, or object you want clickable.
/// The object needs a Collider (your posters/pictures should get one).
///
/// In the Inspector you can wire up:
///   - onInteract : a UnityEvent (drag in a UI panel's SetActive, a method, etc.)
///   - an optional AudioClip that plays on click
///   - an optional GameObject (e.g. a UI popup) to toggle on click
///
/// This keeps designers out of code: most posters just need the popup or clip
/// fields filled in, no scripting required.
/// </summary>
public class Interactable : MonoBehaviour
{
    [Header("Identity (optional)")]
    [Tooltip("Friendly name, e.g. 'Poster: Photosynthesis'. Handy for debugging/logs.")]
    public string displayName = "";

    [Header("Option 1: Toggle a UI popup")]
    [Tooltip("A UI GameObject (panel) to show when clicked. Leave empty if unused.")]
    public GameObject popupToToggle;
    [Tooltip("If true, a second click hides it again. If false, it only shows.")]
    public bool popupToggles = true;

    [Header("Option 2: Play a sound")]
    [Tooltip("Audio to play on click. Needs an AudioSource on this object or it " +
             "will be played at the hit point via PlayClipAtPoint.")]
    public AudioClip audioClip;
    [Range(0f, 1f)] public float audioVolume = 1f;

    [Header("Option 3: Custom event(s)")]
    [Tooltip("Wire up any methods to call on click (analytics, animations, etc.).")]
    public UnityEvent onInteract;

    [Header("Feedback")]
    [Tooltip("Optional: briefly scale up on click for tactile feedback.")]
    public bool pulseOnClick = true;

    AudioSource cachedSource;

    void Awake()
    {
        cachedSource = GetComponent<AudioSource>();
    }

    /// <summary>Called by InteractionRaycaster when this object is clicked.</summary>
    public void Interact(RaycastHit hit)
    {
        if (!string.IsNullOrEmpty(displayName))
            Debug.Log($"[Interactable] Clicked: {displayName}");

        // Option 1: popup
        if (popupToToggle != null)
        {
            if (popupToggles)
                popupToToggle.SetActive(!popupToToggle.activeSelf);
            else
                popupToToggle.SetActive(true);
        }

        // Option 2: audio
        if (audioClip != null)
        {
            if (cachedSource != null)
                cachedSource.PlayOneShot(audioClip, audioVolume);
            else
                AudioSource.PlayClipAtPoint(audioClip, hit.point, audioVolume);
        }

        // Option 3: custom events
        onInteract?.Invoke();

        // Feedback
        if (pulseOnClick)
            StartCoroutine(Pulse());
    }

    System.Collections.IEnumerator Pulse()
    {
        Vector3 baseScale = transform.localScale;
        Vector3 big = baseScale * 1.06f;
        float t = 0f, dur = 0.12f;
        while (t < dur)
        {
            t += Time.deltaTime;
            transform.localScale = Vector3.Lerp(baseScale, big, t / dur);
            yield return null;
        }
        t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            transform.localScale = Vector3.Lerp(big, baseScale, t / dur);
            yield return null;
        }
        transform.localScale = baseScale;
    }
}
