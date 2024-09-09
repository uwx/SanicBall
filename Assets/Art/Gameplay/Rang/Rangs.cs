using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Rangs : MonoBehaviour
{
    public static Rangs Instance { get; private set; }

    public float rotationY = 0.0f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
        }
        else
        {
            Instance = this;
        }
    }

    public void Update()
    {
        rotationY += Time.deltaTime * 180.0f;
        if (rotationY > 360.0f)
            rotationY -= 360.0f;
    }

}
