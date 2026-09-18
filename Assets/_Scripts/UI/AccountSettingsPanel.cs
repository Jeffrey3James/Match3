using JadedBelles.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// In-app account-management panel. Meets the App Store and Google Play
/// requirement that an app which creates accounts must let users request
/// deletion of that account from inside the app (not just from a web
/// page). Fires the exact same self-serve deletion endpoint the website
/// uses (POST /api/accountDeletion/request), so all state transitions
/// stay in one place server-side.
///
/// The panel intentionally never confirms deletion in-app: the user has
/// to click the emailed link on the website. This mirrors the account-
/// deletion pipeline design and protects the account against an attacker
/// with brief physical access to the device.
///
/// SETUP (all Inspector drag-and-drop, same conventions as LoginPanel):
///   1. Build a Canvas with a panel containing:
///        - TMP_InputField "Confirm Email"  (user must retype the account email)
///        - Button         "Delete Account"
///        - Button         "Cancel"
///        - (optional) TextMeshProUGUI status label
///   2. Put this component on the panel root GameObject.
///   3. Drag each of those into the matching slot below.
///
/// You do NOT need to wire the Buttons' OnClick lists in the Inspector;
/// this component adds its own listeners in Awake.
/// </summary>
public class AccountSettingsPanel : MonoBehaviour
{
    [Header("Input Fields")]
    [Tooltip("Account email the user retypes as a confirmation step. Prevents accidental taps.")]
    [SerializeField] private TMP_InputField confirmEmailField;

    [Header("Buttons")]
    [Tooltip("Fires POST /api/accountDeletion/request against the entered email.")]
    [SerializeField] private Button deleteButton;
    [Tooltip("Optional. Closes the panel without doing anything.")]
    [SerializeField] private Button cancelButton;

    [Header("Optional")]
    [Tooltip("Optional. Shows the API's 'check your email' response or any errors.")]
    [SerializeField] private TextMeshProUGUI statusText;
    [Tooltip("Optional. The GameObject to hide when the user cancels. Defaults to this GameObject.")]
    [SerializeField] private GameObject panelRoot;

    private bool _busy;

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------
    private void Awake()
    {
        if (panelRoot == null) panelRoot = gameObject;

        if (deleteButton != null) deleteButton.onClick.AddListener(OnDeleteClicked);
        if (cancelButton != null) cancelButton.onClick.AddListener(OnCancelClicked);

        if (confirmEmailField != null)
            confirmEmailField.onSubmit.AddListener(_ => OnDeleteClicked());
    }

    private void OnDestroy()
    {
        if (deleteButton != null) deleteButton.onClick.RemoveListener(OnDeleteClicked);
        if (cancelButton != null) cancelButton.onClick.RemoveListener(OnCancelClicked);
    }

    // ------------------------------------------------------------------
    // Button handlers
    // ------------------------------------------------------------------
    private void OnDeleteClicked()
    {
        if (_busy) return;

        string email = (confirmEmailField != null ? confirmEmailField.text : "").Trim();

        if (string.IsNullOrWhiteSpace(email))
        {
            SetStatus("Type your account email to confirm.", isError: true);
            return;
        }

        if (!email.Contains("@") || !email.Contains("."))
        {
            SetStatus("That doesn't look like a valid email address.", isError: true);
            return;
        }

        SetBusy(true, "Requesting account deletion...");

        JadedBellesApiClient.Instance.RequestAccountDeletion(
            email,
            onSuccess: response =>
            {
                SetBusy(false, null);
                // The server returns the same "if that email exists, we sent a link"
                // shape regardless of whether the email matched. Surface it verbatim
                // so we don't leak whether the address is registered.
                string msg = response != null && !string.IsNullOrEmpty(response.message)
                    ? response.message
                    : "If an account exists for that email, we've sent a confirmation link. " +
                      "Click it within 24 hours to permanently delete your account.";
                SetStatus(msg);

                if (confirmEmailField != null)
                    confirmEmailField.text = "";
            },
            onError: error =>
            {
                SetBusy(false, null);
                SetStatus(string.IsNullOrEmpty(error) ? "Could not send confirmation email." : error, isError: true);
                Debug.LogWarning("[AccountSettingsPanel] Deletion request failed: " + error);
            });
    }

    private void OnCancelClicked()
    {
        if (_busy) return;
        Hide();
    }

    // ------------------------------------------------------------------
    // Show / hide
    // ------------------------------------------------------------------
    /// <summary>Opens the panel. Hook to a "Delete Account" button in your settings menu.</summary>
    public void Show()
    {
        if (panelRoot != null) panelRoot.SetActive(true);
        if (confirmEmailField != null) confirmEmailField.text = "";
        SetStatus("");
    }

    /// <summary>Closes the panel.</summary>
    public void Hide()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    // ------------------------------------------------------------------
    // Plumbing
    // ------------------------------------------------------------------
    private void SetBusy(bool busy, string message)
    {
        _busy = busy;

        if (deleteButton != null) deleteButton.interactable = !busy;
        if (cancelButton != null) cancelButton.interactable = !busy;
        if (confirmEmailField != null) confirmEmailField.interactable = !busy;

        if (!string.IsNullOrEmpty(message)) SetStatus(message);
    }

    private void SetStatus(string message, bool isError = false)
    {
        if (statusText == null)
        {
            if (isError) Debug.LogWarning("[AccountSettingsPanel] " + message);
            return;
        }

        statusText.text = message;
        statusText.color = isError ? new Color(1f, 0.42f, 0.42f) : new Color(0.75f, 0.78f, 0.85f);
    }
}
