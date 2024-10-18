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
        _manager.AddMonger(this);
    }

    public void OnEnable()
    {
        if (!_manager)
            return;

        _manager.AddMonger(this);
    }


    public void OnDisable()
    {
        _manager.RemoveMonger(this);
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

        MongerManager_Jobbified.TarPoint tarPoint = _manager.RequestTarPoint(this, hit.point, hit.normal, out var poolEntry);

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

        for (int i = _points.Count - 1; i >= 0; i--)
        {
            var point = _points[i];
            var pointPos = point.worldPosition;


            int totalOverlaps = Physics.RaycastNonAlloc(pointPos, point.normalDirection, _physicsHits, 1, _manager.physicsCheckMask, QueryTriggerInteraction.Collide);
            for (int j = 0; j < totalOverlaps; j++)
            {
                var collider = _physicsHits[j].collider;
                if (!collider.TryGetComponent<MongerTarDetector>(out var mtd))
                {
                    continue;
                }

                var mm = mtd.tiedMovement;

                if (!mm)
                    continue;


                var collidedGameObject = mm.gameObject;
                if (!_ignoredObjects.Contains(collidedGameObject))
                {
                    _ignoredObjects.Add(collidedGameObject);
                }
            }
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
