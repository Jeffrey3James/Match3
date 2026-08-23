using System.Threading.Tasks;
using Unity.Services.Authentication;
using UnityEngine;
using UnityEngine.UI;

public class DummyPlayerDataSaver : MonoBehaviour
{
    [SerializeField] private Button saveButton  ;

    private void Start()
    {
        saveButton.onClick.AddListener(OnSaveButtonClicked);
    }

    private void OnSaveButtonClicked()
    {
        _ = SavePlayerDataAsyn();
    }

    private async Task SavePlayerDataAsyn()
    {
        await CloudSaveManager.instance.UpdatePlayerData();
        Debug.Log("Player data saved successfully.");
    }
}
