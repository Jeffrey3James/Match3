using UnityEngine;
using UnityEngine.UI;

public class SettingsPanel : MonoBehaviour {
  [Header("Buttons on the Settings Panel")]
  [SerializeField] private Button soundBtn;
  [SerializeField] private Button musicBtn;
  [SerializeField] private Button hapticsBtn;
  [SerializeField] private Button chatBtn;
  [SerializeField] private Button notifsBtn;
  [SerializeField] private Button languageBtn;
  [SerializeField] private Button healthAndSupport;

  [SerializeField] private string language;
  [SerializeField] private string playerName;

  [SerializeField] private Button signOutBtn;
  [SerializeField] private Button deleteAccountBtn;

  [SerializeField] private AudioSource audioSource;

  private bool musicOn = true;
  private bool soundOn = true;
 

  private void Start() {
    WireButtons();
    }

  private void WireButtons() {
    if (soundBtn != null) {
      soundBtn.onClick.RemoveAllListeners();
      soundBtn.onClick.AddListener(ToggleSound);
      }

    if (musicBtn != null) { 
      musicBtn.onClick.RemoveAllListeners();
      soundBtn.onClick.AddListener(ToggleMusic);
      }
    if (hapticsBtn != null) { }
    if (chatBtn != null) { }
    if (notifsBtn != null) { }
    if (languageBtn != null) { }
    if (healthAndSupport != null) { }
    if (deleteAccountBtn != null) { }
    }

  private void ToggleSound() {
    soundOn = !soundOn;
    AudioListener.volume = soundOn ? 1f : 0f;
    }

  private void ToggleMusic() {
    musicOn = !musicOn;
    audioSource.loop = false;
    }
  }