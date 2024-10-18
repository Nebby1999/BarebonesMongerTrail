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
    private Collider[] _colliders = new Collider[50];
    private List<MongerManager_Jobbified.TarPoolEntry> _poolEntries = new List<MongerManager_Jobbified.TarPoolEntry>();
    private List<MongerManager_Jobbified.TarPoint> _points = new List<MongerManager_Jobbified.TarPoint>();
    private List<GameObject> _ignoredObjects = new List<GameObject>();

    private MongerManager_Jobbified _manager;
    private int _managerIndex;

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

        MongerManager_Jobbified.TarPoint tarPoint = _manager.RequestTarPoint(hit.point, out var poolEntry);

        var pointTransform = poolEntry.tiedGameObject.transform;
        pointTransform.up = hit.normal;
        pointTransform.position = hit.point;
        pointTransform.rotation = Quaternion.Euler(0, UnityEngine.Random.Range(0, 360), 0);
        _points.Add(tarPoint);
        _poolEntries.Add(poolEntry);
    }

    public void PhysicsCheck(float deltaTime)
    {
        if (_points.Count == 0)
            return;

        _ignoredObjects.Clear();
        _ignoredObjects.Add(gameObject);

        for(int i = _points.Count - 1; i >= 0; i--)
        {
            var point = _points[i];
            var pointPos = point.worldPosition;
            var xy = Vector2.Lerp(Vector2.zero, point.pointWidthDepth, Util.Remap(point.pointLifetime, 0, point.totalLifetime, 0, 1));

            int totalOverlaps = Physics.OverlapBoxNonAlloc(pointPos, new Vector3(xy.x / 2, 0.5f, xy.y / 2), _colliders, Quaternion.identity, _manager.physicsCheckMask);
            for(int j = 0; j < totalOverlaps; j++)
            {
                var collider = _colliders[j];
                if(!collider.TryGetComponent<MockupMovement>(out var mm))
                {
                    continue;
                }

                if (!mm)
                    continue;


                var collidedGameObject = mm.gameObject;
                if(!_ignoredObjects.Contains(collidedGameObject))
                {
                    _ignoredObjects.Add(collidedGameObject);
                }
            }
        }
    }

    public void PointUpdate(float deltaTime, bool doLifetimeReduction, bool doPointScaling)
    {
        for(int i = _points.Count - 1; i >= 0; i--)
        {
            var point = _points[i];
            var poolEntry = _poolEntries[i];

            if (doLifetimeReduction)
                point.pointLifetime -= deltaTime;

            if(doPointScaling)
            {
                poolEntry.tiedGameObject.transform.localScale = Vector3.Lerp(Vector3.zero, Vector3.one, Util.Remap(point.pointLifetime, 0, point.totalLifetime, 0, 1));
            }

            _points[i] = point;
            _poolEntries[i] = poolEntry;
        }
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
