using UnityEngine;
using UnityEngine.SceneManagement;

// One reusable script for all 5 interactions.
// Add it to any object, fill in only the fields you need, then call its public
// methods from the XR Simple Interactable "Interactable Events" (or a UI Button's OnClick).
// No XR Toolkit namespaces are used, so it works with XRI 2.x and 3.x.
public class InteractionHelper : MonoBehaviour
{
    [Header("Hover highlight (Hover Entered / Hover Exited)")]
    public Renderer[] hoverRenderers;
    public Material hoverMaterial;
    public float hoverScale = 1.05f;

    [Header("Material cycling (Hover, Select or Activate)")]
    public Renderer cycleRenderer;          // for 3D objects (MeshRenderer)
    public UnityEngine.UI.Graphic cycleImage; // for a UI Image on a canvas
    public Material[] cycleMaterials;

    [Header("Panel to show/hide (info panel, start/end screen)")]
    public GameObject panel;

    [Header("Finish-line trigger (needs a trigger collider on this object)")]
    public bool showPanelWhenPlayerEnters;

    [Header("Optional sound")]
    public AudioSource sound;

    Material[][] originalMats;
    Vector3 originalScale;
    int cycleIndex;

    void Awake()
    {
        originalScale = transform.localScale;
        originalMats = new Material[hoverRenderers.Length][];
        for (int i = 0; i < hoverRenderers.Length; i++)
            if (hoverRenderers[i]) originalMats[i] = hoverRenderers[i].sharedMaterials;
    }

    // --- Hover ---
    public void HoverOn()
    {
        for (int i = 0; i < hoverRenderers.Length; i++)
        {
            if (!hoverRenderers[i]) continue;
            var mats = new Material[originalMats[i].Length];
            for (int m = 0; m < mats.Length; m++) mats[m] = hoverMaterial;
            if (hoverMaterial) hoverRenderers[i].sharedMaterials = mats;
        }
        transform.localScale = originalScale * hoverScale;
    }

    public void HoverOff()
    {
        for (int i = 0; i < hoverRenderers.Length; i++)
            if (hoverRenderers[i]) hoverRenderers[i].sharedMaterials = originalMats[i];
        transform.localScale = originalScale;
    }

    // --- Change material / background ---
    public void NextMaterial()
    {
        if (cycleMaterials.Length == 0) return;
        cycleIndex = (cycleIndex + 1) % cycleMaterials.Length;
        if (cycleRenderer) cycleRenderer.sharedMaterial = cycleMaterials[cycleIndex];
        if (cycleImage) cycleImage.material = cycleMaterials[cycleIndex];
        PlaySound();
    }

    // --- Panels ---
    public void ShowPanel()   { if (panel) panel.SetActive(true); }
    public void HidePanel()   { if (panel) panel.SetActive(false); }
    public void TogglePanel() { if (panel) panel.SetActive(!panel.activeSelf); }

    // --- Misc ---
    public void PlaySound() { if (sound) sound.Play(); }

    public void RestartScene()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // End screen when the player drives/walks through the finish arch.
    // Tag your XR Origin as "Player".
    void OnTriggerEnter(Collider other)
    {
        if (showPanelWhenPlayerEnters && other.CompareTag("Player")) ShowPanel();
    }
}
