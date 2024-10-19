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
    private List<TarPoint> _points = new List<TarPoint>();

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

        Quaternion boxcastOrientation = Quaternion.Euler(0, UnityEngine.Random.Range(0, 360), 0);
        TarPoint tarPoint = new TarPoint
        {
            worldPosition = hit.point,
            normalDirection = hit.normal,
            rotation = boxcastOrientation,
            pointLifetime = _manager.pointLifetime,
            totalLifetime = _manager.pointLifetime,
            pointWidthDepth = new Vector3(5, 5)
        };

        var pointInstance = _manager.RequestPoint();
        var pointTransform = pointInstance.transform;
        pointTransform.up = hit.normal;
        pointTransform.rotation *= boxcastOrientation;
        pointTransform.position = hit.point;
        tarPoint.pointTransform = pointTransform;
        _points.Add(tarPoint);
    }

    public void PhysicsCheck(float deltaTime)
    {
        if (_points.Count == 0)
            return;

        for(int i = _points.Count - 1; i >= 0; i--)
        {
            var point = _points[i];
            var pointPos = point.worldPosition;
            var xy = Vector2.Lerp(Vector2.zero, point.pointWidthDepth, Util.Remap(point.pointLifetime, 0, point.totalLifetime, 0, 1));

            //Util.DrawBoxCastBox(pointPos, new Vector3(xy.x / 2, 0.5f, xy.y / 2), point.rotation, point.normalDirection, 1, Color.red);
            if(Physics.BoxCast(pointPos, new Vector3(xy.x / 2, 0.5f, xy.y / 2), point.normalDirection, out var hit, point.rotation, 1, _manager.physicsCheckMask))
            {
                var collider = hit.collider;
                if (!collider.TryGetComponent<MockupMovement>(out var mm))
                {
                    continue;
                }

                var component = mm;

                if (!component)
                    continue;


                var collidedGameObject = mm.gameObject;
                //Debug.Log($"Trail of {gameObject}'s {i} point has collided with {collidedGameObject}");
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
        public Quaternion rotation;
        public Vector2 pointWidthDepth;
        public float pointLifetime;
        public float totalLifetime;
        public Transform pointTransform;
    }
}
