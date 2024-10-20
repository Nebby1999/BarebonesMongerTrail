using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UIElements;

public class MongerTrail_Jobbified : MonoBehaviour
{
    [NonSerialized]
    public new Transform transform;
    public int damage = 1;
    public MockupMovement mockupMovement;
    private RaycastHit[] _physicsHits = new RaycastHit[5];
    public NativeList<MongerManager_Jobbified.TarPoint> points;
    private List<MongerManager_Jobbified.TarPoint> _points = new List<MongerManager_Jobbified.TarPoint>();
    private List<MongerManager_Jobbified.TarPoolEntry> _poolEntries = new List<MongerManager_Jobbified.TarPoolEntry>();
    private List<GameObject> _ignoredObjects = new List<GameObject>();

    private MongerManager_Jobbified _manager;
    private int _myIndex;

    private void Awake()
    {
        transform = base.transform;
    }
    public void SetManager(MongerManager_Jobbified manager)
    {
        _manager = manager;
        points = new NativeList<MongerManager_Jobbified.TarPoint>(manager.totalPointsPerMonger, Allocator.Persistent);
        manager.AddMonger(this);
    }

    private void OnEnable()
    {
        if(_manager && _myIndex == -1)
        {
            _manager.AddMonger(this);
        }
    }

    private void OnDisable()
    {
        for (int i = _points.Count - 1; i >= 0; i--)
        {
            RemovePoint(i);
        }

        _manager.RemoveMonger(this);
    }

    private void OnDestroy()
    {
        if (points.IsCreated)
            points.Dispose();
    }

    public void UpdateTrail(float deltaTime)
    {
        while(_points.Count > 0 && points[0].pointLifetime <= 0)
        {
            RemovePoint(0);
        }
        AddPoint();
    }

    private void RemovePoint(int index)
    {
        var point = points[index];
        var pointPool = _poolEntries[index];

        _manager.ReturnTarPoint(point, pointPool);
        points.RemoveAt(index);
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
        points.Add(tarPoint);
        _poolEntries.Add(poolEntry);
    }

    internal void UpdateFromManager()
    {
        for(int i = 0; i < points.Length; i++)
        {
            var myPoint = points[i];

            points[i] = _manager.GetPoint(myPoint.managerIndex);
        }
    }
}
