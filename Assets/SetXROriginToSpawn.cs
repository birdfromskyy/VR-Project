using System.Collections;
using UnityEngine;
using Unity.XR.CoreUtils;

public class SetXROriginToSpawn : MonoBehaviour
{
    [SerializeField] private Transform spawnPoint;

    private XROrigin xrOrigin;

    private void Awake()
    {
        xrOrigin = GetComponent<XROrigin>();
    }

    private IEnumerator Start()
    {
        // подождём немного, чтобы XR успел инициализироваться
        yield return null;

        if (xrOrigin == null || spawnPoint == null) yield break;

        // 1) Ставим риг на точку пола
        xrOrigin.transform.position = spawnPoint.position;

        // 2) Поворачиваем только по Y (чтобы смотреть туда же)
        var e = xrOrigin.transform.eulerAngles;
        e.y = spawnPoint.eulerAngles.y;
        xrOrigin.transform.eulerAngles = e;
    }
}
