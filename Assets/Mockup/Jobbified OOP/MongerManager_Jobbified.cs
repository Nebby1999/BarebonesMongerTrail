
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Xml.Linq;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Jobs;

public class MongerManager_Jobbified : MonoBehaviour
{
    public static MongerManager_Jobbified instance;

    static readonly ProfilerMarker _trailUpdateMarker = new ProfilerMarker(ProfilerCategory.Scripts, "MongerTrail_Jobbified.TrailUpdates");
    static readonly ProfilerMarker _physicsChecksPreJobMarker = new ProfilerMarker(ProfilerCategory.Scripts, "MongerTrail_Jobbified.PhysicsChecksPreJob");
    static readonly ProfilerMarker _physicsChecksPostJobMarker = new ProfilerMarker(ProfilerCategory.Scripts, "MongerTrail_Jobbified.PhysicsChecksPostJob");
    static readonly ProfilerMarker _lifetimeAndScalingMarker = new ProfilerMarker(ProfilerCategory.Scripts, "MongerTrail_Jobbified.LifetimeAndScalings");


    [Header("Sub-Behaviour Toggles")]
    public bool doTrailUpdates = true;
    public bool doPhysicsChecks = true;
    public bool doLifetimeReduction = true;
    public bool doPointScaling = true;
    
    [Header("Spawning Settings")]
    public MongerTrail_Jobbified mongerPrefab;
    public Vector2 spawnArea;

    [Header("Global Monger Data")]
    public GameObject pointPrefab;
    public LayerMask raycastMask;
    public float raycastLength;
    public float pointLifetime;
    public float timeBetweenTrailUpdates;
    public float timeBetweenPhysicsChecks;
    public LayerMask physicsCheckMask;

    private Dictionary<int, MongerTrail_Jobbified> _instanceIDToMonger = new Dictionary<int, MongerTrail_Jobbified>();
    private List<MongerTrail_Jobbified> _mongerInstances = new List<MongerTrail_Jobbified>();
    private float _trailUpdateStopwatch;
    private float _physicsCheckStopwatch;

    public int totalPointsPerMonger => _totalPointsPerMonger;
    private int _totalPointsPerMonger;
    private List<TarPoolEntry> _tarPoolEntries;
    private TransformAccessArray _allPointTransforms;
    private NativeList<TarPoint> _allTarPoints;
    private NativeQueue<int> _invalidTarPointIndices;
    private NativeList<ManagerIndex> _activeTarPoints;

    private int _GUI_addMongersCount = 1;

    private int _poolCounts;
    private int _currentPoolChildCount;
    private GameObject _currentPoolObject;

    private void Awake()
    {
        instance = this;

        _totalPointsPerMonger = Mathf.CeilToInt(pointLifetime) * 5;
        _tarPoolEntries = new List<TarPoolEntry>();
        _allPointTransforms = new TransformAccessArray(0);
        _allTarPoints = new NativeList<TarPoint>(Allocator.Persistent);
        _invalidTarPointIndices = new NativeQueue<int>(Allocator.Persistent);
        _activeTarPoints = new NativeList<ManagerIndex>(Allocator.Persistent);
    }

    public void AddMonger(MongerTrail_Jobbified trail)
    {
        _mongerInstances.Add(trail);
        _instanceIDToMonger.Add(trail.GetInstanceID(), trail);
    }

    public void RemoveMonger(MongerTrail_Jobbified trail)
    {
        _mongerInstances.Remove(trail);
        _instanceIDToMonger.Remove(trail.GetInstanceID());
    }
    public TarPoint RequestTarPoint(MongerTrail_Jobbified owner, Vector3 position, Vector3 normalDirection, float yRotation, out TarPoolEntry gameObjectForPoint)
    {
        int index = GetFreeTarPointIndex();
        TarPoint tarPoint = new TarPoint(index)
        {
            pointLifetime = pointLifetime,
            totalLifetime = pointLifetime,
            normalDirection = normalDirection,
            rotation = quaternion.EulerXYZ(0, yRotation, 0),
            pointWidthDepth = new float2(5),
            remappedLifetime0to1 = 1,
            worldPosition = position,
            currentOwnerInstanceID = owner.GetInstanceID()
        };
        _allTarPoints[index] = tarPoint;

        gameObjectForPoint = GetTarPoolEntryAtIndex(index);
        gameObjectForPoint.isInPool = false;
        return tarPoint;
    }

    private int GetFreeTarPointIndex()
    {
        int index = -1;
        //Use the stash of invalid indices first, should be considerably quicker.
        if (_invalidTarPointIndices.TryDequeue(out index))
        {
            return index;
        }

        _allTarPoints.Add(TarPoint.invalid);
        index = _allTarPoints.Length - 1;
        return index;
    }

