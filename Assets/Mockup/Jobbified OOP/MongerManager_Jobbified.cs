
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Jobs;

public class MongerManager_Jobbified : MonoBehaviour
{
    static readonly ProfilerMarker _trailUpdateMarker = new ProfilerMarker(ProfilerCategory.Scripts, "MongerTrail_Jobbified.TrailUpdates");
    static readonly ProfilerMarker _physicsChecksMarker = new ProfilerMarker(ProfilerCategory.Scripts, "MongerTrail_Jobbified.PhysicsChecks");
    static readonly ProfilerMarker _lifetimeAndScalingMarker = new ProfilerMarker(ProfilerCategory.Scripts, "MongerTrail_Jobbified.LifetimeAndScalings");


    [Header("Sub-Behaviour Toggles")]
    public bool doTrailUpdates = true;
    public bool doPhysicsChecks = true;
    public bool doLifetimeReduction = true;
    public bool doPointScaling = true;
    
    [Header("Spawning Settings")]
    public MongerTrail_Jobbified mongerPrefab;
    public int mongerCount;
    public Vector2 spawnArea;

    [Header("Global Monger Data")]
    public GameObject pointPrefab;
    public LayerMask raycastMask;
    public float raycastLength;
    public float pointLifetime;
    public float timeBetweenTrailUpdates;
    public float timeBetweenPhysicsChecks;
    public LayerMask physicsCheckMask;

    private List<MongerTrail_Jobbified> _mongerInstances = new List<MongerTrail_Jobbified>();
    private Queue<GameObject> _pointPool = new Queue<GameObject>();
    private float _trailUpdateStopwatch;
    private float _physicsCheckStopwatch;
    private GameObject _pointContainer;

    private TarPoolEntry[] _tarPoolEntries;
    private TransformAccessArray _allPointTransforms;
    private NativeArray<TarPoint> _allTarPoints;

    private void Awake()
    {
        _pointContainer = new GameObject("pointContainer");
        _pointContainer.transform.SetParent(transform);

        int totalPointsPerMonger = Mathf.CeilToInt(pointLifetime) * 5;
        _allTarPoints = new NativeArray<TarPoint>(totalPointsPerMonger * mongerCount, Allocator.Persistent);
        for(int i = 0; i < _allTarPoints.Length; i++)
        {
            _allTarPoints[i] = TarPoint.invalid;
        }

        var containerTransform = _pointContainer.transform;
        _allPointTransforms = new TransformAccessArray(totalPointsPerMonger * mongerCount, JobsUtility.JobWorkerMaximumCount / 2);
        _tarPoolEntries = new TarPoolEntry[totalPointsPerMonger * mongerCount];

        for(int i = 0; i < _tarPoolEntries.Length; i++)
        {
            var instance = Instantiate(pointPrefab, containerTransform);
            _allPointTransforms[i] = instance.transform;

            var entry = new TarPoolEntry
            {
                isInPool = false,
                tiedGameObject = instance,
                poolIndex = i
            };
            _tarPoolEntries[i] = entry;
        }
    }

    private void Start()
    {
        var transform = this.transform;
        float myX = transform.position.x;
        float myZ = transform.position.z;

        float halfAreaX = spawnArea.x / 2;
        float halfAreaZ = spawnArea.y / 2;
        for(int i = 0; i < mongerCount; i++)
        {
            float mongerPosX = UnityEngine.Random.Range(-halfAreaX, halfAreaX);
            float mongerPosZ = UnityEngine.Random.Range(-halfAreaZ, halfAreaZ);

            Vector3 spawnPos = new Vector3(mongerPosX + myX, 2, mongerPosZ + myZ);
            var instance = Instantiate(mongerPrefab, spawnPos, Quaternion.identity, transform);
            instance.SetManager(this);
            _mongerInstances.Add(instance);
        }
    }

