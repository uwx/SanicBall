using System.Collections;
using System.Collections.Generic;
using Sanicball.Gameplay;
using UnityEngine;

public class RangRingSpawner : RangSpawner
{
    // implements a grid of 
    public int count = 8;
    public float radius = 16f;

    public bool collectAllAtOnce = false;
    public float collectIndividualDelay = 0.003f;
    public float collectAllDelay = 0.05f;

    [Range(0f, 360f)]
    public float maxDegree = 360f;

    protected override int RangCount => this.count;
    protected override Vector3 GetPositionCore(int idx)
        => transform.position
        + Mathf.Cos(idx * (Mathf.Deg2Rad * maxDegree) / count) * radius * transform.forward
        + Mathf.Sin(idx * (Mathf.Deg2Rad * maxDegree) / count) * radius * transform.right;

    public override void Start()
    {
        base.Start();
        //if (collectAllAtOnce)
        //{
        //    var rangs = this.RangCount;
        //    for (int i = 0; i < rangs; i++)
        //    {
        //        var rang = spawnedRangs[i].GetComponent<Collider>();
        //        Destroy(rang);
        //    }

        //    var collider = this.GetComponent<BoxCollider>();
        //    collider.enabled = true;
        //    collider.isTrigger = true;
        //    collider.size = new Vector3(radius * 2, 1, radius * 2);
        //}

        var collider = this.GetComponent<BoxCollider>();
        collider.enabled = true;
        collider.isTrigger = true;
        collider.size = new Vector3(radius * 2, radius * 2, radius * 2);
    }

    //public void OnTriggerEnter(Collider other)
    //{
    //    var bc = other.GetComponent<Ball>();
    //    if (bc != null)
    //    {
    //        Collider collider = GetComponent<Collider>();
    //        if (collider)
    //            collider.enabled = false;

    //        if (collectAllAtOnce)
    //        {
    //            StartCoroutine(CollectAll(bc));
    //        }
    //    }

    //}

    private IEnumerator CollectAll(Ball ball)
    {
        yield return new WaitForSeconds(collectAllDelay);

        var rangs = this.RangCount;
        for (int i = 0; i < rangs; i++)
        {
            var rang = spawnedRangs[i].GetComponent<Rang>();
            rang.StartCoroutine(rang.MagnetiseToBall(ball));
        }

    }
}
