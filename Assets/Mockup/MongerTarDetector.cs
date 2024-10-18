using UnityEngine;

public class MongerTarDetector : MonoBehaviour
{
    public MockupMovement tiedMovement;
    public BoxCollider boxCollider;

    public void OnEnable()
    {
        MongerManager_Jobbified.instance.AddDetector(this);
    }

    public void OnDisable()
    {
        MongerManager_Jobbified.instance.RemoveDetector(this);
    }
}