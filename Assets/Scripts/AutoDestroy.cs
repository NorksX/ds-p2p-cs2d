using UnityEngine;

public class AutoDestroy : MonoBehaviour
{
    [SerializeField] private float lifetime = 0.05f;

    private void Start()
    {
        Destroy(gameObject, lifetime);
    }
}
