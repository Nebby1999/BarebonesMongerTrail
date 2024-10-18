
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
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

    private int _totalPointsPerMonger;
    private int _arraySizes;
    private TarPoolEntry[] _tarPoolEntries;
    private TransformAccessArray _allPointTransforms;
    private NativeArray<TarPoint> _allTarPoints;

    private void Awake()
    {
        _pointContainer = new GameObject("pointContainer");
        _pointContainer.transform.SetParent(transform);
        var containerTransform = _pointContainer.transform;

        _totalPointsPerMonger = Mathf.CeilToInt(pointLifetime) * 5;
        _arraySizes = _totalPointsPerMonger * mongerCount;
        _allTarPoints = new NativeArray<TarPoint>(_arraySizes, Allocator.Persistent);
        _allPointTransforms = new TransformAccessArray(_arraySizes, JobsUtility.JobWorkerMaximumCount / 2);
        _tarPoolEntries = new TarPoolEntry[_arraySizes];

        for(int i = 0; i < _arraySizes; i++)
        {
            _allTarPoints[i] = TarPoint.invalid;

            var instance = Instantiate(pointPrefab, containerTransform);
            _allPointTransforms.Add(instance.transform);

            var entry = new TarPoolEntry
            {
                tiedGameObject = instance,
                poolIndex = i,
                isInPool = true
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

    public TarPoint RequestTarPoint(Vector3 position, out TarPoolEntry gameObjectForPoint)
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

        gameObjectForPoint = _tarPoolEntries[index];
        gameObjectForPoint.isInPool = false;
        return tarPoint;
    }

    public void ReturnTarPoint(TarPoint tarPoint, TarPoolEntry gameObjectForPoint)
    {
        if (tarPoint.Equals(TarPoint.invalid))
            return;

        if (tarPoint.managerIndex != gameObjectForPoint.poolIndex)
            return;

        _allTarPoints[tarPoint.managerIndex] = TarPoint.invalid;
        gameObjectForPoint.isInPool = true;
        _tarPoolEntries[tarPoint.managerIndex] = gameObjectForPoint;
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
                JobHandle lifetimeJobHandle = default;
                if(doLifetimeReduction)
                {
                    TrailPointLifetimeJob lifetimeJob = new TrailPointLifetimeJob
                    {
                        deltaTime = deltaTime,
                        tarPoints = _allTarPoints
                    };
                    lifetimeJobHandle = lifetimeJob.Schedule(_arraySizes, _totalPointsPerMonger);
                }

                JobHandle pointScalingJobHandle = default;
                if(doPointScaling)
                {
                    TrailPointVisualJob job = new TrailPointVisualJob
                    {
                        maxSize = new float3(1),
                        tarPoints = _allTarPoints,
                        totalLifetime = pointLifetime
                    };
                    pointScalingJobHandle = job.Schedule(_allPointTransforms, lifetimeJobHandle);
                }

                if (!pointScalingJobHandle.IsCompleted)
                    pointScalingJobHandle.Complete();
            }
        }

        for(int i = 0; i < _mongerInstances.Count; i++)
        {
            _mongerInstances[i].UpdateFromManager();
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

    internal TarPoint GetPoint(int managerIndex)
    {
        return _allTarPoints[managerIndex];
    }

    /// <summary>
    /// NOT JOB SAFE
    /// </summary>
    public struct TarPoolEntry : IEquatable<TarPoolEntry>
    {
        public GameObject tiedGameObject;
        public bool isInPool
        {
            get => _isInPool;
            set
            {
                if(_isInPool != value)
                {
                    _isInPool = value;
                    tiedGameObject.SetActive(!value);
                }
            }
        }
        private bool _isInPool;
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