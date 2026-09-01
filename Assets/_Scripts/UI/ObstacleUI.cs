using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ObstacleUI : MonoBehaviour
{
    [SerializeField] private Image obstacleImage;
    [SerializeField] private TextMeshProUGUI obstacleCountText;
    [SerializeField] private int obstacleCount;
    [SerializeField] private bool hasBeenInitialized = false;   
    private IntEventListener listener;    

    public void SetObstacleImage(Sprite sprite)
    {
        obstacleImage.sprite = sprite;
    }

    public void SetObstacleCount(int count)
    {
        if(hasBeenInitialized)
        {
            obstacleCount += count;
            if (obstacleCount < 0)
            {
                obstacleCount = 0;
            }
            obstacleCountText.text = obstacleCount.ToString();
            return;
        }
        obstacleCount = count;
        obstacleCountText.text = count.ToString();
        hasBeenInitialized = true;
    }

    public IntEventListener GetListener()
    {
        if (listener == null)
        {
            listener = GetComponent<IntEventListener>();
        }
        return listener;
    }

    public void SetChannel(EventChannel<int> channel )
    {

        listener = GetComponent<IntEventListener>();
        if (listener != null)
        {
            listener.SetEventChannel(channel);
        }
    }
}

