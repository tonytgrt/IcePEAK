using UnityEngine;

public class SurfaceTag : MonoBehaviour
{
    [SerializeField] private SurfaceType surfaceType = SurfaceType.Ice;

    public SurfaceType Type => surfaceType;
}