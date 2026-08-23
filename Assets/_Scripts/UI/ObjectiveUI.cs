using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class ObjectiveUI : MonoBehaviour 
{
    [SerializeField] private Image objectiveImage;
    [SerializeField] private TextMeshProUGUI objectiveCountText;
    [SerializeField] private int objectiveCount;
    [SerializeField] private bool hasBeenInitialized = false;
    private IntEventListener listener;

    public void SetObjectiveImage(Sprite sprite)
    {
        objectiveImage.sourceImage = sprite;
    }

    public void SetObjectiveCount(int count)
    {
        if (hasBeenInitialized)
        {
            objectiveCount += count;
            if (objectiveCount < 0)
            {
                objectiveCount = 0;
            }
            objectiveCountText.text = objectiveCount.ToString();
            GameEventsManager.instance.gameEvents.ObjectiveProgressionChanged();
            return;


        }
        objectiveCount = count;
        objectiveCountText.text = count.ToString();
        hasBeenInitialized = true;
      
        Debug.Log($"Objective Count Set: {objectiveCount}");
    }

    public IntEventListener GetListener()
    {
        if (listener == null)
        {
            listener = GetComponent<IntEventListener>();
        }
        return listener;
    }

    public void SetChannel(EventChannel<int> channel)
    {

        listener = GetComponent<IntEventListener>();
        if (listener != null)
        {
            listener.SetEventChannel(channel);
        }
    }
}