    private TarPoolEntry GetTarPoolEntryAtIndex(int index)
    {
        if(index >= _tarPoolEntries.Count)
        {
            _tarPoolEntries.Add(new TarPoolEntry(index));
            index = _tarPoolEntries.Count - 1;
        }

        var entry = _tarPoolEntries[index];
        if (!entry.tiedGameObject)
        {
            if (_currentPoolChildCount >= _totalPointsPerMonger)
            {
                _currentPoolChildCount = 0;
                _currentPoolObject = null;
            }
            if (!_currentPoolObject)
            {
                _currentPoolObject = new GameObject($"SplotchContainer{_poolCounts}");
                _poolCounts++;
            }

            var instance = Instantiate(pointPrefab, _currentPoolObject.transform);
            _currentPoolChildCount++;
            _allPointTransforms.Add(instance.transform);

            entry = new TarPoolEntry(index)
            {
                tiedGameObject = instance,
                isInPool = true
            };
            _tarPoolEntries.Insert(index, entry);
        }
        else //Game object already exists, we just need to add it back to the all point transforms.
        {
            _allPointTransforms[index] = entry.cachedTransform;
        }
        return entry;
    }

    public void ReturnTarPoint(TarPoint tarPoint, TarPoolEntry gameObjectForPoint)
    {
        if (!tarPoint.isValid)
            return;

        if (tarPoint.managerIndex != gameObjectForPoint.managerPoolIndex)
            return;

        int index = (int)tarPoint.managerIndex;

        //This index is being freed, add it to the stash so another monger can use it.
        _invalidTarPointIndices.Enqueue(index);
        _allTarPoints[index] = TarPoint.invalid;
        gameObjectForPoint.isInPool = true;
        //Maybe remove at swap back?
        _allPointTransforms[index] = null;
        _tarPoolEntries[index] = gameObjectForPoint;
    }

    private void FixedUpdate()
    {
        if (_mongerInstances.Count == 0)
            return;

        int allTarPoints = 0;
        bool shouldPhysicsCheck = false;
        bool shouldTrailUpdate = false;
        NativeArray<BoxcastCommand> physicsChecksBoxcastCommands = default;
        NativeArray<RaycastHit> physicsChecksHitBuffer = default;
        NativeList<ManagerIndex> pointsThatCollidedWithSomething = default;

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

        //TODO: Jobify this process
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

        allTarPoints = _allTarPoints.Length;
        //Our jobs should only affect tar points who's indices are not invalid. these active points are also used on the lifetime and size jobs so we need to run it each fixed update. This is done because some points may be allocated in memory but not used (IE: a monger that spawned them was destroyed)
        //Unlike all other jobs, we need to complete this immediatly, since we need to know the length of the points we should modify prior to scheduling the actual heavy lifting jobs.
        _activeTarPoints.Clear();
        if(_activeTarPoints.Capacity <= allTarPoints) //Ensure we have enough capacity prior to using the parallel writer.
        {
            _activeTarPoints.SetCapacity(allTarPoints);
        }

        new GetActiveTarPointsJob
        {
            activePoints = _activeTarPoints.AsParallelWriter(),
            allTarPoints = _allTarPoints,
        }.Schedule(allTarPoints, _totalPointsPerMonger, dependency).Complete();

        int innerloopBatchCount = _activeTarPoints.Length / _mongerInstances.Count;

        if(shouldPhysicsCheck)
        {
            //We need to check if the active points have overlapping objects
            using(_physicsChecksPreJobMarker.Auto())
            {
                physicsChecksBoxcastCommands = new NativeArray<BoxcastCommand>(_activeTarPoints.Length, Allocator.TempJob);
                //Write the boxcasts
                dependency = new WriteBoxcastCommandsJob
                {
                    output = physicsChecksBoxcastCommands,
                    physicsScene = Physics.defaultPhysicsScene,
                    physicsCheckMask = physicsCheckMask,
                    tarPoints = _allTarPoints
                }.Schedule(_activeTarPoints.Length, innerloopBatchCount, dependency);

                //Check for any colliders the points have collided with.
                physicsChecksHitBuffer = new NativeArray<RaycastHit>(physicsChecksBoxcastCommands.Length, Allocator.TempJob);
                dependency = BoxcastCommand.ScheduleBatch(physicsChecksBoxcastCommands, physicsChecksHitBuffer, innerloopBatchCount, dependency);

                //We should filter out the points that didnt hit anything.
                pointsThatCollidedWithSomething = new NativeList<ManagerIndex>(_activeTarPoints.Capacity / 2, Allocator.TempJob);
                dependency = new FilterManagerIndicesThatDidntCollideWithAnythingJob
                {   
                    input = _allTarPoints,
                    output = pointsThatCollidedWithSomething.AsParallelWriter(),
                    raycastHits = physicsChecksHitBuffer
                }.Schedule(_activeTarPoints.Length, innerloopBatchCount, dependency);
            }
        }

        if((doLifetimeReduction || doPointScaling) && _allPointTransforms.length > 0)
        {
            using(_lifetimeAndScalingMarker.Auto())
            {
                if(doLifetimeReduction)
                {
                    TrailPointLifetimeJob lifetimeJob = new TrailPointLifetimeJob
                    {
                        deltaTime = deltaTime,
                        tarPoints = _allTarPoints,
                    };
                    dependency = lifetimeJob.Schedule(allTarPoints, innerloopBatchCount, dependency);
                }

                if(doPointScaling)
                {
                    TrailPointVisualJob job = new TrailPointVisualJob
                    {
                        maxSize = new float3(1),
                        tarPoints = _allTarPoints,
                        totalLifetime = pointLifetime,
                    };
                    dependency = job.Schedule(_allPointTransforms, dependency);
                }

            }
        }

        dependency.Complete();

        if(shouldPhysicsCheck)
        {
            using(_physicsChecksPostJobMarker.Auto())
            {
                for(int i = 0; i < pointsThatCollidedWithSomething.Length; i++)
                {
                    var managerIndex = pointsThatCollidedWithSomething[i];
                    var collider = physicsChecksHitBuffer[(int)managerIndex].collider;

                    if (!collider.TryGetComponent<MockupMovement>(out var mm))
                    {
                        continue;
                    }

                    var component = mm;

                    if (!component)
                        continue;


                    var collidedGameObject = mm.gameObject;
                    Debug.Log($"{_instanceIDToMonger[GetPoint(managerIndex).currentOwnerInstanceID].gameObject}'s point is colliding with {mm.gameObject}");
                }
            }
        }

        NativeArray<JobHandle> updateFromManagerHandles = new NativeArray<JobHandle>(_mongerInstances.Count, Allocator.Temp);
        for (int i = 0; i < _mongerInstances.Count; i++)
        {
            var mongerInstance = _mongerInstances[i];
            updateFromManagerHandles[i] = new UpdateFromManagerJob
            {
                allTarPoints = _allTarPoints,
                myTarPoints = mongerInstance.points
            }.Schedule(mongerInstance.points.Length, default);
        }
        JobHandle.CompleteAll(updateFromManagerHandles);

        if (physicsChecksHitBuffer.IsCreated)
            physicsChecksHitBuffer.Dispose();

        if (pointsThatCollidedWithSomething.IsCreated)
            pointsThatCollidedWithSomething.Dispose();

        if (physicsChecksBoxcastCommands.IsCreated)
            physicsChecksBoxcastCommands.Dispose();

        if (updateFromManagerHandles.IsCreated)
            updateFromManagerHandles.Dispose();
    }


