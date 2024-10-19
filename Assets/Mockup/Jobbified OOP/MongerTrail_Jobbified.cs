using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

public class MongerTrail_Jobbified : MonoBehaviour
{
    [NonSerialized]
    public new Transform transform;
    public int damage = 1;
    public MockupMovement mockupMovement;
    private RaycastHit[] _physicsHits = new RaycastHit[5];
    private List<MongerManager_Jobbified.TarPoolEntry> _poolEntries = new List<MongerManager_Jobbified.TarPoolEntry>();
    private List<MongerManager_Jobbified.TarPoint> _points = new List<MongerManager_Jobbified.TarPoint>();
    private List<GameObject> _ignoredObjects = new List<GameObject>();

    private MongerManager_Jobbified _manager;

    private void Awake()
    {
        transform = base.transform;
    }
    public void SetManager(MongerManager_Jobbified manager)
    {
        _manager = manager;
    }

    public void UpdateTrail(float deltaTime)
    {
        while(_points.Count > 0 && _points[0].pointLifetime <= 0)
        {
            RemovePoint(0);
        }
        AddPoint();
    }

    private void RemovePoint(int index)
    {
        var point = _points[index];
        var pointPool = _poolEntries[index];

        _manager.ReturnTarPoint(point, pointPool);
        _points.RemoveAt(index);
        _poolEntries.RemoveAt(index);
    }

    private void AddPoint()
    {
        if (!Physics.Raycast(transform.position, Vector3.down, out var hit, _manager.raycastLength, _manager.raycastMask))
        {
            return;
        }

        float yRot = UnityEngine.Random.Range(0, 360);
        MongerManager_Jobbified.TarPoint tarPoint = _manager.RequestTarPoint(this, hit.point, hit.normal, yRot, out var poolEntry);

        var pointTransform = poolEntry.tiedGameObject.transform;
        pointTransform.up = hit.normal;
        pointTransform.rotation *= Quaternion.Euler(0, yRot, 0);
        pointTransform.position = hit.point;
        _points.Add(tarPoint);
        _poolEntries.Add(poolEntry);
    }

    internal void UpdateFromManager()
    {
        for(int i = 0; i < _points.Count; i++)
        {
            var myPoint = _points[i];

            _points[i] = _manager.GetPoint(myPoint.managerIndex);
        }
    }
}
