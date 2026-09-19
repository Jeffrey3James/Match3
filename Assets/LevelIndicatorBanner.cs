using TMPro;
using UnityEngine;

public class LevelIndicatorBanner : MonoBehaviour
{
  [SerializeField] private TextMeshProUGUI levelText;

  private void Start () {
    levelText.text = PlayerHandler.instance.GetPlayerLevel().ToString();
  }
}
