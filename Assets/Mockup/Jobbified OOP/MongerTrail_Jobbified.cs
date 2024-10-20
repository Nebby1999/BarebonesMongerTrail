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

    private MongerManager_Jobbified _manager;

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
        if(_manager)
        {
            _manager.AddMonger(this);
        }
    }

    private void OnDisable()
    {
        _manager.RemoveMonger(this);
    }

    private void OnDestroy()
    {
        if (points.IsCreated)
            points.Dispose();
    }

    public void AddPoint(RaycastHit hit)
    {
        float yRot = UnityEngine.Random.Range(0, 360);
        MongerManager_Jobbified.TarPoint tarPoint = _manager.RequestTarPoint(this, hit.point, hit.normal, yRot, out var poolEntry);

        var pointTransform = poolEntry.tiedGameObject.transform;
        pointTransform.up = hit.normal;
        pointTransform.rotation *= Quaternion.Euler(0, yRot, 0);
        pointTransform.position = hit.point;
        points.Add(tarPoint);
    }
}
