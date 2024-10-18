
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
    public static MongerManager_Jobbified instance;

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
    private float _trailUpdateStopwatch;
    private float _physicsCheckStopwatch;


    private int _totalPointsPerMonger;
    private int _arraySizes;
    private TarPoolEntry[] _tarPoolEntries;
    private TransformAccessArray _allPointTransforms;
    private NativeArray<TarPoint> _allTarPoints;
    private NativeList<RaycastCommand> _physicsChecksRaycastCommands;
    private NativeHashMap<int, int> _mongerTarColliderIDToMockupMovementID;
    private Dictionary<int, MockupMovement> _mockupMovementIDToMockupMovement;
    private Dictionary<int, MongerTrail_Jobbified> _mongerTrailIDToMongerTrail;

    private List<ManagerIndex> _tarPointPhysicsChecks;
    private List<int> _physicsChecksIgnoredObjectIDs = new List<int>();

    private void Awake()
    {
        instance = this;

        _totalPointsPerMonger = Mathf.CeilToInt(pointLifetime) * 5;
        _arraySizes = _totalPointsPerMonger * mongerCount;
        _allTarPoints = new NativeArray<TarPoint>(_arraySizes, Allocator.Persistent);
        _allPointTransforms = new TransformAccessArray(_arraySizes);
        _physicsChecksRaycastCommands = new NativeList<RaycastCommand>(_arraySizes / 2, Allocator.Persistent);
        _mongerTarColliderIDToMockupMovementID = new NativeHashMap<int, int>(1, Allocator.Persistent);
        _mockupMovementIDToMockupMovement = new Dictionary<int, MockupMovement>();
        _mongerTrailIDToMongerTrail = new Dictionary<int, MongerTrail_Jobbified>();
        _tarPoolEntries = new TarPoolEntry[_arraySizes];
        _tarPointPhysicsChecks = new List<ManagerIndex>(_arraySizes / 2);

        int childrenInContainer = 0;
        int containerCount = 0;
        GameObject container = null;
        for(int i = 0; i < _arraySizes; i++)
        {
            _allTarPoints[i] = TarPoint.invalid;

            if(childrenInContainer >= _totalPointsPerMonger)
            {
                childrenInContainer = 0;
                container = null;
            }

            if(!container)
            {
                container = new GameObject($"SplotchContainer{containerCount}");
                containerCount++;
            }
            var instance = Instantiate(pointPrefab, container.transform);
            childrenInContainer++;
            _allPointTransforms.Add(instance.transform);

            var entry = new TarPoolEntry(i)
            {
                tiedGameObject = instance,
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

    public TarPoint RequestTarPoint(MongerTrail_Jobbified owner, Vector3 position, Vector3 normalDirection, out TarPoolEntry gameObjectForPoint)
    {
        int index = _allTarPoints.IndexOf(TarPoint.invalid);
        TarPoint tarPoint = new TarPoint(index)
        {
            pointLifetime = pointLifetime,
            totalLifetime = pointLifetime,
            normalDirection = normalDirection,
            remappedLifetime0to1 = 1,
            worldPosition = position,
            currentOwnerInstanceID = owner.GetInstanceID()
        };
        _allTarPoints[index] = tarPoint;

        gameObjectForPoint = _tarPoolEntries[index];
        gameObjectForPoint.isInPool = false;
        return tarPoint;
    }

    public void ReturnTarPoint(TarPoint tarPoint, TarPoolEntry gameObjectForPoint)
    {
        if (!tarPoint.isValid)
            return;

        if (tarPoint.managerIndex != gameObjectForPoint.managerPoolIndex)
            return;


        _allTarPoints[(int)tarPoint.managerIndex] = TarPoint.invalid;
        gameObjectForPoint.isInPool = true;
        _tarPoolEntries[(int)tarPoint.managerIndex] = gameObjectForPoint;
    }

    public void AddDetector(MongerTarDetector detector)
    {
        var colliderID = detector.boxCollider.GetInstanceID();
        var mockupMovementID = detector.tiedMovement.GetInstanceID();

        _mockupMovementIDToMockupMovement.Add(mockupMovementID, detector.tiedMovement);
        _mongerTarColliderIDToMockupMovementID.Add(colliderID, mockupMovementID);
    }

    public void AddMonger(MongerTrail_Jobbified monger)
    {
        _mongerTrailIDToMongerTrail.Add(monger.GetInstanceID(), monger);
    }

    public void RemoveMonger(MongerTrail_Jobbified monger)
    {
        _mongerTrailIDToMongerTrail.Remove(monger.GetInstanceID());
    }

    public void RemoveDetector(MongerTarDetector detector)
    {
        var colliderID = detector.boxCollider.GetInstanceID();
        var mockupMovementID = detector.tiedMovement.GetInstanceID();

        _mockupMovementIDToMockupMovement.Remove(mockupMovementID);
        _mongerTarColliderIDToMockupMovementID.Remove(colliderID);
    }


    private void FixedUpdate()
    {
        bool shouldPhysicsCheck = false;
        bool shouldTrailUpdate = false;
        NativeArray<RaycastHit> hitBuffer = default;
        NativeArray<int> processHitBuffer_output = default;

        JobHandle dependency = default;
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
                _tarPointPhysicsChecks.Clear();
                _physicsChecksRaycastCommands.Clear();
                for(int i = 0; i < _arraySizes; i++)
                {
                    var point = _allTarPoints[i];
                    if (!point.isValid)
                    {
                        continue;
                    }
                    _tarPointPhysicsChecks.Add(point.managerIndex);
                    var command = new RaycastCommand(point.worldPosition, point.normalDirection, 1, physicsCheckMask, 5);
                    _physicsChecksRaycastCommands.Add(command);
                }

                //The length is the total indvidual raycast commands, times 5, since the command has a max of 5 hits
                hitBuffer = new NativeArray<RaycastHit>(_physicsChecksRaycastCommands.Length * 5, Allocator.TempJob);

                //Schedule it.
                dependency = RaycastCommand.ScheduleBatch(_physicsChecksRaycastCommands, hitBuffer, _totalPointsPerMonger, dependency);

                //We need to process the hits now, we'll just care for the hit MockupMovements.

                //The output contains the mockup movement IDs, this way we can directly map raycasthit to the proper mockup movement, then iterate properly thru the results.
                processHitBuffer_output = new NativeArray<int>(hitBuffer.Length, Allocator.TempJob);

                //Schedule the job that will transform all the raycast hit into mockup movement IDs.
                dependency = new ProcessHitBufferJob
                {
                    colliderIDToMockupMovementID = _mongerTarColliderIDToMockupMovementID,
                    numberOfRaycastToProcess = 5,
                    output = processHitBuffer_output,
                    raycastHits = hitBuffer
                }.Schedule(_physicsChecksRaycastCommands.Length, _totalPointsPerMonger, dependency);
            }
        }

        if(doLifetimeReduction || doPointScaling)
        {
            using(_lifetimeAndScalingMarker.Auto())
            {
                if(doLifetimeReduction)
                {
                    TrailPointLifetimeJob lifetimeJob = new TrailPointLifetimeJob
                    {
                        deltaTime = deltaTime,
                        tarPoints = _allTarPoints
                    };
                    dependency = lifetimeJob.Schedule(_arraySizes, _totalPointsPerMonger, dependency);
                }

                if(doPointScaling)
                {
                    TrailPointVisualJob job = new TrailPointVisualJob
                    {
                        maxSize = new float3(1),
                        tarPoints = _allTarPoints,
                        totalLifetime = pointLifetime
                    };
                    dependency = job.Schedule(_allPointTransforms, dependency);
                }

            }
        }

        dependency.Complete();

        if(shouldPhysicsCheck)
        {
            for (int i = 0; i < _tarPointPhysicsChecks.Count; i++)
            {

                var pointManager = _tarPointPhysicsChecks[i];
                var point = GetPoint(pointManager);

                if (!point.isValid) //This shouldnt happen.
                    continue;

                if (!_mongerTrailIDToMongerTrail.TryGetValue(point.currentOwnerInstanceID, out var mongerTrailThatOwnsThePoint))
                {
                    continue;
                }

                var startIndex = i * 5;
                for(int j = 0; j < 5; j++)
                {
                    int outputIndex = startIndex + j;
                    var id = processHitBuffer_output[outputIndex];
                    if(id == 0) //we've hit the last one of our point.
                    {
                        break;
                    }

                    if(_physicsChecksIgnoredObjectIDs.Contains(id))
                    {
                        continue;
                    }

                    if(!_mockupMovementIDToMockupMovement.TryGetValue(id, out var mockupMovement))
                    {
                        continue;
                    }

                    if (mockupMovement == mongerTrailThatOwnsThePoint.mockupMovement)
                    {
                        _physicsChecksIgnoredObjectIDs.Add(id);
                        continue;
                    }

                    mockupMovement.number -= mongerTrailThatOwnsThePoint.damage;
                    _physicsChecksIgnoredObjectIDs.Add(id);
                }
            }
        }

        for(int i = 0; i < _mongerInstances.Count; i++)
        {
            _mongerInstances[i].UpdateFromManager();
        }

        if (hitBuffer.IsCreated)
            hitBuffer.Dispose();

        if (processHitBuffer_output.IsCreated)
            processHitBuffer_output.Dispose();
    }


    private void OnDestroy()
    {
        if (_allTarPoints.IsCreated)
            _allTarPoints.Dispose();

        if(_allPointTransforms.isCreated)
            _allPointTransforms.Dispose();

        if (_physicsChecksRaycastCommands.IsCreated)
            _physicsChecksRaycastCommands.Dispose();

        if (_mongerTarColliderIDToMockupMovementID.IsCreated)
            _mongerTarColliderIDToMockupMovementID.Dispose();

        instance = null;
    }

    private void OnDrawGizmos()
    {
        float x = spawnArea.x;
        float y = spawnArea.y;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(transform.position, new Vector3(x, 10, y));

    }

    internal TarPoint GetPoint(ManagerIndex managerIndex)
    {
        return _allTarPoints[(int)managerIndex];
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
        public ManagerIndex managerPoolIndex => _managerPoolIndex;
        private ManagerIndex _managerPoolIndex;

        internal TarPoolEntry(int index)
        {
            tiedGameObject = null;
            _isInPool = false;
            _managerPoolIndex = (ManagerIndex)index;
        }

        public bool Equals(TarPoolEntry other)
        {
            return managerPoolIndex == other.managerPoolIndex;
        }
    }

    public struct TarPoint : IEquatable<TarPoint>
    {
        public static readonly TarPoint invalid = new TarPoint(-1);

        public float3 worldPosition;
        public float3 normalDirection;
        public float pointLifetime;
        public float totalLifetime;
        public float remappedLifetime0to1;
        public int currentOwnerInstanceID;
        public ManagerIndex managerIndex => _managerIndex;
        private ManagerIndex _managerIndex;
        public bool isValid => !Equals(invalid);

        public bool Equals(TarPoint other)
        {
            return _managerIndex == other._managerIndex;
        }

        internal TarPoint(int index)
        {
            _managerIndex = (ManagerIndex)index;
            worldPosition = float3.zero;
            normalDirection = float3.zero;
            pointLifetime = 0;
            totalLifetime = 0;
            remappedLifetime0to1 = 0;
            currentOwnerInstanceID = -1;
        }
    }

    public enum ManagerIndex : int
    {
        Invalid = -1
    }
}