    public TarPoint RequestTarPoint(Vector3 position)
    {
        TarPoint tarPoint = new TarPoint
        {
            pointLifetime = pointLifetime,
            totalLifetime = pointLifetime,
            pointWidthDepth = new float2(5f),
            worldPosition = position,
        };
        int index = _allTarPoints.IndexOf(TarPoint.invalid);
        tarPoint.managerIndex = index;
        _allTarPoints[index] = tarPoint;
        return tarPoint;
    }

    public void ReturnTarPoint(TarPoint tarPoint)
    {
        if (tarPoint.Equals(TarPoint.invalid))
            return;

        _allTarPoints[tarPoint.managerIndex] = TarPoint.invalid;
    }

    public GameObject RequestPoint()
    {
        if(_pointPool.TryDequeue(out var point))
        {
            point.SetActive(true);
            point.transform.localScale = Vector3.one;
            return point;
        }
        return Instantiate(pointPrefab, _pointContainer.transform);
    }

    public void ReturnPoint(GameObject obj)
    {
        obj.SetActive(false);
        _pointPool.Enqueue(obj);
    }

    private void FixedUpdate()
    {
        bool shouldPhysicsCheck = false;
        bool shouldTrailUpdate = false;

        float deltaTime = Time.fixedDeltaTime;
        _physicsCheckStopwatch += doPhysicsChecks ? deltaTime : 0;
        _trailUpdateStopwatch += doTrailUpdates ? deltaTime : 0;

        if(_physicsCheckStopwatch > timeBetweenPhysicsChecks)
        {
            _physicsCheckStopwatch -= timeBetweenPhysicsChecks;
            shouldPhysicsCheck = true;
        }

        if(_trailUpdateStopwatch > timeBetweenTrailUpdates)
        {
            _trailUpdateStopwatch -= timeBetweenTrailUpdates;
            shouldTrailUpdate = true;
        }

        if(shouldTrailUpdate)
        {
            using(_trailUpdateMarker.Auto())
            {
                for(int i = 0; i < _mongerInstances.Count; i++)
                {
                    _mongerInstances[i].UpdateTrail(deltaTime);
                }
            }
        }

        if(shouldPhysicsCheck)
        {
            using(_physicsChecksMarker.Auto())
            {
                for(int i = 0; i < _mongerInstances.Count; i++)
                {
                    _mongerInstances[i].PhysicsCheck(deltaTime);
                }
            }
        }

        if(doLifetimeReduction || doPointScaling)
        {
            using(_lifetimeAndScalingMarker.Auto())
            {
                for(int i = 0; i < _mongerInstances.Count; i++)
                {
                    _mongerInstances[i].PointUpdate(deltaTime, doLifetimeReduction, doPointScaling);
                }
            }
        }
    }

    private void OnDestroy()
    {
        if (_allTarPoints.IsCreated)
            _allTarPoints.Dispose();

        if(_allPointTransforms.isCreated)
            _allPointTransforms.Dispose();

        _pointContainer = null;
    }

    private void OnDrawGizmos()
    {
        float x = spawnArea.x;
        float y = spawnArea.y;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(transform.position, new Vector3(x, 10, y));

    }

    /// <summary>
    /// NOT JOB SAFE
    /// </summary>
    public struct TarPoolEntry : IEquatable<TarPoolEntry>
    {
        public GameObject tiedGameObject;
        public bool isInPool;
        public int poolIndex;

        public bool Equals(TarPoolEntry other)
        {
            return poolIndex == other.poolIndex;
        }
    }

    public struct TarPoint : IEquatable<TarPoint>
    {
        public static readonly TarPoint invalid = new TarPoint() { managerIndex = -1 };

        public float3 worldPosition;
        public float2 pointWidthDepth;
        public float pointLifetime;
        public float totalLifetime;
        public int managerIndex;

        public bool Equals(TarPoint other)
        {
            return managerIndex == other.managerIndex;
        }
    }
}