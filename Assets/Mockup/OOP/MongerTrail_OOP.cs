using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

public class MongerTrail_OOP : MonoBehaviour
{
    [NonSerialized]
    public new Transform transform;
    private RaycastHit[] _physicsHits = new RaycastHit[5];
    private List<TarPoint> _points = new List<TarPoint>();
    private List<GameObject> _ignoredObjects = new List<GameObject>();

    private MongerManager_OOP _manager;
    private int _managerIndex;

    private void Awake()
    {
        transform = base.transform;
    }
    public void SetManager(MongerManager_OOP manager)
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
        if(point.pointTransform)
        {
            _manager.ReturnPoint(point.pointTransform.gameObject);
        }
        _points.RemoveAt(index);
    }

    private void AddPoint()
    {
        if (!Physics.Raycast(transform.position, Vector3.down, out var hit, _manager.raycastLength, _manager.raycastMask))
        {
            return;
        }

        TarPoint tarPoint = new TarPoint
        {
            worldPosition = hit.point,
            normalDirection = hit.normal,
            pointLifetime = _manager.pointLifetime,
            totalLifetime = _manager.pointLifetime,
            pointWidthDepth = new Vector3(5, 5)
        };

        var pointInstance = _manager.RequestPoint();
        var pointTransform = pointInstance.transform;
        pointTransform.up = hit.normal;
        pointTransform.position = hit.point;
        pointTransform.rotation = Quaternion.Euler(0, UnityEngine.Random.Range(0, 360), 0);
        tarPoint.pointTransform = pointTransform;
        _points.Add(tarPoint);
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


            int totalOverlaps = Physics.RaycastNonAlloc(pointPos, point.normalDirection, _physicsHits, 1, _manager.physicsCheckMask, QueryTriggerInteraction.Collide);
            for (int j = 0; j < totalOverlaps; j++)
            {
                var collider = _physicsHits[j].collider;
                if(!collider.TryGetComponent<MongerTarDetector>(out var mtd))
                {
                    continue;
                }

                var mm = mtd.tiedMovement;

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
            if (doLifetimeReduction)
                point.pointLifetime -= deltaTime;

            if(doPointScaling)
            {
                point.pointTransform.localScale = Vector3.Lerp(Vector3.zero, Vector3.one, Util.Remap(point.pointLifetime, 0, point.totalLifetime, 0, 1));
            }

            _points[i] = point;
        }
    }

    private struct TarPoint
    {
        public Vector3 worldPosition;
        public Vector3 normalDirection;
        public Vector2 pointWidthDepth;
        public float pointLifetime;
        public float totalLifetime;
        public Transform pointTransform;
    }
}
