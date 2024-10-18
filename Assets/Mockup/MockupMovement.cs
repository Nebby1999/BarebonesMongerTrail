using UnityEngine;
using System.Collections.Generic;

public class MockupMovement : MonoBehaviour
{
    public float movementSpeed;
    public float timeBetweenMovementChange;
    public new Rigidbody rigidbody;

    private float _timeBetweenMovementStopwatch;
    private Vector3 _chosenMovementVector;

    private void Start()
    {
        CreateMovementVector();
    }
    private void FixedUpdate()
    {
        _timeBetweenMovementStopwatch += Time.fixedDeltaTime;
        if (_timeBetweenMovementStopwatch > timeBetweenMovementChange)
        {
            _timeBetweenMovementStopwatch -= timeBetweenMovementChange + Random.Range(0, timeBetweenMovementChange);
            CreateMovementVector();
        }
        rigidbody.velocity = _chosenMovementVector * movementSpeed;
    }

    private void CreateMovementVector()
    {
        var random = Random.insideUnitCircle;
        _chosenMovementVector = new Vector3(random.x, 0, random.y).normalized;
    }
}