    private void OnDestroy()
    {
        if (_allTarPoints.IsCreated)
            _allTarPoints.Dispose();

        if(_allPointTransforms.isCreated)
            _allPointTransforms.Dispose();


        if (_activeTarPoints.IsCreated)
            _activeTarPoints.Dispose();

        if (_invalidTarPointIndices.IsCreated)
            _invalidTarPointIndices.Dispose();

        instance = null;
    }

    private void OnDrawGizmos()
    {
        float x = spawnArea.x;
        float y = spawnArea.y;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(transform.position, new Vector3(x, 10, y));
    }

    private void OnGUI()
    {
        GUILayout.BeginHorizontal("box");
        if (GUILayout.Button("Add"))
        {
            var transform = this.transform;
            float myX = transform.position.x;
            float myZ = transform.position.z;

            float halfAreaX = spawnArea.x / 2;
            float halfAreaZ = spawnArea.y / 2;
            for (int i = 0; i < _GUI_addMongersCount; i++)
            {
                float mongerPosX = UnityEngine.Random.Range(-halfAreaX, halfAreaX);
                float mongerPosZ = UnityEngine.Random.Range(-halfAreaZ, halfAreaZ);

                Vector3 spawnPos = new Vector3(mongerPosX + myX, 2, mongerPosZ + myZ);
                var instance = Instantiate(mongerPrefab, spawnPos, Quaternion.identity, transform);
                instance.SetManager(this);
            }
        }
        string s = GUILayout.TextField(_GUI_addMongersCount.ToString());
        if(GUI.changed)
        {
            if(int.TryParse(s, out _GUI_addMongersCount))
            {

            }
        }
        GUILayout.Label("Monger(s)");
        GUILayout.EndHorizontal();
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
        public GameObject tiedGameObject
        {
            get => _tiedGameObject;
            set
            {
                _tiedGameObject = value;
                cachedTransform = value.transform;
            }
        }
        private GameObject _tiedGameObject;

        public Transform cachedTransform;
        public bool isInPool
        {
            get => _isInPool;
            set
            {
                if (!tiedGameObject)
                {
                    _isInPool = false;
                    return;
                }

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
            _tiedGameObject = null;
            cachedTransform = null;
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
        public quaternion rotation;
        public float2 pointWidthDepth;
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
            rotation = quaternion.identity;
            pointWidthDepth = float2.zero;
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